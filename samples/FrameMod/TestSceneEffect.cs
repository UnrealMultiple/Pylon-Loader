using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    internal sealed class TestSceneEffect : ModSceneEffect
    {
        internal static bool Active;

        public override bool IsSceneEffectActive(Player player) => Active;
        public override int Priority => 8;
        public override int Music => MusicLoader.GetMusicSlot(Mod, "Music/TestMusic");
    }
}


