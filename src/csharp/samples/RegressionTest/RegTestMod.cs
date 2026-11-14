using System;
using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;
using Terraria.Graphics;
using Terraria.ID;

namespace RegressionTest
{
    /// <summary>
    /// 回归测试 Mod 入口：Mod 级钩子全挂在这里，每类一个静态开关（ImGui 页拨）。
    /// 计数器与开关都是静态的，方便 RegTestTab 直接读写。
    /// </summary>
    public sealed class RegressionTestMod : Mod
    {
        internal static RegressionTestMod Instance;
        internal static ModLogger Log;

        // ---- P2-8 ConsumeBait：0=不干预(null) 1=保证消耗(true) 2=拒绝消耗(false) ----
        public static int BaitMode;

        // ---- P2-8b GetDyeTraderReward：0=不干预 1=加 2=删 3=清空 4=塞负数（单次动作）----
        public static int DyeAction;

        // ---- P1-11c ModifyTimeRate：0=原速 1=两倍 2=半速 ----
        public static int TimeRateMode;

        // ---- P1-11d ModifyTransformMatrix：Zoom 改成 1.25（本版 Translation 只读，平移不可表达）----
        public static bool ZoomOn;

        // ---- P2-9 UpdateLifeRegen 开关 ----
        public static bool MerchantVampire;   // 商人回血 +6
        public static bool BossDrain;         // Boss 掉血 -10

        // ---- P1-11a/b 每帧钩计数 ----
        public static int CntPrePlayers, CntPostNpcs, CntPostGores, CntPreItems, CntPostItems, CntPostProjectiles;
        public static int CntSaveWorld, CntSaveHeader, CntLoadWorld;
        public static int CntConsumeBait, CntDyeReward, CntTimeRate, CntTransform;

        public override void Load()
        {
            Instance = this;
            Log = Logger;
            RegisterImGuiTab(new RegTestTab());

            // 进世界自动执行：世界更新第 AutoRunFrame 帧布景+发物品（回归免点按；On.* 钩不依赖系统桥）。
            On.Terraria.Main.DoUpdateInWorld += AutoRunTick;
            Logger.Info("[rt] RegressionTest 已加载（进世界 3 秒后自动布景+发物品；开关面在 RegressionTest 页）");
        }

        public override void OnWorldUnload()
        {
            RegTestScene.ResetForWorldReload();
            _autoDone = false;
        }

        private static bool _autoDone;

        private static void AutoRunTick(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
        {
            orig(self);
            if (!_autoDone)
            {
                if (--RegTestScene.AutoRunFrame > 0) return;
                _autoDone = true;
                DespawnXiheBoss();
                Log?.Info("[rt] 自动执行：布景 + 发全套物品");
                RegTestScene.BuildAllScenes();
                RegTestScene.GiveAllForRegression();
                return;
            }
            RegTestScene.WatchDoor();
        }

        /// <summary>清掉羲和 Boss 的 NPC（回归不要 Boss 战干扰；存档后世界不再带 Boss 复发）。</summary>
        private static void DespawnXiheBoss()
        {
            try
            {
                var killed = 0;
                for (var i = 0; i < Main.npc.Length; i++)
                {
                    var n = Main.npc[i];
                    if (n is not { } npc || !npc.active) continue;
                    // 原版类型 Find 返回 null，天然跳过；mod NPC 按 mod 名匹配。
                    if (ModContent.Find(ContentKind.Npc, npc.type) is not { } mod || mod.Mod?.Name != "XiheBoss")
                        continue;
                    npc.active = false;
                    killed++;
                }
                if (killed > 0)
                    Log?.Info($"[rt] 已清除羲和 Boss NPC ×{killed}（回归不受 Boss 战干扰）");
            }
            catch (Exception e)
            {
                Log?.Warn("[rt] 清除羲和 Boss 失败：" + e.Message);
            }
        }

        // ===================== P1-11a/b：每帧阶段计数 =====================

        public override void PreUpdatePlayers() => CntPrePlayers++;
        public override void PostUpdateNPCs() => CntPostNpcs++;
        public override void PostUpdateGores() => CntPostGores++;
        public override void PreUpdateItems() => CntPreItems++;
        public override void PostUpdateItems() => CntPostItems++;
        public override void PostUpdateProjectiles() => CntPostProjectiles++;

        // ===================== P1-11 外围：世界数据读写计数 =====================

        public override void SaveWorldData(TagCompound tag)
        {
            CntSaveWorld++;
            tag.Set("rtSaves", CntSaveWorld);
            Log.Info($"[rt] SaveWorldData 第 {CntSaveWorld} 次");
        }

        public override void SaveWorldHeader(TagCompound tag)
        {
            CntSaveHeader++;
            tag.Set("rtHeader", CntSaveHeader);
            Log.Info($"[rt] SaveWorldHeader 第 {CntSaveHeader} 次");
        }

        public override void LoadWorldData(TagCompound tag)
        {
            CntLoadWorld++;
            var saves = tag.GetInt("rtSaves");
            Log.Info($"[rt] LoadWorldData 第 {CntLoadWorld} 次（读到历史 rtSaves={saves}）");
        }

        // ===================== P2-8：ConsumeBait（审计新语义） =====================

        public override bool? ConsumeBait(ref int baitTypeUsed)
        {
            CntConsumeBait++;
            switch (BaitMode)
            {
                case 1:
                    Log.Info($"[rt] ConsumeBait→true（保证消耗一颗，bait={baitTypeUsed}）");
                    return true;
                case 2:
                    Log.Info($"[rt] ConsumeBait→false（拒绝消耗：鱼应照上钩、饵应保留）");
                    return false;
                default:
                    return null; // 原版概率消耗
            }
        }

        // ===================== P2-8b：GetDyeTraderReward（池手术在点击时置位，单次生效） =====================

        public override void GetDyeTraderReward(ref List<int> pool)
        {
            CntDyeReward++;
            switch (DyeAction)
            {
                case 1:
                    pool.Add(ItemID.Fireblossom);
                    Log.Info($"[rt] 染料池 +1（现有 {pool.Count} 条）");
                    break;
                case 2:
                    if (pool.Count > 0) { pool.RemoveAt(pool.Count - 1); Log.Info($"[rt] 染料池 -1（剩 {pool.Count} 条）"); }
                    break;
                case 3:
                    pool.Clear();
                    Log.Info("[rt] 染料池已清空（发放应不崩，补 0）");
                    break;
                case 4:
                    pool.Add(-5);
                    Log.Info($"[rt] 染料池塞负数（现有 {pool.Count} 条，-5 应被丢弃）");
                    break;
                default:
                    Log.Info($"[rt] GetDyeTraderReward 旁路观察（池 {pool.Count} 条）");
                    break;
            }
            DyeAction = 0;
        }

        // ===================== P1-11c/d：时钟与矩阵 =====================

        public override void ModifyTimeRate(ref double timeRate, ref double tileUpdateRate, ref double eventUpdateRate)
        {
            CntTimeRate++;
            if (TimeRateMode == 1) timeRate = 2.0;
            else if (TimeRateMode == 2) timeRate = 0.5;
        }

        public override void ModifyTransformMatrix(ref SpriteViewMatrix transform)
        {
            CntTransform++;
            if (ZoomOn)
            {
                var z = transform.Zoom;
                transform.Zoom = new Il2CppMicrosoft.Xna.Framework.Vector2(1.25f, 1.25f);
                _ = z;
            }
        }
    }
}
