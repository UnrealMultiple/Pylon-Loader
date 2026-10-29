using System;
using System.Reflection;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     Attaches a managed handler to a game (il2cpp) method, with an orig delegate that calls
///     the original function. Usage mirrors MonoMod.RuntimeDetour.Hook:
/// <code>
///     using var hook = new Hook(
///         typeof(Main).GetMethod(nameof(Main.DrawMenu), new[] { typeof(GameTime) }),
///         new Action&lt;Action&lt;GameTime&gt;, GameTime&gt;((orig, time) =&gt;
///         {
///             orig(time);
///             // ...
///         }));
/// </code>
///     The handler's first parameter is orig (a delegate with the target method's signature);
///     for instance methods, self follows orig. The generated On.* assemblies also go through
///     this path (see <see cref="HookEndpoint"/>).
/// </summary>
public sealed class Hook : IDisposable
{
    private readonly HookPlan _plan;
    private readonly Delegate _handler;

    private int _slot = -1;
    private IntPtr _entry;
    private Delegate _orig = null!;
    private HookChain _chain = null!;
    private HookNode _node = null!;
    private bool _applied;

    /// <summary>Hooks immediately (equivalent to MonoMod's applyByDefault = true).</summary>
    public Hook(MethodBase target, Delegate handler) : this(target, handler, true)
    {
    }

    public Hook(MethodBase target, Delegate handler, bool applyByDefault)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        _handler = handler;
        _plan = HookPlan.Create(target, handler);
        if (applyByDefault) Apply();
    }

    /// <summary>The target method's label (Type.Method).</summary>
    public string Label => _plan.Label;

    public bool IsApplied => _applied;

    /// <summary>The orig delegate: invoking it walks the rest of the chain until the real trampoline.</summary>
    public Delegate Original => _orig;

    /// <summary>The method this hook is attached to.</summary>
    public MethodInfo Target => _plan.Target;

    /// <summary>Applies the hook (idempotent). Throws when the detour backend is unavailable.</summary>
    public void Apply()
    {
        if (_applied) return;
        var backend = DetourBackend.Current;
        if (backend is null)
            throw new InvalidOperationException(
                "No DetourBackend is set: the host must provide the native detour implementation at startup (Pylon -> kernel Dobby)");
        if (!backend.IsAvailable)
            throw new InvalidOperationException($"The detour backend is unavailable (libdobby.so not loaded); cannot hook {_plan.Label}");

        _slot = HookState.AllocateSlot(_handler);
        _orig = HookStub.BuildOrig(_plan, _slot);
        HookState.SetOrigDelegate(_slot, _orig);

        var (stub, entry) = HookStub.BuildEntry(_plan, _slot);
        HookState.s_keepAlive.Add(stub);
        _entry = entry;

        _chain = HookState.GetOrCreateChain(_plan.FunctionPtr);

        if (_chain.Nodes.Count == 0)
        {
            // Chain head: hand it to the backend and take back the trampoline
            var rc = backend.Apply(_plan.FunctionPtr, _entry, out var trampoline);
            if (rc != 0 || trampoline == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Hook failed rc={rc} trampoline=0x{trampoline.ToInt64():x} (target 0x{_plan.FunctionPtr.ToInt64():x})");
            _chain.Trampoline = trampoline;
            HookState.SetOrig(_slot, _chain.Trampoline);
        }
        else
        {
            // Append to the chain tail: the new node takes over the tail's next hop, and the tail
            // now jumps to the new node
            var tail = _chain.Nodes[_chain.Nodes.Count - 1];
            HookState.SetOrig(_slot, HookState.GetOrig(tail.Slot));
            HookState.SetOrig(tail.Slot, _entry);
        }

        _node = new HookNode(_slot, _entry) { Owner = this };
        _chain.Nodes.Add(_node);
        _applied = true;
        HookState.Active.Add(this);

        HookLog.Say($"[hook] applied {_plan.Label} (slot {_slot}, chain position {_chain.Nodes.Count})");
    }

    /// <summary>
    ///     Undoes the hook (idempotent). When the hook is the chain head and other hooks remain,
    ///     the Dobby detour is kept and this node is demoted to pure forwarding.
    /// </summary>
    public void Undo()
    {
        if (!_applied) return;
        _applied = false;
        HookState.Active.Remove(this);

        var chain = HookState.FindChain(_plan.FunctionPtr);
        if (chain is null) return;

        var node = chain.FindNode(this);
        if (node is null) return;

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
                // The kernel refuses to swap a detour on the same address (rc=-4), so keep the head
                // node and swap its handler for a "call orig only" forwarding stub.
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

        HookLog.Say($"[hook] undone {_plan.Label} (slot {_slot})");
    }

    public void Dispose()
    {
        try { Undo(); }
        catch (Exception e) { HookLog.Fail("hook:" + _plan.Label, e); }
    }

    /// <summary>Desktop probe: the entry stub's native address (simulates il2cpp calling the detour).</summary>
    internal IntPtr EntryForTest() => _entry;

    /// <summary>The same handler can be attached to other methods as well.</summary>
    public static Hook Attach(MethodBase target, Delegate handler) => new(target, handler);
}