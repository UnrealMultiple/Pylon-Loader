namespace Pylon.ModLoader.Contracts;
/// <summary>用 il2cpp 强 GCHandle 钉住一个游戏对象，防游戏 GC 回收（商店条目的 Item 这类托管侧长期持有的对象用）。</summary>
internal static class ObjPin
{
    public static System.IntPtr Hold(object obj) =>
        obj is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o
            ? Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new(o.Pointer, false)
            : System.IntPtr.Zero;
}
