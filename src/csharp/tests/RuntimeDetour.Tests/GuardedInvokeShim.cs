#if HOOK_TEST_SHIM
namespace Il2CppInterop.Runtime;

// The detour shim compiles the engine sources without the full runtime assembly.
// Keep the callback boundary observable to the engine tests while production binds
// to the real GuardedInvoke implementation.
public static class GuardedInvoke
{
    [ThreadStatic] private static int s_depth;
    public static void EnterNativeCallback() => s_depth++;
    public static void ExitNativeCallback()
    {
        if (s_depth <= 0) throw new InvalidOperationException("No native callback to leave");
        s_depth--;
    }
}
#endif
