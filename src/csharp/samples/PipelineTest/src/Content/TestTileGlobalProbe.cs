using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>GlobalTile 探针：所有横切钩子都计数，供 tile-w 组断言"全局也收到了"。</summary>
public sealed class TestTileGlobalProbe : GlobalTile
{
    public static bool VetoCanKill, VetoCanDrop, VetoPlace, VetoExplode, VetoReplace;
    public static bool DangerTrue, SpelunkTrue;
    public static int CanKillCalls, KillCalls, NumDustCalls, CreateDustCalls, CanDropCalls, DropCalls;
    public static int CanPlaceCalls, CanExplodeCalls, CanReplaceCalls, FrameCalls, MouseOverFarCalls;
    public static int AdjTilesCalls, AnimateCalls, SpriteEffectsCalls, DangerousCalls, SpelunkableCalls;
    public static int PreDrawCalls, PostDrawCalls, DrawEffectsCalls, SpecialDrawCalls;

    public static void Reset()
    {
        VetoCanKill = VetoCanDrop = VetoPlace = VetoExplode = VetoReplace = false;
        DangerTrue = SpelunkTrue = false;
        CanKillCalls = KillCalls = NumDustCalls = CreateDustCalls = CanDropCalls = DropCalls = 0;
        CanPlaceCalls = CanExplodeCalls = CanReplaceCalls = FrameCalls = MouseOverFarCalls = 0;
        AdjTilesCalls = AnimateCalls = SpriteEffectsCalls = DangerousCalls = SpelunkableCalls = 0;
        PreDrawCalls = PostDrawCalls = DrawEffectsCalls = SpecialDrawCalls = 0;
    }

    public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged)
    {
        CanKillCalls++;
        return !VetoCanKill;
    }

    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem) => KillCalls++;

    public override void NumDust(int i, int j, int type, bool fail, ref int num) => NumDustCalls++;

    public override bool CreateDust(int i, int j, int type, ref int dustType)
    {
        CreateDustCalls++;
        return true;
    }

    public override bool CanDrop(int i, int j, int type)
    {
        CanDropCalls++;
        return !VetoCanDrop;
    }

    public override void Drop(int i, int j, int type) => DropCalls++;

    public override bool CanPlace(int i, int j, int type)
    {
        CanPlaceCalls++;
        return !VetoPlace;
    }

    public override bool CanExplode(int i, int j, int type)
    {
        CanExplodeCalls++;
        return !VetoExplode;
    }

    public override bool CanReplace(int i, int j, int type, int tileTypeBeingPlaced)
    {
        CanReplaceCalls++;
        return !VetoReplace;
    }

    public override bool TileFrame(int i, int j, int type, ref bool resetFrame, ref bool noBreak)
    {
        FrameCalls++;
        return true;
    }

    public override void MouseOverFar(int i, int j, int type) => MouseOverFarCalls++;

    public override int[] AdjTiles(int type)
    {
        AdjTilesCalls++;
        return [];
    }

    public override void AnimateTile() => AnimateCalls++;

    public override void SetSpriteEffects(int i, int j, int type, ref SpriteEffects spriteEffects) => SpriteEffectsCalls++;

    public override bool? IsTileDangerous(int i, int j, int type, Player player)
    {
        DangerousCalls++;
        return DangerTrue ? true : null;
    }

    public override bool? IsTileSpelunkable(int i, int j, int type)
    {
        SpelunkableCalls++;
        return SpelunkTrue ? true : null;
    }

    public override bool PreDraw(int i, int j, int type, SpriteBatch spriteBatch)
    {
        PreDrawCalls++;
        return true;
    }

    public override void PostDraw(int i, int j, int type, SpriteBatch spriteBatch) => PostDrawCalls++;

    public override void DrawEffects(int i, int j, int type, SpriteBatch spriteBatch, ref TileDrawData drawData) =>
        DrawEffectsCalls++;

    public override void SpecialDraw(int i, int j, int type, SpriteBatch spriteBatch) => SpecialDrawCalls++;
}
