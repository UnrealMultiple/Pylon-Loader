using System;
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

    public static void Register()
    {
        PT.Group("banner", Phase.Ready, RunReady);
        PT.Group("banner-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("BANNER-R1", c =>
        {
            _npcType = ModContent.NpcType<Content.TestZBannerNpc>();
            _kinType = ModContent.NpcType<Content.TestZBannerKin>();
            _itemType = ModContent.ItemType<Content.TestZBannerItem>();

            c.True(_npcType >= NPCID.Count, $"代表 NPC type={_npcType}");
            c.True(_kinType >= NPCID.Count, $"同族 NPC type={_kinType}");

            var tileType = ModContent.TileType<Content.TestZBannerTile>();
            c.True(tileType >= TileID.Count, $"旗子方块 type={tileType}");
            c.True(ModContent.GetContent<ModTile>().Count > 0,
                $"ModTile 数量={ModContent.GetContent<ModTile>().Count}");
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
            BannerSystem.AddNPCKillBy(_npcType, Main.myPlayer);
            BannerSystem.AddNPCKillBy(_kinType, Main.myPlayer);
            BannerSystem.AddNPCKillBy(_npcType, Main.myPlayer);

            c.Eq(BannerSystem.GetKillCount(_npcType), 3, "击杀数");
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
            var bx = (int)(center.X / 16f) + 3;
            var by = (int)(player.Top.Y / 16f) - 1;
            var tileType = ModContent.TileType<Content.TestZBannerTile>();

            // Banner 管线只按 (tile.type, frameX / 18) 反查旗子，所以这里直接写 3 格。
            // 不调用 WorldGen.PlaceObject：它依赖 TileObjectData，而方块管线的 SetStaticDefaults
            // 在安装期跑（TileObjectData 已锁）——那是 Tile 管线的独立缺陷，不阻塞 Banner 的 buff 验证。
            for (var dy = 0; dy < 3; dy++)
            {
                var tile = Main.tile[bx, by + dy];
                tile.active(true);
                tile.type = (ushort)tileType;
                tile.frameX = 0;
                tile.frameY = (short)(dy * 18);
            }

            var top = Main.tile[bx, by];
            c.True(top.active() && top.type == tileType, $"旗子方块已写入 tile={top.type} frameY={top.frameY}");
        });

        // 场景扫描每 20 帧一次；给 60 帧再查 buff。
        PT.Later("BANNER-W4", 60, c =>
        {
            var metrics = Main.SceneMetrics;
            c.NotNull(metrics, "Main.SceneMetrics");
            if (metrics is null) return;

            var buff = metrics.NPCBannerBuff;
            c.True(buff is not null && _npcType < buff.Length && buff[_npcType], "NPCBannerBuff 点亮");
            c.True(metrics.hasBanner, "hasBanner");
            c.True(Main.LocalPlayer.HasNPCBannerBuff(_npcType), "HasNPCBannerBuff");
        });

        PT.Case("BANNER-W5", c =>
        {
            // 世界加载会重建 Lang._npcNameCache；名字必须在世界内仍然读得到。
            c.Eq(Lang.GetNPCNameValue(_npcType), "测试旗子史莱姆", "旗帜 NPC 名字");
            c.Eq(Lang.GetNPCNameValue(ModContent.NpcType<Content.TestTownNpc>()), "测试商人", "城镇 NPC 名字");
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
