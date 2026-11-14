using System;
using Pylon.Mod;
using Pylon.ModLoader;
using Pylon.ModLoader.UI;
using Terraria;
using Terraria.ID;

namespace RegressionTest
{
    /// <summary>回归控制页：开关、计数、布景按钮、发物品。</summary>
    public sealed class RegTestTab : IModImGuiTab
    {
        public string Title => "RegressionTest";

        private static string _status = "";
        private static string _merchantState = "未生成";
        private static readonly string[] BaitModes = ["不干预（null）", "保证消耗一颗（true）", "拒绝消耗（false）"];
        private static readonly string[] TimeModes = ["原速", "两倍速", "半速"];
        private static readonly string[] DyeModes = ["不干预", "加 1 条", "删 1 条", "清空", "塞负数"];

        public static void Status(string s)
        {
            _status = s;
            RegressionTestMod.Log?.Info("[rt] " + s);
        }

        public static void UpdateMerchantState(string s) => _merchantState = s;

        public void Draw()
        {
            var m = RegressionTestMod.Instance;
            if (m == null) return;

            TerrariaUI.SeparatorText("P1-11a/b 每帧钩计数");
            TerrariaUI.DimText($"PrePlayers={RegressionTestMod.CntPrePlayers}  PostNPCs={RegressionTestMod.CntPostNpcs}  PostGores={RegressionTestMod.CntPostGores}");
            TerrariaUI.DimText($"PreItems={RegressionTestMod.CntPreItems}  PostItems={RegressionTestMod.CntPostItems}  PostProjectiles={RegressionTestMod.CntPostProjectiles}");
            TerrariaUI.DimText($"SaveWorld={RegressionTestMod.CntSaveWorld}  SaveHeader={RegressionTestMod.CntSaveHeader}  LoadWorld={RegressionTestMod.CntLoadWorld}");
            TerrariaUI.DimText($"ConsumeBait={RegressionTestMod.CntConsumeBait}  DyeReward={RegressionTestMod.CntDyeReward}  TimeRate={RegressionTestMod.CntTimeRate}  Transform={RegressionTestMod.CntTransform}");

            TerrariaUI.SeparatorText("P2-8 鱼饵消耗（ConsumeBait）");
            if (TerrariaUI.Combo("模式", ref RegressionTestMod.BaitMode, BaitModes))
                Status($"ConsumeBait 模式 → {BaitModes[RegressionTestMod.BaitMode]}");

            TerrariaUI.SeparatorText("P2-8b 染料商奖励池");
            var dye = RegressionTestMod.DyeAction;
            TerrariaUI.Combo("下次发放动作", ref dye, DyeModes);
            RegressionTestMod.DyeAction = dye;
            if (TerrariaUI.Button("找染料商并发放一次"))
                RegTestScene.InvokeDyeReward();

            TerrariaUI.SeparatorText("P1-11c/d 时钟与矩阵");
            var tm = RegressionTestMod.TimeRateMode;
            if (TerrariaUI.Combo("昼夜倍率", ref tm, TimeModes))
            {
                RegressionTestMod.TimeRateMode = tm;
                Status($"ModifyTimeRate → {TimeModes[tm]}");
            }
            var zoom = RegressionTestMod.ZoomOn;
            if (TerrariaUI.Checkbox("画面 Zoom 1.25（P1-11d）", ref zoom))
            {
                RegressionTestMod.ZoomOn = zoom;
                Status($"ModifyTransformMatrix Zoom → {zoom}");
            }

            TerrariaUI.SeparatorText("P2-9 UpdateLifeRegen");
            var vamp = RegressionTestMod.MerchantVampire;
            if (TerrariaUI.Checkbox("商人吸血 +6", ref vamp))
            {
                RegressionTestMod.MerchantVampire = vamp;
                Status($"商人吸血 → {vamp}");
            }
            var drain = RegressionTestMod.BossDrain;
            if (TerrariaUI.Checkbox("Boss 掉血 -10", ref drain))
            {
                RegressionTestMod.BossDrain = drain;
                Status($"Boss 掉血 → {drain}");
            }

            TerrariaUI.SeparatorText("布景（P1-6/7/8、P2-3/6/7、GiMms）");
            if (TerrariaUI.Button("⓪ 一键布景（第二幕全套）")) RegTestScene.BuildAllScenes();
            if (TerrariaUI.Button("① 布平台+家具+作物+制作站")) RegTestScene.BuildPlatformScene();
            if (TerrariaUI.Button("② 布门对")) RegTestScene.PlaceDoorPair();
            if (TerrariaUI.Button("③ 布墙三变体")) RegTestScene.PlaceWallTrio();
            if (TerrariaUI.Button("④ 建房+生成商人")) RegTestScene.BuildHouseAndSpawn();
            if (TerrariaUI.Button("⑤ 生成掉血 Boss")) RegTestScene.SpawnBoss();
            if (TerrariaUI.Button("⑥ 倒水（mod 水样式）")) RegTestScene.PourWater();
            if (TerrariaUI.Button("⑦ 布一个裸方块（无形状对照）")) RegTestScene.PlaceBareTile();
            if (TerrariaUI.Button("⑧ 种地下作物 ×2")) RegTestScene.PlantUndergroundCrop();
            TerrariaUI.DimText("商人状态：" + _merchantState);

            TerrariaUI.SeparatorText("发物品（P1-1/P2-1/P2-2/P1-3）");
            if (TerrariaUI.Button("数据物品 ×2（原版前缀/无前缀）")) RegTestScene.GiveDataItems();
            if (TerrariaUI.Button("数据物品（mod 前缀）")) RegTestScene.GiveModPrefixItem();
            if (TerrariaUI.Button("数据物品（陌生之印·独立mod）")) RegTestScene.GiveStrangerPrefixItem();
            if (TerrariaUI.Button("火焰物品 / 无火焰物品")) RegTestScene.GiveFlameItems();
            if (TerrariaUI.Button("召唤物 + 仆从")) RegTestScene.GiveSummonItem();
            if (TerrariaUI.Button("钓具（鱼竿+鱼饵）")) RegTestScene.GiveFishingGear();

            if (!string.IsNullOrEmpty(_status))
                TerrariaUI.Status(_status);
        }
    }
}
