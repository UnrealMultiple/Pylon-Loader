// 层：框架 / Patch —— NPC.SetDefaults 拦截
//
// 原版 SetDefaults 对 mod 类型照样跑：清字段、写 type/netID、补 scale=1/color=White/alpha=0，
// 之后才是 mod 的 SetDefaults → ApplyBuffImmunity → PostSetDefaults，最后按 mod 值重做游戏尾部。
// 早期跳过原版 → new NPC() 的 color 是透明黑（绘制乘 npc.color），真机上 NPC 完全透明；
// 那条 `type > NPCID.Count-1` 比较在 IL2CPP 上改不了，这里等价补"原版本来会写的出厂值"（ResetDefaults）。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>NPC.SetDefaults 的 mod 类型接管（原版之后叠自定义默认值）。</summary>
    internal static class NpcSetDefaultsPatch
    {
        /// <summary>自定义 NPC 的最后失败原因（给 Mod 侧日志取证）。</summary>
        public static Action<string> Error = _ => { };

        private static bool _installed;

        /// <summary>注册期装一次（幂等）。</summary>
        internal static void Install(ModLogger logger)
        {
            if (_installed) return;
            _installed = true;

            On.Terraria.NPC.SetDefaults += SetDefaults;
        }

        /// <summary>原版跑完之后：mod 的默认值 + finishModNpc，再分派 GlobalNpc。</summary>
        private static void SetDefaults(On.Terraria.NPC.orig_SetDefaults orig, NPC self, int type,
                                        Terraria.NPCSpawnParams spawnParams)
        {
            orig(self, type, spawnParams);

            if (self == null || type < 0) return;

            var template = ModContent.Find(ContentKind.Npc, type) as ModNpc;
            if (template != null) Run(self, type);

            DispatchGlobals(self);
        }

        private static void Run(NPC npc, int type)
        {
            try
            {
                // 原版那条 `type > NPCID.Count-1` 比较在 IL2CPP 上改不了：这里等价补"原版清空后应有的出厂值"
                // （否则槽位复用留旧值、new NPC() 的 color 是透明黑 → 画出来是透明的）。
                ResetDefaults(npc);
                npc.type = type;

                // 实体实例：每只 NPC 一份。
                var mod = NpcEntities.Rebind(npc);
                if (mod == null) return;

                mod.SetDefaults(npc);
                mod.ApplyBuffImmunity(npc);
                mod.PostSetDefaults(npc);
                Finish(npc, mod);
            }
            catch (Exception e)
            {
                try { Error?.Invoke($"NPC.SetDefaults 自定义分支异常：{e.Message}"); } catch { }
            }
        }

        /// <summary>原版 SetDefaults 对 mod 类型会提前 return：这里补上它本来会写的那组默认值。</summary>
        private static void ResetDefaults(NPC npc)
        {
            // ai / localAI 归零（原版清空；ai 是 struct 属性的数组，取出来改完不用回写）。
            var ai = npc.ai;
            if (ai != null) for (var i = 0; i < ai.Length; i++) ai[i] = 0f;
            var localAI = npc.localAI;
            if (localAI != null) for (var i = 0; i < localAI.Length; i++) localAI[i] = 0f;

            npc.width = 0;
            npc.height = 0;
            npc.life = 0;
            npc.lifeMax = 0;
            npc.defLifeMax = 0;
            npc.damage = 0;
            npc.defDamage = 0;
            npc.defense = 0;
            npc.defDefense = 0;
            npc.aiStyle = 0;
            npc.townNPC = false;
            npc.friendly = false;
            npc.npcSlots = 0f;
            npc.frame = new Rectangle(0, 0, 0, 0);
            npc.frameCounter = 0;
            npc.direction = 0;
            npc.spriteDirection = 0;
            npc.scale = 1f;
            npc.color = Color.White;
            npc.alpha = 0;
            npc.behindTiles = false;
            npc.rarity = 0;
        }

        /// <summary>游戏 SetDefaults 末尾那几步，用 mod 的值重做一遍。</summary>
        private static void Finish(NPC npc, ModNpc mod)
        {
            var type = mod.Type;

            // 作者没写宽高时按贴图尺寸补（竖排帧条：整宽 / 整高÷帧数）。
            if (npc.width <= 0 || npc.height <= 0)
            {
                NpcLoader.TextureSize(type, out var width, out var height);
                if (npc.width <= 0 && width > 0) npc.width = width;
                if (npc.height <= 0 && height > 0) npc.height = height;
            }

            npc.type = type;
            npc.netID = type;
            npc.active = true;
            // 原版 SetDefaults 的尾部在 mod 填值之前就跑过了：生命/基础攻防都是 0，这里按 mod 的值重做。
            npc.defDamage = npc.damage;
            npc.defDefense = npc.defense;
            npc.life = npc.lifeMax;
            // ScaleStats 对 friendly/townNPC 会提前退出、不写 defLifeMax；留 0 的话 morador 的
            // AI_007 会把 lifeMax 清零（真机踩过）。
            npc.defLifeMax = npc.lifeMax;

            // 人数缩放：两个空 Nullable 调 ScaleStats（等价于"当前人数/不覆盖强度"）；缩放会改 lifeMax，再补满。
            try
            {
                npc.ScaleStats(new Il2CppSystem.Nullable<int>(), new Il2CppSystem.Nullable<float>());
                npc.life = npc.lifeMax;
            }
            catch (Exception e) { Error?.Invoke($"{mod.Name} 的 ScaleStats 跳过：{e.Message}"); }
        }

        /// <summary>原版之后、任何 NPC 都分派 Global 的 SetDefaults。</summary>
        private static void DispatchGlobals(NPC npc)
        {
            if (npc == null || npc.type <= 0) return;
            if (!GlobalNpcRegistry.AnyWith("SetDefaults")) return;
            // 实体级列表每次都重建：槽位复用/同类型重生不能继承上一只的 AppliesToEntity 结果。
            GlobalNpcRegistry.Attach(npc, true);
            foreach (var g in GlobalNpcRegistry.For(npc, "SetDefaults"))
            {
                try { g.SetDefaults(npc); }
                catch (Exception e) { Safe.Report(g.GetType().Name + ".SetDefaults", e); }
            }
        }
    }
}
