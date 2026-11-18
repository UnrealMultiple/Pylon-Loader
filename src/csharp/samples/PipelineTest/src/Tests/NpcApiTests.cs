using System;
using System.Linq;
using Il2CppMicrosoft.Xna.Framework;
using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Globals;
using NPCInteraction = Pylon.ModLoader.Contracts.NPCInteraction;

namespace PipelineTest.Tests;

internal static class NpcApiTests
{
    public static void Register()
    {
        PT.Group("npc-api", Phase.Ready, Ready);
        PT.Group("npc-api-w", Phase.World, World);
    }

    private static void Ready()
    {
        var type = ModContent.NPCType<TestNpc>();

        PT.Case("NPC-API-1", c =>
        {
            var npc = new NPC();
            npc.SetDefaults(type, new NPCSpawnParams());
            try
            {
                c.NotNull(npc.ModNPC, "NPC.ModNPC");
                c.True(npc.ModNPC is TestNpc, $"ModNPC 类型={npc.ModNPC?.GetType().Name}");
                c.Eq(npc.ModNPC?.Type ?? -1, type, "ModNPC.Type");
                c.True(npc.EntityGlobals.Count >= 1, $"EntityGlobals={npc.EntityGlobals.Count}");
                var global = npc.GetGlobalNPC<TestNpcApiGlobal>();
                c.NotNull(global, "GetGlobalNPC<T>");
                c.True(npc.TryGetGlobalNPC<TestNpcApiGlobal>(out var found) && found is not null, "TryGetGlobalNPC<T>");
                c.True(ReferenceEquals(global, npc.GetGlobalNPC(global)), "GetGlobalNPC<T>(baseInstance)");
            }
            finally
            {
                npc.active = false;
            }
        });

        PT.Case("NPC-API-2", c =>
        {
            var npc = new NPC();
            npc.SetDefaults(type, new NPCSpawnParams());
            try
            {
                c.True(npc.ModNPC is not null, "初始 ModNPC");

                try { npc.CloneDefaults(NPCID.BlueSlime); }
                catch (Exception e) { c.Fail("CloneDefaults threw " + e.GetType().Name + ": " + e.Message); return; }
                c.Eq(npc.type, type, "CloneDefaults 后类型");
                c.True(npc.ModNPC is TestNpc, "CloneDefaults 后 ModNPC 复原");
                c.InRange(npc.lifeMax, 1, 60, "CloneDefaults 取史莱姆生命");

                try { npc.SetDefaults(type, new NPCSpawnParams()); }
                catch (Exception e) { c.Fail("raw SetDefaults threw " + e.GetType().Name + ": " + e.Message + " | " + e.StackTrace); return; }
                c.Eq(npc.type, type, "原生 SetDefaults 后类型");
                c.Eq(npc.lifeMax, 120, "原生 SetDefaults 后生命");

                npc.playerInteraction[1] = true;
                try { npc.SetDefaultsKeepPlayerInteraction(NPCID.BlueSlime, new NPCSpawnParams()); }
                catch (Exception e) { c.Fail("SetDefaultsKeepPlayerInteraction threw " + e.GetType().Name + ": " + e.Message); return; }
                c.True(npc.playerInteraction[1], "SetDefaultsKeepPlayerInteraction 保留玩家交互");
            }
            finally
            {
                npc.active = false;
            }
        });

        PT.Case("NPC-API-3", c =>
        {
            var npc = new NPC();
            npc.SetDefaults(NPCID.BlueSlime, new NPCSpawnParams());
            npc.BecomeImmuneTo(BuffID.OnFire);
            c.True(npc.buffImmune[BuffID.OnFire], "BecomeImmuneTo(OnFire)");
            c.True(npc.buffImmune[BuffID.OnFire3], "火焰子效果连带免疫");

            npc.buffType[0] = BuffID.OnFire;
            npc.ClearImmuneToBuffs(out var cleared);
            c.True(cleared, "ClearImmuneToBuffs 命中免疫效果");
            c.Eq(npc.buffType[0], 0, "免疫效果已清");
        });

        PT.Case("NPC-API-4", c =>
        {
            var before = NPCID.Sets.ImmuneToRegularBuffs[type];
            try
            {
                NPCID.Sets.ImmuneToRegularBuffs[type] = true;
                var npc = new NPC();
                npc.SetDefaults(type, new NPCSpawnParams());
                c.True(npc.buffImmune[BuffID.Poisoned], "RegularBuffs 应用到 buffImmune");

                NPCID.Sets.ImmuneToRegularBuffs[type] = false;
                npc.SetDefaults(type, new NPCSpawnParams());
                c.True(!npc.buffImmune[BuffID.Poisoned], "复位后不再免疫");
                npc.active = false;
            }
            finally
            {
                NPCID.Sets.ImmuneToRegularBuffs[type] = before;
            }
        });

        PT.Case("NPC-API-5", c =>
        {
            var spawner = new NPC.Spawner();
            c.Eq(spawner.SpawnTileX, -1, "Spawner.SpawnTileX");
            c.Eq(spawner.SpawnTileY, -1, "Spawner.SpawnTileY");
            c.Eq(spawner.GroundTileY, -1, "Spawner.GroundTileY");
            c.Eq(spawner.SpawnTileType, -1, "Spawner.SpawnTileType");
            c.Eq(spawner.SpawnWallType, -1, "Spawner.SpawnWallType");
            c.NotNull(spawner.Player, "Spawner.Player");
            c.True(!spawner.SafeRangeX, "Spawner.SafeRangeX");
        });

        PT.Case("NPC-API-6", c =>
        {
            var npc = new NPC();
            npc.SetDefaults(type, new NPCSpawnParams());
            c.Eq(npc.Happiness.NpcType, type, "NPC.Happiness.NpcType");
            npc.TownNPCName = "测试名字";
            c.Eq(npc.TownNPCName, "测试名字", "TownNPCName 写回");
            c.Eq(npc.GivenName, "测试名字", "GivenName 直读");
            npc.active = false;
        });

        PT.Case("NPC-API-7", c =>
        {
            var list = new NPCInteractionList(type);
            var first = list.Append(new ProbeInteraction("甲"));
            list.Prepend(new ProbeInteraction("乙"));
            c.Eq(list.Entries.Count, 2, "NPCInteractionList 条目数");
            c.Eq(list.Interactions.First().GetText(), "乙", "Prepend 顺序");
            c.True(list.IsEnabled(first), "Append 默认启用");
            list.Disable(first);
            c.True(!list.IsEnabled(first), "Disable(NPCInteractionList.Entry)");
            c.Eq(list.Type, type, "NPCInteractionList.Type");
        });
    }

    private static void World()
    {
        var player = Main.LocalPlayer;

        PT.Case("NPC-API-W1", c =>
        {
            if (!Slot(out var index)) { c.Fail("没有空闲 NPC 槽位"); return; }
            var saved = Main.npc[index];
            var npc = new NPC();
            npc.SetDefaults(NPCID.BlueSlime, new NPCSpawnParams());
            npc.whoAmI = index;
            npc.active = true;
            npc.position = player.position;
            Main.npc[index] = npc;

            NpcApiProbe.Reset();
            NpcApiProbe.Active = true;
            NpcApiProbe.Index = index;
            try
            {
                npc.HitEffect(new HitInfo(sourceDamage: 5, damage: 5, hitDirection: -1));
                c.Eq(NpcApiProbe.HitEffects, 1, "GlobalNpc.HitEffect 次数");
                c.Eq(NpcApiProbe.LastHitDamage, 5, "HitEffect 伤害");
                c.Eq(NpcApiProbe.LastHitDirection, -1, "HitEffect 方向");
            }
            finally
            {
                NpcApiProbe.Reset();
                npc.active = false;
                Main.npc[index] = saved;
            }
        });

        PT.Case("NPC-API-W2", c =>
        {
            if (!Slot(out var index)) { c.Fail("没有空闲 NPC 槽位"); return; }
            var saved = Main.npc[index];
            var npc = new NPC();
            npc.SetDefaults(NPCID.BlueSlime, new NPCSpawnParams());
            npc.whoAmI = index;
            npc.active = true;
            npc.position = player.position;
            Main.npc[index] = npc;

            try
            {
                var victim = new Rectangle((int)npc.position.X, (int)npc.position.Y, 32, 32);
                var npcRect = victim;
                var setter = 0;
                var baseline = 1f;
                NPC.GetMeleeCollisionData(ref victim, index, ref setter, ref baseline, ref npcRect);

                NpcApiProbe.Reset();
                NpcApiProbe.Active = true;
                NpcApiProbe.Index = index;
                victim = new Rectangle((int)npc.position.X, (int)npc.position.Y, 32, 32);
                npcRect = victim;
                setter = 0;
                var scaled = 1f;
                NPC.GetMeleeCollisionData(ref victim, index, ref setter, ref scaled, ref npcRect);

                c.Eq(NpcApiProbe.CollisionCalls, 1, "GlobalNpc.ModifyCollisionData 次数");
                c.True(Math.Abs(scaled - baseline * NpcApiProbe.CollisionScale) < 0.001f,
                    $"伤害倍率 {scaled}（基线 {baseline} × {NpcApiProbe.CollisionScale}）");
            }
            finally
            {
                NpcApiProbe.Reset();
                npc.active = false;
                Main.npc[index] = saved;
            }
        });

        PT.Case("NPC-API-W3", c =>
        {
            var source = player.GetProjectileSource_Misc(0);
            var center = player.Center;
            var index = NPC.NewNPC(source, (int)center.X, (int)center.Y - 160,
                ModContent.NPCType<TestNpc>(), 0, 0f, 0f, 0f, 0f, 0);
            if (index < 0 || index >= Main.npc.Length)
            {
                c.Fail($"探针 NPC 生成失败 index={index}");
                return;
            }

            var npc = Main.npc[index];
            npc.AddBuff(BuffID.OnFire, 600);
            NpcApiProbe.Reset();
            NpcApiProbe.Active = true;
            NpcApiProbe.Index = index;
            PT.Later("NPC-API-W3-R", 5, cc =>
            {
                try
                {
                    cc.True(NpcApiProbe.RegenCalls > 0, $"GlobalNpc.UpdateLifeRegen 次数={NpcApiProbe.RegenCalls}");
                }
                finally
                {
                    NpcApiProbe.Reset();
                    npc.active = false;
                }
            });
        });
    }

    private static bool Slot(out int index)
    {
        for (var i = 0; i < Main.npc.Length; i++)
        {
            if (Main.npc[i] is not { active: false }) continue;
            index = i;
            return true;
        }

        index = -1;
        return false;
    }

    private sealed class ProbeInteraction(string text) : NPCInteraction
    {
        public override bool Condition() => true;
        public override string GetText() => text;
        public override void Interact() { }
    }
}
