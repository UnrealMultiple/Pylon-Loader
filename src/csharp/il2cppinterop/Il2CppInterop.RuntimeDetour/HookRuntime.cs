using System;
using System.Collections.Generic;
using System.Threading;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
/// One stable native entry per target address. Subscriptions form a strongly typed managed chain;
/// adding/removing a subscription neither emits another native thunk nor adds a global slot.
/// Native entry delegates remain rooted for the process lifetime: a backend may have a thread
/// between its native branch and the first managed instruction even after Remove returns.
/// Retired sites retain no subscriber/closure, only their forwarding stub and signature metadata.
/// </summary>
internal static class HookState
{
    internal static readonly object Gate = new();
    private static HookChain[] s_slots = Array.Empty<HookChain>();
    private static readonly Dictionary<IntPtr, HookChain> s_chains = new();
    internal static readonly List<Hook> Active = new();

    internal static HookChain GetOrCreateChain(HookPlan plan)
    {
        // Caller holds Gate; publish only a fully constructed slot table.
        if (s_chains.TryGetValue(plan.FunctionPtr, out var existing))
        {
            existing.Validate(plan);
            return existing;
        }
        var chain = new HookChain(plan, s_slots.Length);
        var slots = new HookChain[s_slots.Length + 1];
        Array.Copy(s_slots, slots, s_slots.Length);
        slots[chain.Slot] = chain;
        Volatile.Write(ref s_slots, slots);
        try
        {
            chain.Original = HookStub.BuildOrig(plan, chain.Slot);
            chain.Dispatch = chain.Original;
            chain.Handler = HookStub.BuildDispatcher(chain);
            (chain.EntryDelegate, chain.Entry) = HookStub.BuildEntry(plan, chain.Slot);
            s_chains.Add(plan.FunctionPtr, chain);
            return chain;
        }
        catch
        {
            // Nothing has been exposed to native code yet.
            var previous = new HookChain[slots.Length - 1];
            Array.Copy(slots, previous, previous.Length);
            Volatile.Write(ref s_slots, previous);
            throw;
        }
    }

    private static HookChain Site(int slot) => Volatile.Read(ref s_slots)[slot];
    internal static Delegate GetHandler(int slot) => Site(slot).Handler;
    internal static Delegate GetOrigDelegate(int slot) => Site(slot).Original;
    internal static IntPtr GetOrig(int slot)
    {
        var site = Site(slot);
        // Apply/Remove may patch native code before returning the trampoline. The common path
        // only reads an atomic pointer; a callback racing publication waits for that transaction.
        if (Volatile.Read(ref site.Transition))
            lock (Gate) return site.OriginalPointer;
        return Volatile.Read(ref site.OriginalPointer);
    }

    [ThreadStatic] private static InvocationFrame? s_frame;
    [ThreadStatic] private static InvocationFrame? s_spare;

    internal static void EnterInvocation(int slot, int parameterCount)
    {
        var frame = s_spare;
        if (frame is null) frame = new InvocationFrame(parameterCount);
        else
        {
            s_spare = frame.Previous;
            if (frame.Arguments.Length < parameterCount) frame.Arguments = new IntPtr[parameterCount];
        }
        frame.Slot = slot;
        frame.Previous = s_frame;
        s_frame = frame;
    }

    internal static void ExitInvocation()
    {
        var frame = s_frame ?? throw new InvalidOperationException("No hook invocation to leave");
        s_frame = frame.Previous;
        Array.Clear(frame.Arguments, 0, frame.Arguments.Length);
        frame.Previous = s_spare;
        s_spare = frame;
    }

    internal static void SetIndirectArg(int slot, int index, IntPtr value)
    {
        var frame = FindFrame(slot) ?? throw new InvalidOperationException("No active native hook invocation");
        frame.Arguments[index] = value;
    }

    internal static IntPtr GetIndirectArg(int slot, int index) => FindFrame(slot)?.Arguments[index] ?? IntPtr.Zero;

    private static InvocationFrame? FindFrame(int slot)
    {
        for (var frame = s_frame; frame is not null; frame = frame.Previous)
            if (frame.Slot == slot) return frame;
        return null;
    }

    private sealed class InvocationFrame
    {
        internal InvocationFrame(int count) => Arguments = new IntPtr[count];
        internal int Slot;
        internal IntPtr[] Arguments;
        internal InvocationFrame? Previous;
    }

    internal static void EnterNative(int slot)
    {
        var site = Site(slot);
        Interlocked.Increment(ref site.InFlight);
        if (Volatile.Read(ref site.Transition)) lock (Gate) { }
    }

    internal static void ExitNative(int slot)
    {
        var site = Site(slot);
        if (Interlocked.Decrement(ref site.InFlight) == 0 && Volatile.Read(ref site.PendingRemoval) && site.Backend?.SupportsSafeRemoval == true)
        {
            lock (Gate)
            {
                try { site.TryRemove(); }
                catch (Exception error) { HookLog.Fail("hook:deferred-remove", error); }
            }
        }
    }

    internal static void FlushRetired()
    {
        lock (Gate)
            foreach (var site in s_chains.Values)
                if (site.PendingRemoval) site.TryRemove();
    }

    internal static int SiteCount => Volatile.Read(ref s_slots).Length;
    internal static void ReportException(string label, Exception exception) => HookLog.Fail(label, exception);
    internal static void ReportThrowable(string label, object? throwable)
    {
        // Do not format an IL2CPP throwable: malformed native exception objects can fault while
        // formatting Message/ToString. Logging must never propagate through the native boundary.
        try { HookLog.Info?.Invoke($"[hook] {label}: 未捕获({throwable?.GetType().Name ?? "null"})"); }
        catch { }
    }
}

internal sealed class HookNode
{
    internal HookNode(HookPlan plan, Delegate handler, HookChain site)
    {
        Handler = handler;
        Next = site.Original;
        Original = HookStub.BuildNext(plan, this, site.Plan.OrigType);
        Call = HookStub.BuildManagedCall(plan, this, site.Plan.OrigType);
    }
    internal readonly Delegate Handler;
    internal readonly Delegate Original;
    internal readonly Delegate Call;
    internal Delegate Next;
    internal Delegate ReadNext() => Volatile.Read(ref Next);
}

internal sealed class HookChain
{
    internal HookChain(HookPlan plan, int slot)
    {
        Plan = plan;
        Slot = slot;
        OriginalPointer = plan.FunctionPtr;
    }
    internal readonly HookPlan Plan;
    internal readonly int Slot;
    internal IntPtr Target => Plan.FunctionPtr;
    internal Delegate Original = null!;
    internal Delegate Handler = null!;
    internal Delegate Dispatch = null!;
    internal Delegate EntryDelegate = null!;
    internal IntPtr Entry;
    internal IntPtr OriginalPointer;
    internal bool Transition;
    internal bool Installed;
    internal bool PendingRemoval;
    internal int InFlight;
    internal DetourBackend? Backend;
    internal readonly List<HookNode> Nodes = new();
    internal Delegate ReadDispatch() => Volatile.Read(ref Dispatch);

    internal void Publish()
    {
        Delegate next = Original;
        for (var i = Nodes.Count - 1; i >= 0; i--)
        {
            Volatile.Write(ref Nodes[i].Next, next);
            next = Nodes[i].Call;
        }
        Volatile.Write(ref Dispatch, next);
    }

    internal void TryRemove()
    {
        // Caller holds Gate. Never free a trampoline while a managed handler may still call orig.
        if (!Installed || !Backend!.SupportsSafeRemoval || Nodes.Count != 0 || Volatile.Read(ref InFlight) != 0) return;
        Volatile.Write(ref Transition, true);
        try
        {
            // Recheck after closing the publication gate: a native callback may have just entered.
            if (Volatile.Read(ref InFlight) != 0) return;
            var rc = Backend!.Remove(Target);
            if (rc != 0) throw new InvalidOperationException($"Hook removal failed rc={rc} ({Plan.Label}); retained for retry");
            Installed = false;
            Volatile.Write(ref OriginalPointer, Target);
            Volatile.Write(ref PendingRemoval, false);
        }
        finally { Volatile.Write(ref Transition, false); }
    }

    internal void Validate(HookPlan plan)
    {
        var left = Plan.OrigType.GetMethod("Invoke")!;
        var right = plan.OrigType.GetMethod("Invoke")!;
        var a = left.GetParameters();
        var b = right.GetParameters();
        var same = left.ReturnType == right.ReturnType && a.Length == b.Length;
        for (var i = 0; same && i < a.Length; i++) same = a[i].ParameterType == b[i].ParameterType;
        // IL2CPP generic sharing may map distinct MethodInfo* contexts to one address. A detour
        // at that address cannot safely pretend those contexts are interchangeable.
        if (!same || Plan.MethodInfoPtr != plan.MethodInfoPtr)
            throw new NotSupportedException($"{plan.Label} shares native code with an incompatible hook target");
    }
}
