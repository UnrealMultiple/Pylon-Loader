using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using ReLogic.Content;
using Terraria.GameContent;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss;

/// <summary>羲和专用血条：使用 Pylon 新版 BossBar 管线，保证自定义 NPC 能被精确收集。</summary>
public sealed class XiheBossBar : ModBossBar
{
    public override Asset<Texture2D> GetIconTexture(ref Rectangle? iconFrame)
    {
        // 自定义血条独立取图，不会自动采用 NPC.BossHeadSlot 的结果。
        // 复用原版光女已注册的头像槽，无需扩容或复制游戏素材。
        iconFrame = null;
        var slot = XiheBossNpc.EmpressHeadSlot;
        var heads = TextureAssets.NpcHeadBoss;
        return heads != null && slot >= 0 && slot < heads.Length ? heads[slot] : null;
    }

    // 返回 null = 交给管线按目标 NPC 的 life/lifeMax 回填。
    // 返回 true 则必须自己把 life/lifeMax 写进 ref，否则会出现 0/0。
    public override bool? ModifyInfo(BossBarInfo info, ref float life, ref float lifeMax,
        ref float shield, ref float shieldMax) => null;
}
