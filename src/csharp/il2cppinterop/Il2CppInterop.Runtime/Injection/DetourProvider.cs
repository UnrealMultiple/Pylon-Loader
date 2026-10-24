using System;
using Il2CppInterop.Runtime.Startup;

namespace Il2CppInterop.Runtime.Injection;

public interface IDetour : IDisposable
{
    nint Target { get; }
    nint Detour { get; }
    nint OriginalTrampoline { get; }

    void Apply();
    T GenerateTrampoline<T>() where T : Delegate;
}

public interface IDetourProvider
{
    IDetour Create<TDelegate>(nint original, TDelegate target, bool specialReturnBuffer) where TDelegate : Delegate;
}

internal static class Detour
{
    public static IDetour Apply<T>(nint original, T target, out T trampoline) where T : Delegate
    {
        var detour = Il2CppInteropRuntime.Instance.DetourProvider.Create(original, target, specialReturnBuffer: false);
        trampoline = detour.GenerateTrampoline<T>();
        detour.Apply();
        return detour;
    }
}
