using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>基线 NPC.TML.cs / NPC.Spawner.TML.cs / NPCID.TML.cs 的等价扩展成员；游戏类型来自 interop 代理，用 extension block 补作者 API。</summary>
public static class NpcTml
{
    extension(NPC npc)
    {
        /// <summary>这个实体对应的 ModNpc 实例；原版 NPC 返回 null。</summary>
        public ModNpc? ModNPC => NpcPipeline.Instance.InstanceOf(npc);

        /// <summary>作用在这个实体上的全局 NPC 实例（按注册序）。</summary>
        public IReadOnlyList<GlobalNpc> EntityGlobals => GlobalNpcRegistry.Of(npc);

        public T GetGlobalNPC<T>() where T : GlobalNpc
        {
            foreach (var global in GlobalNpcRegistry.Of(npc))
                if (global is T typed) return typed;

            throw new KeyNotFoundException($"NPC {npc.type} 上没有 {typeof(T).FullName}");
        }

        public T GetGlobalNPC<T>(T baseInstance) where T : GlobalNpc
        {
            foreach (var global in GlobalNpcRegistry.Of(npc))
                if (global is T typed) return typed;

            return baseInstance;
        }

        public bool TryGetGlobalNPC<T>(out T? result) where T : GlobalNpc
        {
            foreach (var global in GlobalNpcRegistry.Of(npc))
            {
                if (global is not T typed) continue;
                result = typed;
                return true;
            }

            result = null;
            return false;
        }

        public bool TryGetGlobalNPC<T>(T baseInstance, out T? result) where T : GlobalNpc
        {
            _ = baseInstance;
            return npc.TryGetGlobalNPC(out result);
        }

        /// <summary>照另一个已注册类型的默认值铺一遍；本实体的 ModNpc/Global 实例与类型号保持不变。</summary>
        public void CloneDefaults(int Type) => NpcPipeline.Instance.CloneDefaults(npc, Type);

        /// <summary>按类型号重建默认值，但保留玩家交互标记（townNPC 等）。</summary>
        public void SetDefaultsKeepPlayerInteraction(int Type) => npc.SetDefaultsKeepPlayerInteraction(Type, new NPCSpawnParams());

        public void SetDefaultsKeepPlayerInteraction(int Type, NPCSpawnParams spawnParams)
        {
            var current = npc.playerInteraction;
            var saved = current is null ? null : new bool[current.Length];
            if (current is not null && saved is not null) Array.Copy(current, saved, current.Length);

            try
            {
                npc.SetDefaults(Type, spawnParams);
            }
            finally
            {
                var target = npc.playerInteraction;
                if (saved is not null && target is not null)
                {
                    var length = Math.Min(saved.Length, target.Length);
                    for (var i = 0; i < length; i++) target[i] = saved[i];
                }
            }
        }

        /// <summary>只跑原版 AI：跳过 ModNpc/GlobalNpc 的 PreAI/AI/PostAI。</summary>
        public void VanillaAI()
        {
            if (npc is null || npc.Pointer == IntPtr.Zero) throw new NullReferenceException("NPC.VanillaAI: game object is null");
            NpcPipeline.RunVanillaAi(npc);
        }

        /// <summary>按给定的命中信息触发一次受击回调（不扣血）。</summary>
        public void HitEffect(HitInfo hit) => NpcPipeline.HitWithInfo(npc, hit);

        /// <summary>旧形态重载：方向 + 伤害 + 可选秒杀标记。</summary>
        public void HitEffect(int hitDirection, double dmg, bool? instantKill)
        {
            var instant = instantKill ?? dmg == 9999;
            var value = instant ? 1 : (int)dmg;
            NpcPipeline.HitWithInfo(npc, new HitInfo(sourceDamage: value, damage: value,
                hitDirection: hitDirection, instantKill: instant));
        }

        public ModBossBar? BossBar
        {
            get => BossBarPipeline.Get(npc);
            set => BossBarPipeline.Set(npc, value);
        }

        /// <summary>把一种负面效果加进免疫，并连带免疫所有把它写进免疫继承链的效果。</summary>
        public void BecomeImmuneTo(int buffType)
        {
            var immunity = npc.buffImmune;
            if (immunity is null) throw new InvalidOperationException("NPC.buffImmune 未初始化");
            if ((uint)buffType >= (uint)immunity.Length) throw new ArgumentOutOfRangeException(nameof(buffType));

            immunity[buffType] = true;
            var parents = NpcImmunity.ImmunityParents;
            for (var buff = 0; buff < parents.Length && buff < immunity.Length; buff++)
                if (parents[buff].Contains(buffType)) immunity[buff] = true;
        }

        /// <summary>清掉当前身上所有已被免疫的负面效果。</summary>
        public void ClearImmuneToBuffs(out bool anyBuffsCleared)
        {
            anyBuffsCleared = false;
            var immunity = npc.buffImmune;
            if (immunity is null) return;

            for (var index = NPC.maxBuffs - 1; index >= 0; index--)
            {
                if (index >= npc.buffType.Length) continue;
                var buff = npc.buffType[index];
                if (buff <= 0 || (uint)buff >= (uint)immunity.Length || !immunity[buff]) continue;
                npc.DelBuff(index, false);
                anyBuffsCleared = true;
            }

            if ((Main.netMode & 2) != 0) NetMessage.SendData(54, -1, -1, null!, npc.whoAmI);
        }

        /// <summary>这个城镇 NPC 的幸福度偏好入口（偏好写入在商店管线）。</summary>
        public NPCHappiness Happiness => new(npc.type);

        /// <summary>城镇 NPC 的显示名（读写 GivenName）。</summary>
        public string TownNPCName
        {
            get => npc.GivenName ?? "";
            set => npc.GivenName = value;
        }
    }

    extension(NPC.Spawner spawner)
    {
        public int SpawnTileX => NpcSpawn.InfoOf(spawner).SpawnTileX;
        public int SpawnTileY => NpcSpawn.InfoOf(spawner).SpawnTileY;
        public int GroundTileY => NpcSpawn.InfoOf(spawner).GroundTileY;
        public int SpawnTileType => NpcSpawn.InfoOf(spawner).SpawnTileType;
        public int SpawnWallType => NpcSpawn.InfoOf(spawner).SpawnWallType;
        public Player Player => NpcSpawn.InfoOf(spawner).Player;
        public bool SafeRangeX => NpcSpawn.InfoOf(spawner).SafeRangeX;
    }

    extension(Terraria.ID.NPCID.Sets)
    {
        public static ref bool[] ImmuneToWhipTags => ref NpcImmunity.Whips;
        public static ref bool[] ImmuneToRegularBuffs => ref NpcImmunity.Regular;
        public static ref bool?[][] SpecificDebuffImmunity => ref NpcImmunity.Specific;
    }
}
