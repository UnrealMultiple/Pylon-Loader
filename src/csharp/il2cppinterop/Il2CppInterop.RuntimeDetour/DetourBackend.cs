using System;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     Abstraction over the native detour mechanism used by the hook engine.
///     The engine only generates stubs, maintains chains and invokes handlers; rewriting
///     code in memory is delegated to the host (e.g. Pylon's kernel Dobby exports:
///     PylonMod_HookApply / PylonMod_HookRemove). This keeps the engine free of any
///     platform detail, and a desktop probe can substitute a fake backend.
/// </summary>
public abstract class DetourBackend
{
    /// <summary>Gets or sets the process-wide backend. The host must set it during startup.</summary>
    public static DetourBackend? Current { get; set; }

    /// <summary>Whether the backend is usable right now (false when Dobby is not loaded; never throws).</summary>
    public abstract bool IsAvailable { get; }

    /// <summary>
    ///     Redirects <paramref name="target"/> to <paramref name="detour"/>, exposing a callable
    ///     trampoline to the original function through <paramref name="trampoline"/>.
    /// </summary>
    /// <returns>Zero on success; a non-zero value is translated into a readable exception by <see cref="Hook"/>.</returns>
    public abstract int Apply(IntPtr target, IntPtr detour, out IntPtr trampoline);

    /// <summary>Removes a previously applied detour. Returns -1 when nothing was applied; never throws.</summary>
    public abstract int Remove(IntPtr target);
}
