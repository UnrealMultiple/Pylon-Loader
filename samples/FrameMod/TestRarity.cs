// FrameMod / TestRarity —— 示例自定义稀有度（青蓝色，前缀升降一档）
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class TestRarity : ModRarity
    {
        public override Color RarityColor => new Color(120, 230, 255);

        public override int GetPrefixedRarity(int offset, float valueMult) => Type + offset;

        private static bool _logged;

        public override void SetStaticDefaults()
        {
            if (_logged) return;
            _logged = true;
            Mod.Logger.Info($"[rarity] TestRarity 静态默认值（type={Type}）");
        }
    }
}
