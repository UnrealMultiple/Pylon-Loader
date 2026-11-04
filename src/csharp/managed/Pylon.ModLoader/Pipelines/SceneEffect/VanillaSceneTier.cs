using Terraria;
using Terraria.GameContent.Events;

namespace Pylon.ModLoader;

/// <summary>
/// 原版这一帧的场景优先级（参考实现的数值表移植，逐项对着 <c>refs/dump.cs</c> 核过存在性）：
/// 制作者名单 &gt; 火把神 &gt; NPC 群（入侵/首领）&gt; 事件（血月/南瓜/雪/史莱姆雨/闪光/城镇/沙尘暴/地狱）
/// &gt; 环境群落（神庙/地牢/蘑菇/腐化/猩红）&gt; 次级群落。
/// 作者的 <c>Priority</c> 达到这一档才准压过原版那一路的音乐。
/// 本构建没有 <c>SceneEffectPriority</c> 枚举（tML 侧的东西），直接用 1..7 的整数档。
/// </summary>
internal static class VanillaSceneTier
{
    public const int None = 0, BiomeLow = 1, BiomeMedium = 2, BiomeHigh = 3, Environment = 4, Event = 5,
        BossLow = 6, BossMedium = 7, BossHigh = 8;

    /// <summary>NPC 类型 → 音乐组（键=NPC 号，值=原版决定音乐时用的组号）；没列的不产生音乐优先级。</summary>
    private static readonly Dictionary<int, (int Normal, int Otherworld)> Groups = Build();

    private static Dictionary<int, (int, int)> Build()
    {
        var map = new Dictionary<int, (int, int)>();

        void Add(int[] types, int normal, int otherworld)
        {
            foreach (var type in types) map[type] = (normal, otherworld);
        }

        Add([13, 14, 15], 23, 1);
        Add([26, 27, 28, 29, 111, 471, 472], 11, 11);
        Add([35, 36], 24, 0);
        Add([50], 19, 0);
        Add([113, 114], 2, 3);
        Add([125, 126], 21, 2);
        Add([127], 22, 2);
        Add([128, 129, 130, 131], 22, 0);
        Add([134], 18, 2);
        Add([135, 136], 18, 0);
        Add([143, 144, 145], 3, 11);
        Add([266], 3, 1);
        Add([212, 213, 214, 215, 216, 252, 491, 662], 8, 8);
        Add([222], 5, 1);
        Add([245], 4, 2);
        Add([262, 263, 264], 6, 6);
        Add([370], 15, 15);
        Add([381, 382, 383, 384, 385, 386, 387, 388, 389, 390, 391, 392, 395, 520], 9, 9);
        Add([398], 7, 7);
        Add([422, 493, 507, 517], 10, 10);
        Add([439], 20, 2);
        Add([636], 14, 14);
        Add([657], 13, 13);
        Add([668], 16, 16);
        return map;
    }

    /// <summary>音乐组号 → 优先级档（7 = 火把神那路不在这里；0 = 该组不抬优先级）。</summary>
    private static int TierOfGroup(int group)
    {
        if (group == 7) return BossHigh;
        if (group is 17 or 9 or 10 or 6) return BossMedium;
        if (group is 8 or 11 or 12) return Environment;
        return group > 0 ? Event : 0;
    }

    /// <summary>屏幕附近哪一组 NPC 在说话（入侵/首领按屏幕判定，和原版音乐选择的视野一致）。</summary>
    private static int NpcGroup(bool otherworld)
    {
        var screen = Main.screenPosition;
        var sx = (int)screen.X;
        var sy = (int)screen.Y;
        var sw = Main.screenWidth;
        var sh = Main.screenHeight;
        var oldOnes = Terraria.ID.NPCID.Sets.BelongsToInvasionOldOnesArmy;
        var special = Main.remixWorld && Main.getGoodWorld;
        var npcs = Main.npc;

        for (var i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            if (npc is null || !npc.active) continue;

            var type = npc.type;
            var range = 5000;
            var group = 0;

            if (type is 379 or 438)
            {
                if (type == 379 ? npc.ai[3] >= 0 : npc.ai[1] == 1)
                {
                    range = 1600;
                    group = otherworld ? 2 : 20;
                }
            }
            else if (Groups.TryGetValue(type, out var g))
            {
                group = otherworld ? g.Otherworld : g.Normal;
            }

            if (type < oldOnes.Length && oldOnes[type]) group = 12;
            if (group == 0 && npc.boss) group = 1;
            if (special && type is 127 or 134 or 125 or 126) group = 17;
            if (group == 0) continue;

            var position = npc.position;
            var cx = (int)(position.X + npc.width / 2) - range;
            var cy = (int)(position.Y + npc.height / 2) - range;

            if (sx < cx + range * 2 && cx < sx + sw && sy < cy + range * 2 && cy < sy + sh) return group;
        }

        return 0;
    }

    /// <summary>原版这一帧的场景优先级档（1..8）；作者要压过它就得 ≥ 这个值。</summary>
    public static int Tier(bool otherworld)
    {
        if (CreditsRollEvent.IsEventOngoing) return 9;

        var metrics = Main.SceneMetrics;
        if (metrics.InTorchGodMinigame) return BossHigh;

        if (!Main.showSplash)
        {
            var group = NpcGroup(otherworld);
            var tier = TierOfGroup(group);
            if (tier > 0 && !(otherworld && group >= 18)) return tier;
        }

        var screen = Main.screenPosition;
        var screenHeight = Main.screenHeight;
        double surfaceY = Main.worldSurface * 16 + screenHeight / 2;
        var high = screen.Y / 16 < Main.worldSurface + 10 || Main.remixWorld;

        if (high && (Main.pumpkinMoon || Main.snowMoon)) return Event;
        if (!otherworld && Main.ShouldPlayRainbowBoulderMusic) return Event;

        var center = metrics.Center;
        var cy = center.Y;
        double underworld = Main.UnderworldLayer * 16;
        double rock = Main.rockLayer * 16;
        var town = metrics.TownNPCCount >= 3 && !metrics.ZoneShadowCandle && !metrics.ZoneGraveyard;
        double scale = Math.Pow(Main.maxTilesX / 4200.0, 2);
        var space = ((screen.Y + screenHeight / 2) / 16 - (65 + 10 * scale)) / (Main.worldSurface / 5);
        var environment =
            (Main.eclipse && (Main.remixWorld ? cy > rock : cy < surfaceY)) ||
            (!otherworld && Main.slimeRain && !metrics.ZoneGraveyard && (!Main.bloodMoon || Main.dayTime) && cy < surfaceY) ||
            (Main.remixWorld && Main.bloodMoon && !metrics.ZoneCrimson && !metrics.ZoneCorrupt && cy > rock && cy <= underworld) ||
            (Main.remixWorld && Main.bloodMoon && cy > underworld && center.X / 16 > Main.maxTilesX * 0.37 + 50 && center.X / 16 < Main.maxTilesX * 0.63) ||
            metrics.ZoneShimmer ||
            (!otherworld && town && Main.dayTime && ((Main.cloudAlpha == 0 && !Main._shouldUseWindyDayMusic) || cy >= surfaceY)) ||
            (!otherworld && town && !Main.dayTime && ((!Main.bloodMoon && Main.cloudAlpha == 0) || cy >= surfaceY)) ||
            metrics.ZoneSandstorm || cy > underworld || space < 1;

        if (environment) return Environment;

        var tx = (int)(center.X / 16);
        var ty = (int)(cy / 16);
        var inWorld = tx >= 0 && ty >= 0 && tx < Main.maxTilesX && ty < Main.maxTilesY;
        var temple = inWorld && Main.tile[tx, ty].wall == 87;
        var mushroom = (Main.bgStyle == 9 && cy < surfaceY) || Main.undergroundBackground == 2;

        if (temple || metrics.ZoneDungeon || mushroom || metrics.ZoneCorrupt || metrics.ZoneCrimson) return BiomeHigh;
        if (metrics.ZoneMeteor || metrics.ZoneGraveyard || metrics.ZoneJungle || metrics.ZoneSnow ||
            (!otherworld && (metrics.ZoneUndergroundDesert || metrics.ZoneDesert))) return BiomeMedium;

        return BiomeLow;
    }
}
