// FrameMod / TestMount —— 示例自定义坐骑（借史莱姆外形，逻辑自定义）
// 验证：占号/MountData 安装（_Back/_Front 多层贴图）/SetMount/Dismount/UpdateFrame/UpdateEffects/Draw。
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace FrameMod
{
    public sealed class TestMount : ModMount
    {
        private static readonly HashSet<string> Logged = new();

        // 自检计数器（FrameSelfTest 用）
        public static int SetMountCount, Effects, Jumps;

        private void LogOnce(string what)
        {
            if (!Logged.Add(what)) return;
            Mod.Logger.Info($"[mount] TestMount.{what}（type={Type}）");
        }

        public override void SetStaticDefaults()
        {
            var data = MountData;
            if (data == null) return;

            var buff = ModContent.BuffType<TestMountBuff>();
            data.buff = buff;
            data.runSpeed = 9f;
            data.dashSpeed = 9f;
            data.acceleration = 0.2f;
            data.jumpHeight = 10;
            data.jumpSpeed = 8.2f;
            data.fallDamage = 0f;
            data.heightBoost = 6;
            data.flightTimeMax = 0;
            data.usesHover = false;

            // buff → 坐骑 的桥：玩家身上有这个 buff 时游戏会自动骑上（UpdateBuffs 读这张表）。
            BuffID.Sets.MountType[buff] = Type;

            LogOnce("SetStaticDefaults");
        }

        public override void SetMount(Player player, ref bool skipDust)
        {
            SetMountCount++;
            LogOnce("SetMount");
        }

        public override void Dismount(Player player, ref bool skipDust) => LogOnce("Dismount");

        public override void UpdateEffects(Player player)
        {
            Effects++;
            // 骑的时候给一点光（验证每帧钩子）
            Lighting.AddLight(player.Center, 0.35f, 0.3f, 0.6f);
            LogOnce("UpdateEffects");
        }

        public override bool UpdateFrame(Player mountedPlayer, int state, Vector2 velocity)
        {
            LogOnce("UpdateFrame");
            return true;   // 交给游戏动画
        }

        public override void JumpHeight(Player mountedPlayer, ref int jumpHeight, float xVelocity)
        {
            Jumps++;
            LogOnce("JumpHeight");
        }

        public override void JumpSpeed(Player mountedPlayer, ref float jumpSpeed, float xVelocity) => LogOnce("JumpSpeed");

        public override bool Draw(PlayerDrawSet playerDrawData, int drawType, Player drawPlayer,
            ref Texture2D texture, ref Texture2D glowTexture, ref Vector2 drawPosition, ref Rectangle frame,
            ref Color drawColor, ref Color glowColor, ref float rotation, ref SpriteEffects spriteEffects,
            ref Vector2 drawOrigin, ref float drawScale, float shadow)
        {
            LogOnce("Draw" + drawType);
            return true;
        }
    }
}
