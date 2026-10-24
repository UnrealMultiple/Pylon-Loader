// FrameMod / TestMountBuff —— 坐骑 buff（BuffID.Sets.MountType 指向 TestMount）
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class TestMountBuff : ModBuff
    {
        public override string DisplayName { get; set; } = "测试坐骑";
        public override string Description { get; set; } = "骑上测试史莱姆（验证 ModMount 管线）";

        private static bool _logged;

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = false;
            if (_logged) return;
            _logged = true;
            Mod.Logger.Info($"[mount] TestMountBuff 静态默认值（type={Type}）");
        }

        public override void UpdatePlayer(Player player, int buffIndex)
        {
            if (player.mount != null && player.mount._type == ModContent.MountType<TestMount>()) return;
        }
    }
}
