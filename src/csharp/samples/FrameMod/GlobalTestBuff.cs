// FrameMod / GlobalTestBuff —— 验证 GlobalBuff 分派（对所有 buff 生效，自己按 type 挑）。
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class GlobalTestBuff : GlobalBuff
    {
        private static readonly HashSet<string> Logged = new();

        private void LogOnce(string what)
        {
            if (!Logged.Add(what)) return;
            Mod.Logger.Info($"[global] GlobalTestBuff.{what}");
        }

        public override void UpdatePlayer(int type, Player player, ref int buffIndex)
        {
            if (type == ModContent.BuffType<TestBuff>()) LogOnce("UpdatePlayer");
        }

        public override void ModifyBuffText(int type, ref string buffName, ref string tip, ref int rare)
        {
            if (type != ModContent.BuffType<TestBuff>()) return;
            LogOnce("ModifyBuffText");
            tip += "（GlobalBuff 追加）";
        }

        public override bool PreDraw(SpriteBatch spriteBatch, int type, int buffIndex, BuffDrawParams drawParams)
        {
            if (type == ModContent.BuffType<TestBuff>()) LogOnce("PreDraw");
            return true;
        }

        public override void PostDraw(SpriteBatch spriteBatch, int type, int buffIndex, BuffDrawParams drawParams)
        {
            if (type == ModContent.BuffType<TestBuff>()) LogOnce("PostDraw");
        }

        public override bool RightClick(int type, int buffIndex)
        {
            LogOnce("RightClick");
            return true;
        }
    }
}
