using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using ReLogic.Content;
using Terraria.GameContent;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss;

public sealed class XiheBossBar : ModBossBar
{
    public override Asset<Texture2D> GetIconTexture(ref Rectangle? iconFrame)
    {

        iconFrame = null;
        var slot = XiheBossNpc.EmpressHeadSlot;
        var heads = TextureAssets.NpcHeadBoss;
        return heads != null && slot >= 0 && slot < heads.Length ? heads[slot] : null;
    }

    public override bool? ModifyInfo(BossBarInfo info, ref float life, ref float lifeMax,
        ref float shield, ref float shieldMax) => null;
}
