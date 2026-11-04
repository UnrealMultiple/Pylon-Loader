using Terraria;
using Terraria.DataStructures;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>
/// 传送晶塔管线：只接"这个类型有没有、有没有任何"这两条查询，靠我们自己的 tile entity 列表回答。
/// 地图图标与传送请求需要 mod 晶塔进游戏的列表，本版没有这个设施。
/// </summary>
internal sealed class PylonPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static PylonPipeline Instance { get; } = new();

    public string Name => "pylon";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.GameContent.TeleportPylonsSystem.HasPylonOfType += HasPylonOfType;
        On.Terraria.GameContent.TeleportPylonsSystem.HasAnyPylon += HasAnyPylon;
        On.Terraria.GameContent.TeleportPylonsSystem.IsPlayerNearAPylon += IsPlayerNearAPylon;
        On.Terraria.GameContent.TeleportPylonsSystem.HandleTeleportRequest += HandleTeleportRequest;
        On.Terraria.Map.TeleportPylonsMapLayer.Draw += DrawMapLayer;

        Log.Info("[pylon] 已挂（类型分配 / 有无判定 / 附近判定 / 传送请求 / 地图图标）");
        return true;
    }

    // ===================== 地图图标 =====================

    /// <summary>原版画完自己的晶塔图标，再把 mod 晶塔的图标画上去。</summary>
    private static void DrawMapLayer(On.Terraria.Map.TeleportPylonsMapLayer.orig_Draw orig,
        Terraria.Map.TeleportPylonsMapLayer self, ref Terraria.Map.MapOverlayDrawContext context, ref string text)
    {
        orig(self, ref context, ref text);

        if (PylonTable.Count == 0) return;

        foreach (var entity in TileEntityBinding.All)
        {
            if (entity is not TEModdedPylon pylon) continue;
            if (!TileEntityBinding.IsLive(pylon)) continue;
            if (pylon.TryGetModPylon() is not { } modPylon) continue;

            var info = new TeleportPylonInfo
            {
                PositionInTiles = new Point16(pylon.Position.X, pylon.Position.Y),
                TypeOfPylon = (TeleportPylonType)modPylon.PylonType,
            };

            try
            {
                modPylon.DrawMapIcon(ref context, ref text, info,
                                     IsNear(pylon),
                                     new Color(255, 255, 255, 255),
                                     0.6f, 1f);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[pylon] {modPylon.Name}.DrawMapIcon 异常：{Safe.Describe(e)}");
            }
        }
    }

    private static bool IsNear(TEModdedPylon pylon)
    {
        var player = Main.LocalPlayer;
        if (player is null) return false;

        var dx = player.Center.X / 16f - pylon.Position.X;
        var dy = player.Center.Y / 16f - pylon.Position.Y;
        return dx * dx + dy * dy < 6400f; // 80 格
    }

    // ===================== 传送 =====================

    /// <summary>传送请求：mod 晶塔自己处理（原版列表里没有它们）。</summary>
    private static void HandleTeleportRequest(On.Terraria.GameContent.TeleportPylonsSystem.orig_HandleTeleportRequest orig,
        TeleportPylonsSystem self, TeleportPylonInfo info, int playerIndex)
    {
        var type = (int)info.TypeOfPylon;
        if (PylonTable.Of(type) is not { } modPylon)
        {
            orig(self, info, playerIndex);
            return;
        }

        Player? player;
        try
        {
            player = Main.player[playerIndex];
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[pylon] 传送取玩家失败：{Safe.Describe(e)}");
            return;
        }

        if (player is null) return;

        // 两个有效性检查：作者可以拒绝。
        try
        {
            if (!modPylon.ValidTeleportCheck_NPCCount(info, 2)) return;
            if (!modPylon.ValidTeleportCheck_BiomeRequirements(info, Main.SceneMetrics)) return;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[pylon] {modPylon.Name} 的传送有效性检查异常：{Safe.Describe(e)}");
            return;
        }

        TeleportPlayer(player, info.PositionInTiles);
    }

    private static void TeleportPlayer(Player player, Point16 tile)
    {
        try
        {
            player.Teleport(new Vector2(tile.X * 16f + 8f, tile.Y * 16f + 8f),
                            TeleportationStyleID.TeleportationPylon, 0);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[pylon] 传送失败：{Safe.Describe(e)}");
        }
    }

    /// <summary>玩家脚边有没有 mod 晶塔（原版只认自己列表里的）。</summary>
    private static bool IsPlayerNearAPylon(On.Terraria.GameContent.TeleportPylonsSystem.orig_IsPlayerNearAPylon orig, Player player)
    {
        if (orig(player)) return true;

        foreach (var entity in TileEntityBinding.All)
        {
            if (entity is not TEModdedPylon pylon) continue;
            if (!TileEntityBinding.IsLive(pylon)) continue;

            var dx = player.Center.X / 16f - pylon.Position.X;
            var dy = player.Center.Y / 16f - pylon.Position.Y;
            if (dx * dx + dy * dy < 6400f) return true;
        }

        return false;
    }

    public void Refresh() => PylonTable.Assign();

    /// <summary>安装期一次：晶塔的静态默认值（形状、放置规则都在这里）。</summary>
    internal void Build()
    {
        foreach (var pylon in ModContent.GetContent<ModPylon>())
        {
            if (!pylon.Registered) continue;
            pylon.SetStaticDefaults();
        }

        if (PylonTable.Count > 0) Log.Info($"[pylon] 安装：{PylonTable.Count} 个晶塔");
    }

    // ===================== 两条系统查询 =====================

    private static bool HasPylonOfType(On.Terraria.GameContent.TeleportPylonsSystem.orig_HasPylonOfType orig,
                                       TeleportPylonsSystem self, TeleportPylonType pylonType)
    {
        var type = (int)pylonType;
        if (PylonTable.Of(type) is null) return orig(self, pylonType);

        return Exists(type);
    }

    private static bool HasAnyPylon(On.Terraria.GameContent.TeleportPylonsSystem.orig_HasAnyPylon orig,
                                    TeleportPylonsSystem self)
    {
        foreach (var entity in TileEntityBinding.All)
        {
            if (entity is not TEModdedPylon pylon) continue;
            if (!TileEntityBinding.IsLive(pylon)) continue;

            return true;
        }

        return orig(self);
    }

    /// <summary>世界里有没有这种类型的 mod 晶塔：看我们自己登记的实体，不扫方块。</summary>
    private static bool Exists(int pylonType)
    {
        foreach (var entity in TileEntityBinding.All)
        {
            if (entity is not TEModdedPylon pylon) continue;
            if (!TileEntityBinding.IsLive(pylon)) continue;
            if (pylon.TryGetModPylon() is not { } modPylon) continue;
            if (modPylon.PylonType == pylonType) return true;
        }

        return false;
    }
}
