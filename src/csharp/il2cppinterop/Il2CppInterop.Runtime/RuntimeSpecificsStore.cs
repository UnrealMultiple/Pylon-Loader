using System;
using System.Collections.Concurrent;

namespace Il2CppInterop.Runtime;

/// <summary>Per-class runtime flags used by the interop layer.</summary>
public static class RuntimeSpecificsStore
{
    // IsInjected runs on the hot path (every reference wrap), so use a lock-free dictionary
    // instead of a ReaderWriterLockSlim-protected Dictionary.
    private static readonly ConcurrentDictionary<IntPtr, bool> s_wasInjected = new();

    public static bool IsInjected(IntPtr nativeClass)
    {
        return s_wasInjected.TryGetValue(nativeClass, out var result) && result;
    }

    public static void SetClassInfo(IntPtr nativeClass, bool wasInjected)
    {
        s_wasInjected[nativeClass] = wasInjected;
    }
}
