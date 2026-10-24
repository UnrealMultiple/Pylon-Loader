using System;
using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;

namespace RegressionTest
{
    /// <summary>
    /// 回归测试 Mod 入口：装载期注册 ImGui 页与自动布景；系统面钩子在 RegTestSystem（ModSystem）、
    /// 玩家面钩子在 RegTestPlayer（ModPlayer）。每类一个静态开关（ImGui 页拨），
    /// 计数器与开关都是静态的，方便 RegTestTab 直接读写。
    /// </summary>
    public sealed class RegressionTestMod : Mod
    {
        internal static RegressionTestMod Instance;
        internal static ModLogger Log;

        // ---- P2-8 CanConsumeBait：0=不干预(null) 1=保证消耗(true) 2=拒绝消耗(false) ----
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

        internal static bool _autoDone;
        private static int _statTick;

        public override void Load()
        {
            Instance = this;
            Log = Logger;
            RegisterImGuiTab(new RegTestTab());

            // 进世界自动执行：世界更新第 AutoRunFrame 帧布景+发物品（回归免点按；On.* 钩不依赖系统桥）。
            On.Terraria.Main.DoUpdateInWorld += AutoRunTick;
            Logger.Info("[rt] RegressionTest 已加载（进世界 3 秒后自动布景+发物品；开关面在 RegressionTest 页）");
            Arm64PatchTests.Run(Logger);
        }

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

            // 每 300 帧一行计数快照（P1-11 桥分派证据：各计数随帧推进增长）
            if (++_statTick % 300 == 0)
                Log?.Info($"[rt] 帧计数@{_statTick}：PrePlayers={CntPrePlayers} PostNPCs={CntPostNpcs} " +
                          $"PostGores={CntPostGores} PreItems={CntPreItems} PostItems={CntPostItems} " +
                          $"PostProj={CntPostProjectiles} TimeRate={CntTimeRate} Transform={CntTransform} Dye={CntDyeReward}");
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
                    if (ModContent.GetModNPC(npc.type) is not { } mod || mod.Mod?.Name != "XiheBoss")
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
    }
}
