#nullable disable
// 层：Hook 引擎 —— 运行期槽位与链。
//
// 每条 hook 占一个"槽位"（slot）：生成出来的桩用 ldc.i4 slot + get_Item 读写运行期状态，
// 所以换 handler / 换下一跳指针都不用重新生成 IL，也不需要重新 patch 内存。
//
// 链的形状（Nodes 按调用顺序）：
//     游戏 → Nodes[0]（Dobby 真正 patch 的桩）→ Nodes[1] → … → Dobby 跳板（真正的原函数）
// 新加的 hook 追加到链尾，因此"先挂的先跑"。写明了就不算意外。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Il2CppInterop.RuntimeDetour
{
    /// <summary>hook 的运行期状态：槽位表 + 每个目标地址的链。</summary>
    internal static class HookState
    {
        internal static readonly List<Delegate> Handlers = new();
        internal static readonly List<Delegate> OrigDelegates = new();
        internal static readonly List<IntPtr> OrigPointers = new();

        /// <summary>把委托与桩对象钉住，防 GC 回收后跳进已释放的桩。</summary>
        internal static readonly List<object> KeepAlive = new();

        /// <summary>当前还挂着的 hook（诊断/清点用）。</summary>
        internal static readonly List<Hook> Active = new();

        private static readonly Dictionary<IntPtr, HookChain> Chains = new();
        private static readonly object Gate = new();

        internal static int AllocateSlot(Delegate handler)
        {
            lock (Gate)
            {
                var slot = Handlers.Count;
                Handlers.Add(handler);
                OrigDelegates.Add(null);
                OrigPointers.Add(IntPtr.Zero);
                return slot;
            }
        }

        internal static void SetOrigDelegate(int slot, Delegate orig)
        {
            if (slot < 0 || slot >= OrigDelegates.Count) return;
            OrigDelegates[slot] = orig;
        }

        internal static void SetHandler(int slot, Delegate handler)
        {
            if (slot < 0 || slot >= Handlers.Count) return;
            Handlers[slot] = handler;
        }

        /// <summary>换某个槽位的下一跳指针（链重排只改这里，不需要重新 patch）。</summary>
        internal static void SetOrig(int slot, IntPtr pointer)
        {
            if (slot < 0 || slot >= OrigPointers.Count) return;
            OrigPointers[slot] = pointer;
        }

        internal static IntPtr GetOrig(int slot) =>
            slot >= 0 && slot < OrigPointers.Count ? OrigPointers[slot] : IntPtr.Zero;

        internal static HookChain GetOrCreateChain(IntPtr target)
        {
            lock (Gate)
            {
                if (Chains.TryGetValue(target, out var chain)) return chain;
                chain = new HookChain(target);
                Chains[target] = chain;
                return chain;
            }
        }

        internal static HookChain FindChain(IntPtr target)
        {
            lock (Gate) return Chains.TryGetValue(target, out var chain) ? chain : null;
        }

        internal static void RemoveChain(HookChain chain)
        {
            if (chain == null) return;
            lock (Gate) Chains.Remove(chain.Target);
        }

        /// <summary>桩里的兜底：hook 抛异常只记一条，不把游戏线程带走。</summary>
        internal static void ReportException(string label, Exception exception) => HookLog.Fail(label, exception);
    }

    /// <summary>链上的一个节点：一个槽位 + 它的原生入口。</summary>
    internal sealed class HookNode
    {
        internal HookNode(int slot, IntPtr entry) { Slot = slot; Entry = entry; }

        internal int Slot;
        internal IntPtr Entry;

        /// <summary>为 null 表示这条 hook 已撤销，节点保留只做转发（Dobby 钩子不动）。</summary>
        internal Hook Owner;
    }

    /// <summary>同一目标地址上的 hook 链。</summary>
    internal sealed class HookChain
    {
        internal HookChain(IntPtr target) => Target = target;

        internal IntPtr Target;

        /// <summary>Dobby 给出的真跳板（链尾）。</summary>
        internal IntPtr Trampoline;

        /// <summary>按调用顺序排列；[0] 是 Dobby 真正 patch 的桩。</summary>
        internal readonly List<HookNode> Nodes = new();

        internal HookNode FindNode(Hook hook)
        {
            foreach (var node in Nodes) if (ReferenceEquals(node.Owner, hook)) return node;
            return null;
        }
    }
}


