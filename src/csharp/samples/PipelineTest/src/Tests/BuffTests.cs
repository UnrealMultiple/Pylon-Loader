using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>管线 09：Buff —— 注册面（Ready：发号 / 图标进 TextureAssets.Buff / TML 免疫表）+ 功能面（World：多槽 Update / NPC Update）。</summary>
internal static class BuffTests
{
    public static void Register()
    {
        PT.Group("buff", Phase.Ready, RunReady);
        PT.Group("buff-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        var type = ModContent.BuffType<Content.TestBuff>();

        PT.Case("BUFF-R1", c =>
        {
            c.True(type >= BuffID.Count, $"type={type}（原版 {BuffID.Count}）");
            c.NotNull(ModContent.GetInstance<Content.TestBuff>(), "GetInstance");
        });

        PT.Case("BUFF-T1", c =>
        {
            var table = TextureAssets.Buff;
            var len = table?.Length ?? 0;
            c.True(len > type, $"buff 贴图表长={len}（应 > {type}）");
            if (len > type) c.NotNull(table![type], $"[{type}] 槽位");
        });

        PT.Case("BUFF-T2", c =>
        {
            var tags = BuffID.Sets.IsATagBuff;
            c.True(tags.Length >= BuffID.Count, $"IsATagBuff 长度={tags.Length}（应 >= {BuffID.Count}）");
            c.True(tags[310] && tags[362], "原版鞭 tag 310/362 内置");

            var parents = BuffID.Sets.GrantImmunityWith;
            c.True(parents.Length >= BuffID.Count, $"GrantImmunityWith 长度={parents.Length}");
            c.True(parents[323].Contains(24) && parents[24].Contains(323), "火焰父子链 24↔323 内置");
            c.True(parents[20].Contains(70), "中毒继承 20→70 内置");

            var type2 = ModContent.BuffType<Content.TestBuff2>();
            tags[type2] = true;
            c.True(BuffID.Sets.IsATagBuff[type2], "作者写入 IsATagBuff 可读回");
            BuffID.Sets.GrantImmunityWith[type2] = [BuffID.Poisoned];
            c.True(BuffID.Sets.GrantImmunityWith[type2].Contains(BuffID.Poisoned), "作者写入 GrantImmunityWith 可读回");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("BUFF-W1", "玩家还没就绪");
            return;
        }

        var type = ModContent.BuffType<Content.TestBuff>();

        PT.Case("BUFF-W1", c =>
        {
            var existing = "";
            for (var i = 0; i < System.Math.Min(8, player.buffType.Length); i++)
                existing += $"{player.buffType[i]}:{player.buffTime[i]} ";
            var immune = player.buffImmune;
            PT.Info($"预检：debuff[{type}]={Main.debuff[type]} immuneLen={immune?.Length ?? -1} " +
                    $"immune[{type}]={immune is not null && (uint)type < (uint)immune.Length && immune[type]} " +
                    $"maxBuffs={Player.maxBuffs} 现有=[{existing}]");

            player.AddBuff(type, 3600, true);

            player.AddBuff(BuffID.WellFed, 3600, true);
            var vanillaIndex = player.FindBuffIndex(BuffID.WellFed);

            var index = player.FindBuffIndex(type);
            c.True(index >= 0, $"FindBuffIndex={index}（应 >= 0）");
            c.True(index >= 0, $"buff 槽位={index}");
            PT.Info($"对照：WellFed 槽位={vanillaIndex}；测试增益已挂上：type={type} 槽位={index} " +
                    $"剩余时间={(index >= 0 ? player.buffTime[index] : -1)} 秒牌");
        });

        RunMultiSlot(player, type);
        RunNpcUpdate(player);
    }

    private static void RunMultiSlot(Player player, int type)
    {
        var types = player.buffType;
        var times = player.buffTime;
        var savedTypes = new int[types.Length];
        var savedTimes = new int[times.Length];
        for (var i = 0; i < types.Length; i++) savedTypes[i] = types[i];
        for (var i = 0; i < times.Length; i++) savedTimes[i] = times[i];

        for (var i = 0; i < types.Length; i++)
        {
            types[i] = 0;
            times[i] = 0;
        }

        player.AddBuff(BuffID.WellFed, 3600, true);
        player.AddBuff(type, 3600, true);
        var slot = player.FindBuffIndex(type);
        var before = TestBuff.UpdateCount;
        var globalBefore = TestBuffGlobal.PlayerUpdates;

        PT.Later("BUFF-W2", 30, c =>
        {
            try
            {
                c.True(slot > 0, $"TestBuff 落在非 0 槽位={slot}");
                var delta = TestBuff.UpdateCount - before;
                c.True(delta > 0, $"非 0 槽位 ModBuff.Update 派发 {delta} 次（30 帧窗口）");
                var globalDelta = TestBuffGlobal.PlayerUpdates - globalBefore;
                c.True(globalDelta > 0, $"全局 Update 派发 {globalDelta} 次（含原版槽位）");
            }
            finally
            {
                for (var i = 0; i < types.Length; i++)
                {
                    types[i] = savedTypes[i];
                    times[i] = savedTimes[i];
                }
            }
        });
    }

    private static void RunNpcUpdate(Player player)
    {
        var type = ModContent.BuffType<Content.TestBuff2>();
        NPC? npc = null;
        foreach (var candidate in new[] { Terraria.ID.NPCID.Zombie, Terraria.ID.NPCID.Bunny, Terraria.ID.NPCID.BlueSlime })
        {
            var index = NPC.NewNPC(player.GetProjectileSource_Misc(0),
                (int)player.Center.X, (int)player.Center.Y - 120,
                candidate, 0, 0f, 0f, 0f, 0f, 0);
            if (index < 0 || index >= Main.npc.Length || Main.npc[index] is not { active: true } spawned) continue;

            npc = spawned;
            npc.AddBuff(type, 600, true);
            if (npc.FindBuffIndex(type) >= 0) break;

            var immune = npc.buffImmune;
            PT.Info($"候选 {candidate} 对 {type} 免疫（immune={(immune is not null && (uint)type < (uint)immune.Length && immune[type])}），换下一个");
            npc.active = false;
            npc = null;
        }

        if (npc is null || npc.FindBuffIndex(type) < 0)
        {
            PT.SkipCase("BUFF-W3", "没有可用非免疫 NPC（全部候选拒绝 mod 增益）");
            return;
        }

        var modBefore = BuffProbe.Mod2NpcUpdates;
        var globalBefore = TestBuffGlobal.NpcUpdates;

        PT.Later("BUFF-W3", 30, c =>
        {
            try
            {
                var modDelta = BuffProbe.Mod2NpcUpdates - modBefore;
                c.True(modDelta > 0, $"NPC 侧 ModBuff.Update 派发 {modDelta} 次（30 帧窗口）");
                var globalDelta = TestBuffGlobal.NpcUpdates - globalBefore;
                c.True(globalDelta > 0, $"NPC 侧全局 Update 派发 {globalDelta} 次");
            }
            finally
            {
                npc.active = false;
            }
        });
    }
}
