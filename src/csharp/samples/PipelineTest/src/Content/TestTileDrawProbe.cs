using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.ObjectData;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace PipelineTest.Content;

/// <summary>
/// 绘制钩子探针方块：PreDraw/PostDraw/DrawEffects/SpecialDraw 全计数；
/// DrawEffects 里请求一次 SpecialDraw（AddSpecialLegacyPoint），SpecialDraw 再计数。
/// </summary>
public sealed class TestTileDrawProbe : ModTile
{
    public static int PreDraws, PostDraws, DrawEffectsCalls, SpecialDraws, FrameCalls, KillCalls;

    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileMergeDirt[Type] = true;
        var tile = TileObjectData.newTile;
        tile.CopyFrom(TileObjectData.Style1x1);
        TileObjectData.addTile(Type);
        AddMapEntry(new Color(160, 90, 200));
    }

    public override bool CanKillTile(int i, int j, ref bool blockDamaged)
    {
        if (KillCalls == 0) PT.Info($"[probe] CanKillTile ({i},{j})");
        KillCalls++;
        return true;
    }

    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (KillCalls == 1) PT.Info($"[probe] KillTile ({i},{j}) fail={fail} effectOnly={effectOnly} noItem={noItem}");
        KillCalls++;
    }

    public override bool TileFrame(int i, int j, ref bool resetFrame, ref bool noBreak)
    {
        if (FrameCalls == 0) PT.Info($"[probe] TileFrame ({i},{j}) reset={resetFrame} noBreak={noBreak}");
        FrameCalls++;
        return true;
    }

    public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
    {
        PreDraws++;
        return true;
    }

    public override void PostDraw(int i, int j, SpriteBatch spriteBatch) => PostDraws++;

    public override void DrawEffects(int i, int j, SpriteBatch spriteBatch, TileDrawData drawData)
    {
        DrawEffectsCalls++;
        Main.instance.TilesRenderer.AddSpecialLegacyPoint(new Point(i, j));
    }

    public override void SpecialDraw(int i, int j, SpriteBatch spriteBatch) => SpecialDraws++;
}
