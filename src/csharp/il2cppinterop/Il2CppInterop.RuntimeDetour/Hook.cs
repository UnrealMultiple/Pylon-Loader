using System;
using System.Reflection;
using System.Threading;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>A strongly typed managed subscription to an IL2CPP method or constructor.</summary>
public sealed class Hook : IDisposable
{
    private readonly HookPlan _plan;
    private readonly Delegate _handler;
    private HookChain? _site;
    private HookNode? _node;
    private Delegate _orig = null!;
    private bool _applied;

    public Hook(MethodBase target, Delegate handler) : this(target, handler, true) { }
    public Hook(MethodBase target, Delegate handler, bool applyByDefault)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _plan = HookPlan.Create(target, handler);
        if (applyByDefault) Apply();
    }
    public string Label => _plan.Label;
    public bool IsConstructorHook => _plan.IsConstructor;
    public ConstructorInfo? Constructor => IsConstructorHook ? ConstructorTarget.Unwrap(_plan.Target) : null;
    public bool IsApplied => Volatile.Read(ref _applied);
    /// <summary>The next managed subscription, followed by the native trampoline.</summary>
    public Delegate Original => _orig;
    public MethodInfo Target => _plan.Target;

    public void Apply()
    {
        lock (HookState.Gate)
        {
            if (_applied) return;
            var backend = DetourBackend.Current ?? throw new InvalidOperationException("The host must set DetourBackend.Current before subscribing hooks");
            if (!backend.IsAvailable) throw new InvalidOperationException($"The native detour backend is unavailable; cannot hook {Label}");
            var site = HookState.GetOrCreateChain(_plan);
            var node = new HookNode(_plan, _handler, site);
            if (!site.Installed)
            {
                Volatile.Write(ref site.Transition, true);
                try
                {
                    var rc = backend.Apply(site.Target, site.Entry, out var trampoline);
                    if (rc != 0 || trampoline == IntPtr.Zero)
                        throw new InvalidOperationException($"Hook failed rc={rc} trampoline=0x{trampoline.ToInt64():x} ({Label})");
                    Volatile.Write(ref site.OriginalPointer, trampoline);
                    site.Backend = backend;
                    site.Installed = true;
                }
                finally { Volatile.Write(ref site.Transition, false); }
            }
            Volatile.Write(ref site.PendingRemoval, false);
            site.Nodes.Add(node);
            site.Publish();
            _site = site;
            _node = node;
            _orig = node.Original;
            Volatile.Write(ref _applied, true);
            HookState.Active.Add(this);
            HookLog.Say($"[hook] applied {Label} (site {site.Slot}, subscriptions {site.Nodes.Count})");
        }
    }

    /// <summary>Removes this subscription. Backend failure leaves it applied and retryable.</summary>
    public void Undo()
    {
        lock (HookState.Gate)
        {
            if (!_applied) return;
            var site = _site!;
            var node = _node!;
            var index = site.Nodes.IndexOf(node);
            site.Nodes.RemoveAt(index);
            site.Publish();
            if (site.Nodes.Count == 0)
            {
                Volatile.Write(ref site.PendingRemoval, true);
                try { site.TryRemove(); }
                catch
                {
                    site.Nodes.Insert(index, node);
                    Volatile.Write(ref site.PendingRemoval, false);
                    site.Publish();
                    throw;
                }
            }
            _node = null;
            HookState.Active.Remove(this);
            Volatile.Write(ref _applied, false);
            HookLog.Say($"[hook] undone {Label} (site {site.Slot})");
        }
    }

    public void Dispose() => Undo();
    internal IntPtr EntryForTest() => _site?.Entry ?? IntPtr.Zero;
    public static Hook Attach(MethodBase target, Delegate handler) => new(target, handler);
}
