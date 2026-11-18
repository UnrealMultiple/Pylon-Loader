using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Graphics;
using Terraria.IO;
using Terraria.Localization;
using Terraria.Map;
using Terraria.WorldBuilding;

namespace Pylon.ModLoader;

/// <summary>
/// 系统装载与派发门面：注册表按实例收集 <see cref="ModSystem"/>，按"覆写了哪个钩子"缓存过滤表，
/// 各时机入口先派 Mod 虚方法、再按注册序派每个 system 实例。世界数据以 <c>Mod.Uid/类型名</c> 为键。
/// </summary>
public static class SystemLoader
{
    private static readonly List<ModSystem> Systems = [];
    private static readonly Dictionary<HookId, ModSystem[]> ByHook = [];
    private static readonly object Gate = new();

    internal static bool Any => Systems.Count > 0;

    internal static void Add(ModSystem system)
    {
        lock (Gate)
        {
            Systems.Add(system);
            ByHook.Clear();
        }
    }

    internal static ModSystem[] Of(Mod mod)
    {
        lock (Gate)
        {
            var found = new List<ModSystem>();
            for (var i = 0; i < Systems.Count; i++)
            {
                if (Systems[i].Mod == mod) found.Add(Systems[i]);
            }
            return [.. found];
        }
    }

    internal static ModSystem[] With(HookId hook)
    {
        lock (Gate)
        {
            if (ByHook.TryGetValue(hook, out var cached)) return cached;

            var found = new List<ModSystem>();
            for (var i = 0; i < Systems.Count; i++)
            {
                if (Systems[i].Plan.Has(hook)) found.Add(Systems[i]);
            }
            var result = found.ToArray();
            ByHook[hook] = result;
            return result;
        }
    }

    internal static ModSystem[] With(string hook) =>
        Enum.TryParse<HookId>(hook, out var id) ? With(id) : [];

    internal static string KeyOf(ModSystem system) => $"{system.Mod.Uid}/{system.Name}";

    internal static ModSystem? ByKey(string key)
    {
        lock (Gate)
        {
            for (var i = 0; i < Systems.Count; i++)
            {
                if (KeyOf(Systems[i]) == key) return Systems[i];
            }
            return null;
        }
    }

    private static void Run(string hook, HookId id, Action<ModSystem> action)
    {
        var systems = With(id);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                action(systems[i]);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.{hook}", e);
            }
        }
    }

    internal static void OnModLoad(Mod mod)
    {
        var systems = Of(mod);
        for (var i = 0; i < systems.Length; i++)
        {
            if (!systems[i].Plan.Has(HookId.OnModLoad)) continue;
            try
            {
                systems[i].OnModLoad();
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.OnModLoad", e);
            }
        }
    }

    internal static void SetupContent(Mod mod)
    {
        var systems = Of(mod);
        for (var i = 0; i < systems.Length; i++)
        {
            Safe.Run($"{systems[i].FullKey}.SetStaticDefaults", systems[i].SetStaticDefaults);
        }
    }

    public static void OnModUnload() => Run(nameof(ModSystem.OnModUnload), HookId.OnModUnload, static s => s.OnModUnload());

    internal static bool Localized { get; private set; }

    public static void OnLocalizationsLoaded()
    {
        Localized = true;
        Run(nameof(ModSystem.OnLocalizationsLoaded), HookId.OnLocalizationsLoaded, static s => s.OnLocalizationsLoaded());
    }

    public static void ResizeArrays() => Run(nameof(ModSystem.ResizeArrays), HookId.ResizeArrays, static s => s.ResizeArrays());

    internal static void PostSetupContent(Mod mod)
    {
        var systems = Of(mod);
        for (var i = 0; i < systems.Length; i++)
        {
            Safe.Run($"{systems[i].FullKey}.PostSetupContent", systems[i].PostSetupContent);
        }
    }

    internal static void AddRecipeGroups(Mod mod)
    {
        var systems = Of(mod);
        for (var i = 0; i < systems.Length; i++)
        {
            Safe.Run($"{systems[i].FullKey}.AddRecipeGroups", systems[i].AddRecipeGroups);
        }
    }

    internal static void AddRecipes(Mod mod)
    {
        var systems = Of(mod);
        for (var i = 0; i < systems.Length; i++)
        {
            Safe.Run($"{systems[i].FullKey}.AddRecipes", systems[i].AddRecipes);
        }
    }

    internal static void PostAddRecipes(Mod mod)
    {
        var systems = Of(mod);
        for (var i = 0; i < systems.Length; i++)
        {
            Safe.Run($"{systems[i].FullKey}.PostAddRecipes", systems[i].PostAddRecipes);
        }
    }

    public static void PostSetupRecipes() => Run(nameof(ModSystem.PostSetupRecipes), HookId.PostSetupRecipes, static s => s.PostSetupRecipes());

    public static void OnWorldLoad() => Run(nameof(ModSystem.OnWorldLoad), HookId.OnWorldLoad, static s => s.OnWorldLoad());

    public static void OnWorldUnload() => Run(nameof(ModSystem.OnWorldUnload), HookId.OnWorldUnload, static s => s.OnWorldUnload());

    public static void ClearWorld() => Run(nameof(ModSystem.ClearWorld), HookId.ClearWorld, static s => s.ClearWorld());

    public static void PreSaveAndQuit() => Run(nameof(ModSystem.PreSaveAndQuit), HookId.PreSaveAndQuit, static s => s.PreSaveAndQuit());

    public static void PreUpdateEntities() => Run(nameof(ModSystem.PreUpdateEntities), HookId.PreUpdateEntities, static s => s.PreUpdateEntities());

    public static void PreUpdatePlayers() => Run(nameof(ModSystem.PreUpdatePlayers), HookId.PreUpdatePlayers, static s => s.PreUpdatePlayers());

    public static void PostUpdatePlayers() => Run(nameof(ModSystem.PostUpdatePlayers), HookId.PostUpdatePlayers, static s => s.PostUpdatePlayers());

    public static void PreUpdateNPCs() => Run(nameof(ModSystem.PreUpdateNPCs), HookId.PreUpdateNPCs, static s => s.PreUpdateNPCs());

    public static void PostUpdateNPCs() => Run(nameof(ModSystem.PostUpdateNPCs), HookId.PostUpdateNPCs, static s => s.PostUpdateNPCs());

    public static void PreUpdateGores() => Run(nameof(ModSystem.PreUpdateGores), HookId.PreUpdateGores, static s => s.PreUpdateGores());

    public static void PostUpdateGores() => Run(nameof(ModSystem.PostUpdateGores), HookId.PostUpdateGores, static s => s.PostUpdateGores());

    public static void PreUpdateProjectiles() => Run(nameof(ModSystem.PreUpdateProjectiles), HookId.PreUpdateProjectiles, static s => s.PreUpdateProjectiles());

    public static void PostUpdateProjectiles() => Run(nameof(ModSystem.PostUpdateProjectiles), HookId.PostUpdateProjectiles, static s => s.PostUpdateProjectiles());

    public static void PreUpdateItems() => Run(nameof(ModSystem.PreUpdateItems), HookId.PreUpdateItems, static s => s.PreUpdateItems());

    public static void PostUpdateItems() => Run(nameof(ModSystem.PostUpdateItems), HookId.PostUpdateItems, static s => s.PostUpdateItems());

    public static void PreUpdateDusts() => Run(nameof(ModSystem.PreUpdateDusts), HookId.PreUpdateDusts, static s => s.PreUpdateDusts());

    public static void PostUpdateDusts() => Run(nameof(ModSystem.PostUpdateDusts), HookId.PostUpdateDusts, static s => s.PostUpdateDusts());

    public static void PreUpdateTime() => Run(nameof(ModSystem.PreUpdateTime), HookId.PreUpdateTime, static s => s.PreUpdateTime());

    public static void PostUpdateTime() => Run(nameof(ModSystem.PostUpdateTime), HookId.PostUpdateTime, static s => s.PostUpdateTime());

    public static void PreUpdateWorld() => Run(nameof(ModSystem.PreUpdateWorld), HookId.PreUpdateWorld, static s => s.PreUpdateWorld());

    public static void PostUpdateWorld() => Run(nameof(ModSystem.PostUpdateWorld), HookId.PostUpdateWorld, static s => s.PostUpdateWorld());

    public static void PreUpdateInvasions() => Run(nameof(ModSystem.PreUpdateInvasions), HookId.PreUpdateInvasions, static s => s.PreUpdateInvasions());

    public static void PostUpdateInvasions() => Run(nameof(ModSystem.PostUpdateInvasions), HookId.PostUpdateInvasions, static s => s.PostUpdateInvasions());

    public static void PostUpdateEverything() => Run(nameof(ModSystem.PostUpdateEverything), HookId.PostUpdateEverything, static s => s.PostUpdateEverything());

    public static void PostUpdateInput() => Run(nameof(ModSystem.PostUpdateInput), HookId.PostUpdateInput, static s => s.PostUpdateInput());

    public static void ResetNearbyTileEffects() => Run(nameof(ModSystem.ResetNearbyTileEffects), HookId.ResetNearbyTileEffects, static s => s.ResetNearbyTileEffects());

    public static void UpdateUI(GameTime gameTime)
    {
        var systems = With(HookId.UpdateUI);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].UpdateUI(gameTime);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.UpdateUI", e);
            }
        }
    }

    public static void TileCountsAvailable(ReadOnlySpan<int> tileCounts)
    {
        var systems = With(HookId.TileCountsAvailable);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].TileCountsAvailable(tileCounts);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.TileCountsAvailable", e);
            }
        }
    }

    public static void ModifyTimeRate(ref double timeRate, ref double tileUpdateRate, ref double eventUpdateRate)
    {
        var systems = With(HookId.ModifyTimeRate);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].ModifyTimeRate(ref timeRate, ref tileUpdateRate, ref eventUpdateRate);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.ModifyTimeRate", e);
            }
        }
    }

    public static bool CanWorldBePlayed(PlayerFileData playerData, WorldFileData worldData, out ModSystem? rejector)
    {
        rejector = null;
        var systems = With(HookId.CanWorldBePlayed);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                if (systems[i].CanWorldBePlayed(playerData, worldData)) continue;
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.CanWorldBePlayed", e);
                continue;
            }
            rejector = systems[i];
            return false;
        }
        return true;
    }

    public static void ModifyScreenPosition()
    {
        var systems = With(HookId.ModifyScreenPosition);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].ModifyScreenPosition();
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.ModifyScreenPosition", e);
            }
        }
    }

    public static void ModifyTransformMatrix(ref SpriteViewMatrix transform)
    {
        var systems = With(HookId.ModifyTransformMatrix);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].ModifyTransformMatrix(ref transform);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.ModifyTransformMatrix", e);
            }
        }
    }

    public static void ModifySunLightColor(ref Color tileColor, ref Color backgroundColor)
    {
        if (Main.gameMenu) return;

        var systems = With(HookId.ModifySunLightColor);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].ModifySunLightColor(ref tileColor, ref backgroundColor);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.ModifySunLightColor", e);
            }
        }
    }

    public static void ModifyLightingBrightness(ref float scale)
    {
        var systems = With(HookId.ModifyLightingBrightness);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].ModifyLightingBrightness(ref scale);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.ModifyLightingBrightness", e);
            }
        }
    }

    public static void PostDrawTiles() => Run(nameof(ModSystem.PostDrawTiles), HookId.PostDrawTiles, static s => s.PostDrawTiles());

    public static void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        var systems = With(HookId.ModifyInterfaceLayers);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].ModifyInterfaceLayers(layers);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[interfacelayer] {systems[i].FullKey}.ModifyInterfaceLayers 异常：" + Safe.Describe(e));
            }
        }
    }

    public static void PostDrawInterface(SpriteBatch spriteBatch)
    {
        var systems = With(HookId.PostDrawInterface);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].PostDrawInterface(spriteBatch);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[interface] {systems[i].FullKey}.PostDrawInterface 异常：" + Safe.Describe(e));
            }
        }
    }

    public static void PreDrawMapIconOverlay(IReadOnlyList<IMapLayer> layers, MapOverlayDrawContext mapOverlayDrawContext)
    {
        var systems = With(HookId.PreDrawMapIconOverlay);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].PreDrawMapIconOverlay(layers, mapOverlayDrawContext);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.PreDrawMapIconOverlay", e);
            }
        }
    }

    public static void PostDrawFullscreenMap(ref string mouseText)
    {
        var systems = With(HookId.PostDrawFullscreenMap);
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                systems[i].PostDrawFullscreenMap(ref mouseText);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.PostDrawFullscreenMap", e);
            }
        }
    }

    internal static bool HijackGetData(ref byte messageType, ref System.IO.BinaryReader reader, int playerNumber)
    {
        var systems = With(HookId.HijackGetData);
        var hijacked = false;
        long readerPos = reader.BaseStream.Position;
        long biggestReaderPos = readerPos;
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                if (systems[i].HijackGetData(ref messageType, ref reader, playerNumber))
                {
                    hijacked = true;
                    biggestReaderPos = Math.Max(reader.BaseStream.Position, biggestReaderPos);
                }
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.HijackGetData", e);
            }
            reader.BaseStream.Position = readerPos;
        }
        if (hijacked) reader.BaseStream.Position = biggestReaderPos;
        return hijacked;
    }

    internal static bool HijackSendData(int whoAmI, int msgType, int remoteClient, int ignoreClient, NetworkText text,
                                        int number, float number2, float number3, float number4, int number5,
                                        int number6, int number7)
    {
        var systems = With(HookId.HijackSendData);
        var result = false;
        for (var i = 0; i < systems.Length; i++)
        {
            try
            {
                result |= systems[i].HijackSendData(whoAmI, msgType, remoteClient, ignoreClient, text, number,
                    number2, number3, number4, number5, number6, number7);
            }
            catch (Exception e)
            {
                Safe.Report($"{systems[i].FullKey}.HijackSendData", e);
            }
        }
        return result;
    }

    internal static ModSystem[] NetSystems() => With(HookId.NetSend);

    internal static void NetSend(ModSystem system, System.IO.BinaryWriter writer)
    {
        try
        {
            system.NetSend(writer);
        }
        catch (Exception e)
        {
            Safe.Report($"{system.FullKey}.NetSend", e);
        }
    }

    internal static void NetReceive(ModSystem system, System.IO.BinaryReader reader)
    {
        try
        {
            system.NetReceive(reader);
        }
        catch (Exception e)
        {
            Safe.Report($"{system.FullKey}.NetReceive", e);
        }
    }

    internal static void SaveWorldData(ModSystem system, TagCompound tag)
    {
        try
        {
            system.SaveWorldData(tag);
        }
        catch (Exception e)
        {
            Safe.Report($"{system.FullKey}.SaveWorldData", e);
        }
    }

    internal static void LoadWorldData(ModSystem system, TagCompound tag)
    {
        try
        {
            system.LoadWorldData(tag);
        }
        catch (Exception e)
        {
            Safe.Report($"{system.FullKey}.LoadWorldData", e);
        }
    }

    internal static void SaveWorldHeader(ModSystem system, TagCompound tag)
    {
        try
        {
            system.SaveWorldHeader(tag);
        }
        catch (Exception e)
        {
            Safe.Report($"{system.FullKey}.SaveWorldHeader", e);
        }
    }

    internal static double WeightOf(List<GenPass> tasks) => ModWorldGenHooks.WeightOf(tasks);
}
