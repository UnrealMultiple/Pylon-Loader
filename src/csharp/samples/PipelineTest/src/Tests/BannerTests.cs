using System;
using System.Linq;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 25：Banner（敌人旗子）。
/// Ready：NPC → 旗子 → 物品的映射，物品表（BannerStrength / KillsToBanner）有没有给 mod 物品补上。
/// World：击杀计数（代表 + 同族共用一面）、新旗标、领取、插地方块 → 减伤 buff。
/// </summary>
internal static class BannerTests
{
    private static int _npcType;
    private static int _kinType;
    private static int _itemType;
    private static int _bannerX = -1, _bannerY = -1;

    public static void Register()
    {
        PT.Group("banner", Phase.Ready, RunReady);
        PT.Group("banner-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("BANNER-R1", c =>
        {
            _npcType = ModContent.NPCType<Content.TestZBannerNpc>();
            _kinType = ModContent.NPCType<Content.TestZBannerKin>();
            _itemType = ModContent.ItemType<Content.TestZBannerItem>();

            c.True(_npcType >= NPCID.Count, $"代表 NPC type={_npcType}");
            c.True(_kinType >= NPCID.Count, $"同族 NPC type={_kinType}");

            var tileType = ModContent.TileType<Content.TestZBannerTile>();
            c.True(tileType >= TileID.Count, $"旗子方块 type={tileType}");
            c.True(PipelineTestMod.Instance.GetContent<ModTile>().Count() > 0,
                $"ModTile 数量={PipelineTestMod.Instance.GetContent<ModTile>().Count()}");
            c.Eq(BannerSystem.NPCtoBanner(_npcType), _npcType, "NPCtoBanner(代表)");
            c.Eq(BannerSystem.NPCtoBanner(_kinType), _npcType, "NPCtoBanner(同族)");
            c.Eq(BannerSystem.BannerToItem(_npcType), _itemType, "BannerToItem");
            c.Eq(BannerSystem.BannerToNPC(_npcType), _npcType, "BannerToNPC");
        });

        PT.Case("BANNER-R2", c =>
        {
            var strength = ItemID.Sets.BannerStrength;
            c.True(strength is not null && _itemType < strength.Length,
                $"BannerStrength 表长={strength?.Length} item={_itemType}");
            if (strength is not null && _itemType < strength.Length)
                c.True(strength[_itemType].Enabled, $"BannerStrength[{_itemType}].Enabled");

            var kills = ItemID.Sets.KillsToBanner;
            c.True(kills is not null && _itemType < kills.Length,
                $"KillsToBanner 表长={kills?.Length} item={_itemType}");
            if (kills is not null && _itemType < kills.Length)
                c.Eq(kills[_itemType], 3, "KillsToBanner");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("BANNER-W1", "玩家还没就绪");
            return;
        }

        PT.Case("BANNER-W1", c =>
        {
            var center = player.Center;
            var source = player.GetProjectileSource_Misc(0);
            var rep = NPC.NewNPC(source, (int)center.X - 120, (int)center.Y - 40, _npcType, 0, 0f, 0f, 0f, 0f, 0);
            var kin = NPC.NewNPC(source, (int)center.X + 120, (int)center.Y - 40, _kinType, 0, 0f, 0f, 0f, 0f, 0);
            c.True(rep >= 0 && kin >= 0, $"刷出代表/同族 {rep}/{kin}");

            // 三次击杀：代表两次、同族一次。KillsToBanner 被作者改成 3。
            // 旗子计数是世界级共享状态:同一世界里其它同族 NPC(含本用例刷出来的)死亡也会累加,
            // 所以只断言这三次显式调用带来的增量,绝对值会被无关实体污染。
            var bannerKillsBefore = BannerSystem.GetKillCount(_npcType);
            BannerSystem.AddNPCKillBy(_npcType, Main.myPlayer);
            BannerSystem.AddNPCKillBy(_kinType, Main.myPlayer);
            BannerSystem.AddNPCKillBy(_npcType, Main.myPlayer);

            c.Eq(BannerSystem.GetKillCount(_npcType) - bannerKillsBefore, 3, "击杀数增量");
            c.True(BannerSystem.IsNewBanner(_npcType), "新旗子标记");

            var counts = BannerSystem.GetClaimableBannerCounts();
            c.NotNull(counts, "GetClaimableBannerCounts");
            c.True(counts is not null && _npcType < counts.Length,
                $"可领取表长={counts?.Length}（应 > {_npcType}）");
            if (counts is not null && _npcType < counts.Length)
                c.Eq((int)counts[_npcType], 1, "可领取面数");
        });

        PT.Case("BANNER-W2", c =>
        {
            BannerSystem.RequestBannerClaim(_npcType, 1);

            var counts = BannerSystem.GetClaimableBannerCounts();
            c.NotNull(counts, "GetClaimableBannerCounts");
            if (counts is not null && _npcType < counts.Length)
                c.Eq((int)counts[_npcType], 0, "领取后可领数");

            c.True(HasItem(player, _itemType), "旗子物品到手");
        });

        PT.Case("BANNER-W3", c =>
        {
            var center = player.Center;
            _bannerX = (int)(center.X / 16f) - 8;
            _bannerY = (int)(player.Top.Y / 16f) - 1;
            var tileType = ModContent.TileType<Content.TestZBannerTile>();

            var data = Terraria.ObjectData.TileObjectData.GetTileData(tileType, 0, 0);
            c.NotNull(data, "旗子形状");
            if (data is null) return;

            for (var dy = 0; dy < data.Height; dy++)
                for (var dx = 0; dx < data.Width; dx++)
                    WorldGen.KillTile(_bannerX + dx, _bannerY + dy, false, false, true);

            WorldGen.PlaceTile(_bannerX, _bannerY - 1, TileID.Stone, true, true, -1, 0);
            c.True(Main.tile[_bannerX, _bannerY - 1].active() && Main.tile[_bannerX, _bannerY - 1].type == TileID.Stone,
                "锚石放下");

            var cursorX = _bannerX + data.Origin.X;
            var cursorY = _bannerY + data.Origin.Y;
            var can = Terraria.TileObject.CanPlace(cursorX, cursorY, tileType, 0, 1, out var objectData, false,
                new Il2CppSystem.Nullable<int>());
            var placed = can && Terraria.TileObject.Place(objectData);
            if (placed)
                Terraria.ObjectData.TileObjectData.CallPostPlacementPlayerHook(cursorX, cursorY, tileType, 0, 1, 0, objectData);

            var top = Main.tile[_bannerX, _bannerY];
            c.True(placed && top.active() && top.type == tileType,
                $"旗子经放置对象放下 placed={placed} tile={top.type}");
        });

        PT.Later("BANNER-W4", 60, c =>
        {
            var metrics = Main.SceneMetrics;
            c.NotNull(metrics, "Main.SceneMetrics");
            if (metrics is null) return;

            var tileType = ModContent.TileType<Content.TestZBannerTile>();
            var bx = _bannerX;
            var by = _bannerY;
            PT.Info($"旗子现场（{bx},{by}）：tile(0)={Main.tile[bx, by].type}(期望 {tileType}) " +
                    $"tile(1)={Main.tile[bx, by + 1].type} tile(2)={Main.tile[bx, by + 2].type} " +
                    $"锚石={Main.tile[bx, by - 1].type} buff表长={metrics.NPCBannerBuff?.Length} " +
                    $"击杀数={BannerSystem.GetKillCount(_npcType)} 玩家=({(int)(player.Center.X / 16f)},{(int)(player.Center.Y / 16f)})");

            var buff = metrics.NPCBannerBuff;
            c.True(buff is not null && _npcType < buff.Length && buff[_npcType], "NPCBannerBuff 点亮");
            c.True(metrics.hasBanner, "hasBanner");
            c.True(Main.LocalPlayer.HasNPCBannerBuff(_npcType), "HasNPCBannerBuff");
        });

        PT.Case("BANNER-W5", c =>
        {
            // 世界加载会重建 Lang._npcNameCache；名字必须在世界内仍然读得到。
            c.Eq(Lang.GetNPCNameValue(_npcType), "测试旗子史莱姆", "旗帜 NPC 名字");
            c.Eq(Lang.GetNPCNameValue(ModContent.NPCType<Content.TestTownNpc>()), "测试商人", "城镇 NPC 名字");
        });
    }

    private static bool HasItem(Player player, int type)
    {
        var inventory = player.inventory;
        if (inventory is not null)
        {
            for (var i = 0; i < inventory.Length; i++)
            {
                var item = inventory[i];
                if (item is not null && !item.IsAir && item.type == type) return true;
            }
        }

        var world = Main.item;
        if (world is not null)
        {
            for (var i = 0; i < world.Length; i++)
            {
                var item = world[i];
                if (item is not null && item.active && item.type == type) return true;
            }
        }

        return false;
    }
}
