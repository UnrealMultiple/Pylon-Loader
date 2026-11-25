using System;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 挖掘范围。1:1 对应原版 <c>DiggingTool</c>：
/// hook 实际挖掘入口，在命中点周围按半径调用 WorldGen.KillTile/KillWall，
/// 多人时补发 TileChange（msgType 17）。
/// </summary>
public sealed class DiggingFeature : CheatFeature
{
    public override string Id => "world.digging";
    public override string Title => "挖掘范围";
    public override CheatCategory Category => CheatCategory.Main;
    public override string SubPage => "主要";

    public override void Install()
    {
        On.Terraria.Player.ItemCheck_UseMiningTools_ActuallyUseMiningTool += ActuallyUseMiningTool;
    }

    private static void ActuallyUseMiningTool(
        On.Terraria.Player.orig_ItemCheck_UseMiningTools_ActuallyUseMiningTool orig,
        Player self, Item item, out bool canHitWalls, int x, int y)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { DiggingKillTileRect: true } && WorldGen.InWorld(x, y, 0))
        {
            KillCoordinatesTile(x, y, Math.Clamp(cfg.DiggingRange, 1, 100), cfg.DiggingKillWallRect);
        }
        orig(self, item, out canHitWalls, x, y);
    }

    /// <summary>原版 WorldGen::KillCoordinatesTile 的 1:1 移植。</summary>
    private static void KillCoordinatesTile(int x, int y, int radius, bool killWall)
    {
        var netMode = Main.netMode;
        var maxTilesX = Main.maxTilesX;
        var maxTilesY = Main.maxTilesY;

        for (var i = x - radius; i <= x + radius; i++)
        {
            for (var j = y - radius; j <= y + radius; j++)
            {
                if (i < 0 || i >= maxTilesX || j < 0 || j >= maxTilesY) continue;
                if (!WorldGen.InWorld(i, j, 0) || !Game.TileActive(i, j)) continue;

                WorldGen.KillTile(i, j, false, false, false);
                if (netMode == 1)
                {
                    NetMessage.SendData(17, -1, -1, null, 0, i, j, 0f, 0, 0, 0);
                }

                if (!killWall) continue;
                WorldGen.KillWall(i, j, false);
                if (netMode != 0)
                {
                    NetMessage.SendData(17, -1, -1, null, 2, i, j, 0f, 0, 0, 0);
                }
            }
        }
    }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("挖掘范围", "DIGGING");
        ModernUI.BeginRow();
        ModernUI.RowLabel("范围挖掘");
        if (ModernUI.Toggle("##digrect", cfg.DiggingKillTileRect))
        {
            cfg.DiggingKillTileRect = !cfg.DiggingKillTileRect;
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();

        if (cfg.DiggingKillTileRect)
        {
            ModernUI.BeginRow();
            ModernUI.RowLabel("破坏墙体");
            if (ModernUI.Toggle("##digwall", cfg.DiggingKillWallRect))
            {
                cfg.DiggingKillWallRect = !cfg.DiggingKillWallRect;
                CheatFeatureHost.MarkDirty();
            }
            ModernUI.EndRow();

            var range = (float)cfg.DiggingRange;
            if (ModernUI.RowSlider("##digrange", "破坏半径", "1-100", ref range, 1f, 100f, "0"))
            {
                cfg.DiggingRange = Math.Clamp((int)range, 1, 100);
                CheatFeatureHost.MarkDirty();
            }
        }

        ModernUI.EndCard();
    }
}