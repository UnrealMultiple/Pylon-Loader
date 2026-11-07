using Il2CppInterop.Runtime;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.UI;
using NativeLayer = Terraria.UI.GameInterfaceLayer;
using NativeList = Il2CppSystem.Collections.Generic.List<Terraria.UI.GameInterfaceLayer>;

namespace Pylon.ModLoader;

/// <summary>
/// 界面层管线：游戏每帧按 <c>Main._gameInterfaceLayers</c> 里的顺序逐层画界面。
/// 我们把这份列表抓下来、交给作者改（增删/换序/隐藏），再把结果写回去；
/// 作者层的绘制通过"游戏会调用每一层的 <c>DrawSelf</c>"这一条转回托管。
/// </summary>
internal sealed class InterfaceLayerPipeline : ILoaderModule, ILoaderModuleRefresh
{
    private static long _diTick;

    public static InterfaceLayerPipeline Instance { get; } = new();

    public string Name => "interfacelayer";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>原版层的包装（抓取时建一次，之后每帧复用）。</summary>
    private static readonly List<GameInterfaceLayer> Vanilla = [];

    /// <summary>交给作者改的整份列表（复用同一个实例）。</summary>
    private static readonly List<GameInterfaceLayer> Layers = [];

    /// <summary>上一帧写进去的指针序列（"真的变了"才写回）。</summary>
    private static readonly List<IntPtr> Applied = [];

    /// <summary>作者层外壳（按名字 + 重复序号缓存，作者的层每帧新建也能复用外壳）。</summary>
    private static readonly Dictionary<string, Shell> Shells = new(StringComparer.Ordinal);

    /// <summary>外壳 + 钉住它的强 GC handle。</summary>
    private sealed class Shell
    {
        public NativeLayer Layer = null!;
        public IntPtr Pin;
    }

    /// <summary>外壳指针 → 作者的层（<c>DrawSelf</c> 靠它转回托管）。</summary>
    private static readonly Dictionary<IntPtr, GameInterfaceLayer> ByPointer = [];

    /// <summary>外壳指针 → 外壳对象（写回列表时要把指针换回对象）。</summary>
    private static readonly Dictionary<IntPtr, Shell> ShellsByPointer = [];

    /// <summary>原版层的原始指针（与 <see cref="Vanilla"/> 一一对应）；不长期持有包装对象。</summary>
    private static readonly List<IntPtr> NativePtrs = [];

    /// <summary>加载器自己的层（画完原版层之后画，不参与作者的增删）。</summary>
    private static readonly List<GameInterfaceLayer> Internal = [];

    /// <summary>登记一个加载器内部的界面层（不是作者 API）。</summary>
    internal static void RegisterInternal(GameInterfaceLayer layer) => Internal.Add(layer);

    private static IntPtr _capturedList;
    private static int _capturedCount;

    public bool Install()
    {
        On.Terraria.Main.DrawInterface += DrawInterface;

        // DrawSelf 只有 8 字节（mov w0,#1; ret）；ShadowHook 入口只写一条 4 字节 B，
        // 不会像 Dobby（16 字节补丁）那样覆盖紧邻的 get__notifications。
        On.Terraria.UI.GameInterfaceLayer.DrawSelf += DrawSelf;

        Log.Info("[interfacelayer] 已挂（界面层序列可增删/换序/隐藏；作者层绘制转回托管）");
        return true;
    }

    public void Refresh() => Reset();

    internal static void Reset()
    {
        Vanilla.Clear();
        Layers.Clear();
        Applied.Clear();
        foreach (var shell in Shells.Values)
        {
            if (shell.Pin != IntPtr.Zero) IL2CPP.il2cpp_gchandle_free(shell.Pin);
        }
        Shells.Clear();
        ByPointer.Clear();
        ShellsByPointer.Clear();
        NativePtrs.Clear();
        _capturedList = IntPtr.Zero;
        _capturedCount = 0;
    }

    private static void DrawInterface(On.Terraria.Main.orig_DrawInterface orig, Main self, GameTime gameTime)
    {
        var now = Environment.TickCount64;
        if (now - _diTick > 200)
        {
            _diTick = now;

        }

        if (SystemTable.AnyModifyInterfaceLayers || Internal.Count > 0)
        {
            try
            {
                Apply(self);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[interfacelayer] 重建界面层失败：" + Safe.Describe(e));
            }
        }

        orig(self, gameTime);
    }

    /// <summary>作者层的绘制：游戏调到外壳的 <c>DrawSelf</c>，这里换回作者的委托。</summary>
    private static bool DrawSelf(On.Terraria.UI.GameInterfaceLayer.orig_DrawSelf orig, NativeLayer self)
    {
        if (self is null) return true;

        if (ByPointer.TryGetValue(self.Pointer, out var layer) && layer.Draw is { } draw)
        {
            try
            {
                return draw();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[interfacelayer] {layer.Name} 绘制异常：" + Safe.Describe(e));
                return true;
            }
        }

        try
        {
            return orig(self);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[interfacelayer] 原版 Draw 抛异常：" + Safe.Describe(e));
            return true;
        }
    }

    private static void Apply(Main main)
    {
        // 这一帧游戏要重建自己的层：先不插手，下一帧重新抓。
        if (main._needToSetupDrawInterfaceLayers)
        {
            _capturedList = IntPtr.Zero;
            return;
        }

        var list = main._gameInterfaceLayers;
        if (list is null || list.Count == 0) return;

        if (_capturedList != list.Pointer || list.Count != _capturedCount) Capture(list);

        Layers.Clear();
        for (var i = 0; i < Vanilla.Count; i++)
        {
            Vanilla[i].Active = true;
            Layers.Add(Vanilla[i]);
        }

        for (var i = 0; i < Internal.Count; i++) Layers.Add(Internal[i]);

        SystemTable.ModifyInterfaceLayers(Layers);

        var final = new List<IntPtr>(Layers.Count);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < Layers.Count; i++)
        {
            var layer = Layers[i];
            if (layer is null || !layer.Active) continue;

            if (layer.Native != IntPtr.Zero)
            {
                final.Add(layer.Native);
                continue;
            }

            if (layer.Draw is null) continue;

            var pointer = ShellOf(layer, seen);
            ByPointer[pointer] = layer;
            final.Add(pointer);
        }

        if (Same(final)) return;

        for (var i = list.Count - 1; i >= 0; i--) list.RemoveAt(i);

        for (var i = 0; i < final.Count; i++)
        {
            if (NativeOf(final[i]) is { } native) list.Add(native);
        }

        Applied.Clear();
        Applied.AddRange(final);
    }

    private static NativeLayer? NativeOf(IntPtr pointer)
    {
        for (var i = 0; i < Vanilla.Count; i++)
        {
            if (Vanilla[i].Native == pointer) return new NativeLayer(pointer);
        }

        return ShellsByPointer.TryGetValue(pointer, out var shell) ? shell.Layer : null;
    }

    private static void Capture(NativeList list)
    {
        Vanilla.Clear();
        NativePtrs.Clear();
        ByPointer.Clear();

        for (var i = 0; i < list.Count; i++)
        {
            var native = list[i];
            if (native is null) continue;

            // 我们自己写回的外壳不算原版层：否则下一帧 Capture 又把它们当原版抓回来，
            // Layers = Vanilla(含壳) + Internal 每帧净增，形成 +N/帧的滚雪球（本次点击物品崩溃的根因）。
            if (ShellsByPointer.ContainsKey(native.Pointer)) continue;

            Vanilla.Add(new GameInterfaceLayer(native.Name ?? "", native.ScaleType, native.Pointer));
            NativePtrs.Add(native.Pointer);
        }

        _capturedList = list.Pointer;
        _capturedCount = list.Count;
    }

    /// <summary>作者层的外壳：同一名字的第 n 次出现复用一个外壳。</summary>
    private static IntPtr ShellOf(GameInterfaceLayer layer, Dictionary<string, int> seen)
    {
        var name = layer.Name;
        var repeat = seen.TryGetValue(name, out var count) ? count : 0;
        seen[name] = repeat + 1;

        var key = repeat == 0 ? name : name + "#" + repeat;
        if (!Shells.TryGetValue(key, out var shell))
        {
            var created = new NativeLayer(name, layer.ScaleType);
            // pinned=true：钉住 il2cpp 对象，包装器被回收也不影响游戏侧。
            shell = new Shell { Layer = created, Pin = IL2CPP.il2cpp_gchandle_new(created.Pointer, true) };
            Shells[key] = shell;
        }
        else if (shell.Layer.ScaleType != layer.ScaleType)
        {
            shell.Layer.ScaleType = layer.ScaleType;
        }

        ShellsByPointer[shell.Layer.Pointer] = shell;
        return shell.Layer.Pointer;
    }

    private static bool Same(List<IntPtr> final)
    {
        if (final.Count != Applied.Count) return false;

        for (var i = 0; i < final.Count; i++)
        {
            if (final[i] != Applied[i]) return false;
        }

        return true;
    }
}