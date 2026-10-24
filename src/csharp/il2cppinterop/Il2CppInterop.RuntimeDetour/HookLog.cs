using System;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     Logging sink of the hook engine. The engine does not know the host
///     (Pylon.ModLoader / Pylon.Managed): it only exposes two hooks that the host
///     attaches during startup. When nothing is attached, messages are silently dropped.
/// </summary>
public static class HookLog
{
    /// <summary>Informational messages (apply / undo / chain layout).</summary>
    public static Action<string>? Info;

    /// <summary>Errors (handler exceptions, stub generation failures).</summary>
    public static Action<string, Exception>? Error;

    internal static void Say(string message)
    {
        if (message is null) return;
        try { Info?.Invoke(message); } catch { /* logging must never break hooking */ }
    }

    internal static void Fail(string label, Exception exception)
    {
        try
        {
            if (Error is not null) Error(label, exception);
            else Info?.Invoke($"[hook] {label}: {exception.GetType().Name}: {exception.Message}");
        }
        catch { /* logging must never break hooking */ }
    }
}
