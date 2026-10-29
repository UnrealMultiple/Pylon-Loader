// FrameMod / TestBuff —— 示例自定义增益
// 验证：占号/静态默认值/名字与说明/UpdatePlayer/ApplyPlayer/ReApplyPlayer/ModifyBuffText/图标绘制。
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class TestBuff : ModBuff
    {
        public override string DisplayName { get; set; } = "测试增益";
        public override string Description { get; set; } = "这是一条测试增益：验证 ModBuff 管线";

        private int _ticks;
        private static readonly HashSet<string> Logged = [];

        // 自检计数器（FrameSelfTest 用）
        public static int Ticks, Draws;

        private void LogOnce(string what)
        {
            if (!Logged.Add(what)) return;
            Mod.Logger.Info($"[buff] TestBuff.{what}（type={Type}）");
        }

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = false;   // 增益（不是减益）
            LogOnce("SetStaticDefaults");
        }

        public override void UpdatePlayer(Player player, int buffIndex)
        {
            _ticks++;
            Ticks++;
            LogOnce("UpdatePlayer");   // 首次即见：证明 buff 逐帧在跑
            if (_ticks % 300 == 0)
                Mod.Logger.Info($"[buff] TestBuff.UpdatePlayer 第 {_ticks} 帧（index={buffIndex}）");
        }

        public override void ApplyPlayer(Player player, int buffTime) => LogOnce("ApplyPlayer");

        public override bool ReApplyPlayer(Player player, int buffTime, int buffIndex)
        {
            LogOnce("ReApplyPlayer");
            return true;   // 续时间
        }

        public override void ModifyBuffText(ref string buffName, ref string tip, ref int rare)
        {
            LogOnce("ModifyBuffText");
            buffName = "测试增益（已改）";
            tip = "说明也被 ModifyBuffText 改过";
        }

        public override bool PreDraw(SpriteBatch spriteBatch, int buffIndex, BuffDrawParams drawParams)
        {
            Draws++;
            LogOnce("PreDraw");
            return true;
        }

        public override void PostDraw(SpriteBatch spriteBatch, int buffIndex, BuffDrawParams drawParams) =>
            LogOnce("PostDraw");

        public override bool? CanRemove(Player player, int buffTime, int buffIndex, bool debuff)
        {
            LogOnce("CanRemove");
            return null;
        }

        public override void OnRemove(Player player, int buffTime, int buffIndex) => LogOnce("OnRemove");
    }
}
