#nullable disable
// 层：Hook 引擎 —— MonoMod 写法的游戏方法钩子。
//
// 用法（和 MonoMod.RuntimeDetour 的 Hook 一个形状）：
//
//     using Pylon.Hooking;
//     using var hook = new Hook(
//         typeof(Main).GetMethod(nameof(Main.DrawMenu), new[] { typeof(GameTime) }),
//         new Action<Action<GameTime>, GameTime>((orig, time) =>
//         {
//             orig(time);                 // 调原函数
//             // …原函数之后想做的事…
//         }));
//
// 约定：handler 第一个参数是 orig（与目标方法同签名的委托），实例方法跟在 orig 后面是 self。
// 生成的 On.* 程序集也是走这里（见 HookEndpoint）。
using System;
using System.Reflection;

namespace Il2CppInterop.RuntimeDetour
{
    /// <summary>把一个托管处理器挂到游戏（il2cpp）方法上，提供 orig 直接调原函数。</summary>
    public sealed class Hook : IDisposable
    {
        private readonly HookPlan _plan;
        private readonly Delegate _handler;

        private int _slot = -1;
        private IntPtr _entry;
        private Delegate _orig;
        private HookChain _chain;
        private HookNode _node;
        private bool _applied;

        /// <summary>挂钩并立即生效（等价 MonoMod 的 applyByDefault = true）。</summary>
        public Hook(MethodBase target, Delegate handler) : this(target, handler, true) { }

        public Hook(MethodBase target, Delegate handler, bool applyByDefault)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handler = handler;
            _plan = HookPlan.Create(target, handler);
            if (applyByDefault) Apply();
        }

        /// <summary>目标方法的标签（Type.Method）。</summary>
        public string Label => _plan.Label;

        public bool IsApplied => _applied;

        /// <summary>orig 委托：调用它会走完剩余链，最后一次落到真跳板。</summary>
        public Delegate Original => _orig;

        /// <summary>这条 hook 挂的目标方法。</summary>
        public MethodInfo Target => _plan.Target;

        /// <summary>把钩子装上（幂等）。Dobby 不可用时抛异常。</summary>
        public void Apply()
        {
            if (_applied) return;
            var backend = DetourBackend.Current;
            if (backend == null)
                throw new InvalidOperationException(
                    "没有设置 DetourBackend：宿主必须在启动时提供原生 detour 实现（Pylon → 内核 Dobby）");
            if (!backend.IsAvailable)
                throw new InvalidOperationException("detour 后端不可用（libdobby.so 未加载），无法挂钩 " + _plan.Label);

            _slot = HookState.AllocateSlot(_handler);
            _orig = HookStub.BuildOrig(_plan, _slot);
            HookState.SetOrigDelegate(_slot, _orig);

            var (stub, entry) = HookStub.BuildEntry(_plan, _slot);
            HookState.KeepAlive.Add(stub);
            _entry = entry;

            _chain = HookState.GetOrCreateChain(_plan.FunctionPtr);

            if (_chain.Nodes.Count == 0)
            {
                // 链头：真正交给后端，拿回跳板
                var rc = backend.Apply(_plan.FunctionPtr, _entry, out var trampoline);
                if (rc != 0 || trampoline == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"挂钩失败 rc={rc} 跳板=0x{trampoline.ToInt64():x}（目标 0x{_plan.FunctionPtr.ToInt64():x}）");
                _chain.Trampoline = trampoline;
                HookState.SetOrig(_slot, _chain.Trampoline);
            }
            else
            {
                // 追加到链尾：新节点接住链尾原来的下一跳，链尾改为跳到新节点
                var tail = _chain.Nodes[_chain.Nodes.Count - 1];
                HookState.SetOrig(_slot, HookState.GetOrig(tail.Slot));
                HookState.SetOrig(tail.Slot, _entry);
            }

            _node = new HookNode(_slot, _entry) { Owner = this };
            _chain.Nodes.Add(_node);
            _applied = true;
            HookState.Active.Add(this);

            HookLog.Say($"[hook] 已挂 {_plan.Label}（槽 {_slot}，链第 {_chain.Nodes.Count} 位）");
        }

        /// <summary>撤销（幂等）。链头且还有别的 hook 时保留 Dobby 钩子，只把本节点降级成纯转发。</summary>
        public void Undo()
        {
            if (!_applied) return;
            _applied = false;
            HookState.Active.Remove(this);

            var chain = HookState.FindChain(_plan.FunctionPtr);
            if (chain == null) return;

            var node = chain.FindNode(this);
            if (node == null) return;

            var index = chain.Nodes.IndexOf(node);
            if (index == 0)
            {
                if (chain.Nodes.Count == 1)
                {
                    DetourBackend.Current?.Remove(_plan.FunctionPtr);
                    HookState.RemoveChain(chain);
                }
                else
                {
                    // 内核拒绝同一地址换 detour（rc=-4），所以保留头节点，
                    // 把它的处理函数换成"只调 orig"的转发桩。
                    HookState.SetHandler(node.Slot, HookStub.BuildPassthrough(_plan));
                    node.Owner = null;
                }
            }
            else
            {
                var previous = chain.Nodes[index - 1];
                HookState.SetOrig(previous.Slot, HookState.GetOrig(node.Slot));
                chain.Nodes.RemoveAt(index);
            }

            HookLog.Say($"[hook] 已撤 {_plan.Label}（槽 {_slot}）");
        }

        public void Dispose()
        {
            try { Undo(); }
            catch (Exception e) { HookLog.Fail("hook:" + _plan.Label, e); }
        }

        /// <summary>桌面探针用：入口桩的原生地址（模拟 il2cpp 调到 detour）。</summary>
        internal IntPtr EntryForTest() => _entry;

        /// <summary>同一个 handler 也能挂到别的方法上。</summary>
        public static Hook Attach(MethodBase target, Delegate handler) => new(target, handler);
    }
}

