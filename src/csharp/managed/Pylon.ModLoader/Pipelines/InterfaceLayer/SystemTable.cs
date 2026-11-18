namespace Pylon.ModLoader;

/// <summary>
/// 系统时机的表：界面层与两个绘制时机是 Mod 上的虚方法，这里按"覆写了哪些"决定挂不挂/跑不跑；
/// ModSystem 实例排在所有 Mod 之后（先派 Mod，再按注册序派 system）。
/// 位集在 Mod 装载时算好（<c>Mod.Plan</c> / <c>ModSystem.Plan</c>），分派期只做位查询。
/// </summary>
internal static class SystemTable
{
    private static readonly List<Mod> Interface = [];
    private static bool _systemsInterface;

    internal static bool AnyModifyInterfaceLayers => Interface.Count > 0 || _systemsInterface;

    internal static bool AnyPostDrawTiles { get; private set; }

    internal static bool AnyPostDrawInterface { get; private set; }

    internal static void Rebuild()
    {
        Interface.Clear();
        _systemsInterface = false;
        AnyPostDrawTiles = false;
        AnyPostDrawInterface = false;

        var mods = LoaderHost.Current.Mods.Mods;
        for (var i = 0; i < mods.Count; i++)
        {
            var mod = mods[i];
            if (mod is null) continue;

            if (mod.Plan.Has(HookId.ModifyInterfaceLayers)) Interface.Add(mod);
            if (mod.Plan.Has(HookId.PostDrawTiles)) AnyPostDrawTiles = true;
            if (mod.Plan.Has(HookId.PostDrawInterface)) AnyPostDrawInterface = true;
        }

        _systemsInterface = SystemLoader.With(HookId.ModifyInterfaceLayers).Length > 0;
        if (SystemLoader.With(HookId.PostDrawTiles).Length > 0) AnyPostDrawTiles = true;
        if (SystemLoader.With(HookId.PostDrawInterface).Length > 0) AnyPostDrawInterface = true;
    }

    internal static void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        for (var i = 0; i < Interface.Count; i++)
        {
            var mod = Interface[i];
            try
            {
                mod.ModifyInterfaceLayers(layers);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[interfacelayer] {mod.Name}.ModifyInterfaceLayers 异常：" + Safe.Describe(e));
            }
        }

        if (_systemsInterface) SystemLoader.ModifyInterfaceLayers(layers);
    }

    internal static void PostDrawInterface(Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch batch)
    {
        var mods = LoaderHost.Current.Mods.Mods;
        for (var i = 0; i < mods.Count; i++)
        {
            var mod = mods[i];
            if (mod is null || !mod.Plan.Has(HookId.PostDrawInterface)) continue;

            try { mod.PostDrawInterface(batch); }
            catch (Exception e) { ContentErrors.Report($"[interface] {mod.Name}.PostDrawInterface 异常：" + Safe.Describe(e)); }
        }

        SystemLoader.PostDrawInterface(batch);
    }

    internal static void PostDrawTiles()
    {
        var mods = LoaderHost.Current.Mods.Mods;
        for (var i = 0; i < mods.Count; i++)
        {
            var mod = mods[i];
            if (mod is null || !mod.Plan.Has(HookId.PostDrawTiles)) continue;

            try { mod.PostDrawTiles(); }
            catch (Exception e) { ContentErrors.Report($"[interface] {mod.Name}.PostDrawTiles 异常：" + Safe.Describe(e)); }
        }

        SystemLoader.PostDrawTiles();
    }
}
