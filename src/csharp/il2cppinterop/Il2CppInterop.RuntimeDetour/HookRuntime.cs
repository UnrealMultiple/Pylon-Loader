using System;
using System.Collections.Generic;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     Runtime state of the hook engine: the slot tables plus one chain per target address.
///
///     Each hook owns a "slot": stubs load their per-call state from these tables
///     (ldc.i4 slot + get_Item), so swapping a handler or re-pointing the next hop never
///     requires regenerating IL or re-patching memory.
///
///     Chain shape (Nodes in invocation order):
///         game -> Nodes[0] (the stub Dobby actually patches) -> Nodes[1] -> ... -> trampoline
///     New hooks are appended to the end of the chain, so earlier hooks run first.
/// </summary>
internal static class HookState
{
    internal static readonly List<Delegate> s_handlers = new();
    internal static readonly List<Delegate> s_origDelegates = new();
    internal static readonly List<IntPtr> s_origPointers = new();

    /// <summary>Pins stubs and delegates so the GC never collects them mid-invocation.</summary>
    internal static readonly List<object> s_keepAlive = new();

    /// <summary>Currently applied hooks (diagnostics / undo-all).</summary>
    private static readonly List<Hook> s_active = new();
    internal static List<Hook> Active => s_active;

    private static readonly Dictionary<IntPtr, HookChain> s_chains = new();
    private static readonly object s_gate = new();

    internal static int AllocateSlot(Delegate handler)
    {
        lock (s_gate)
        {
            var slot = s_handlers.Count;
            s_handlers.Add(handler);
            s_origDelegates.Add(null!);
            s_origPointers.Add(IntPtr.Zero);
            return slot;
        }
    }

    internal static void SetOrigDelegate(int slot, Delegate orig)
    {
        if (slot < 0 || slot >= s_origDelegates.Count) return;
        s_origDelegates[slot] = orig;
    }

    internal static void SetHandler(int slot, Delegate handler)
    {
        if (slot < 0 || slot >= s_handlers.Count) return;
        s_handlers[slot] = handler;
    }

    /// <summary>Re-points a slot's next hop (chain re-linking only changes this, never re-patches memory).</summary>
    internal static void SetOrig(int slot, IntPtr pointer)
    {
        if (slot < 0 || slot >= s_origPointers.Count) return;
        s_origPointers[slot] = pointer;
    }

    internal static IntPtr GetOrig(int slot) =>
        slot >= 0 && slot < s_origPointers.Count ? s_origPointers[slot] : IntPtr.Zero;

    internal static HookChain GetOrCreateChain(IntPtr target)
    {
        lock (s_gate)
        {
            if (s_chains.TryGetValue(target, out var chain)) return chain;
            chain = new HookChain(target);
            s_chains[target] = chain;
            return chain;
        }
    }

    internal static HookChain? FindChain(IntPtr target)
    {
        lock (s_gate) return s_chains.TryGetValue(target, out var chain) ? chain : null;
    }

    internal static void RemoveChain(HookChain chain)
    {
        if (chain is null) return;
        lock (s_gate) s_chains.Remove(chain.Target);
    }

    /// <summary>Fallback used by stubs: a handler exception is logged, never propagated into the game.</summary>
    internal static void ReportException(string label, Exception exception) => HookLog.Fail(label, exception);
}

/// <summary>A single node of a hook chain: one slot plus its native entry point.</summary>
internal sealed class HookNode
{
    internal HookNode(int slot, IntPtr entry)
    {
        Slot = slot;
        Entry = entry;
    }

    internal int Slot { get; }
    internal IntPtr Entry { get; }

    /// <summary>The hook this node belongs to; null once the hook is undone (the node then only forwards).</summary>
    internal Hook? Owner { get; set; }
}

/// <summary>The chain of hooks attached to a single target address.</summary>
internal sealed class HookChain
{
    internal HookChain(IntPtr target) => Target = target;

    internal IntPtr Target { get; }

    /// <summary>The real trampoline handed out by the backend (end of the chain).</summary>
    internal IntPtr Trampoline { get; set; }

    /// <summary>Nodes in invocation order; [0] is the stub Dobby actually patches.</summary>
    internal List<HookNode> Nodes { get; } = new();

    internal HookNode? FindNode(Hook hook)
    {
        foreach (var node in Nodes)
            if (ReferenceEquals(node.Owner, hook))
                return node;
        return null;
    }
}