using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

internal static class BuffInteractionProbe
{
    internal static bool Active, VetoGlobal, VetoMod, VetoRemove;
    internal static int GlobalClicks, ModClicks, RemovedTime, RemovedCount, PreDraws, PostDraws;
    internal static void Reset()
    {
        VetoGlobal = VetoMod = VetoRemove = false;
        GlobalClicks = ModClicks = RemovedTime = RemovedCount = PreDraws = PostDraws = 0;
    }
}

public sealed class TestBuffInteraction : ModBuff
{
    public override string TexturePath => "assets/buffs/TestBuff.png";
    public override void SetStaticDefaults() => Main.debuff[Type] = false;
    public override bool RightClick(int buffIndex)
    {
        if (!BuffInteractionProbe.Active) return true;
        BuffInteractionProbe.ModClicks++;
        return !BuffInteractionProbe.VetoMod;
    }
    public override bool? CanRemove(Player player, int buffTime, int buffIndex, bool debuff) =>
        BuffInteractionProbe.Active && BuffInteractionProbe.VetoRemove ? false : null;
    public override void OnRemove(Player player, int buffTime, int buffIndex)
    {
        if (!BuffInteractionProbe.Active) return;
        BuffInteractionProbe.RemovedCount++;
        BuffInteractionProbe.RemovedTime = buffTime;
    }
    public override bool PreDraw(SpriteBatch spriteBatch, int buffIndex, BuffDrawParams drawParams)
    {
        if (!BuffInteractionProbe.Active) return true;
        BuffInteractionProbe.PreDraws++;
        drawParams.Position += new Vector2(7, 9);
        drawParams.TextPosition += new Vector2(3, 4);
        drawParams.SourceRectangle = new Rectangle(0, 0, 16, 16);
        drawParams.DrawColor = Color.Red;
        return true;
    }
    public override void PostDraw(SpriteBatch spriteBatch, int buffIndex, BuffDrawParams drawParams)
    {
        if (BuffInteractionProbe.Active) BuffInteractionProbe.PostDraws++;
    }
}

public sealed class TestBuffInteractionGlobal : GlobalBuff
{
    public override bool RightClick(int type, int buffIndex)
    {
        if (!BuffInteractionProbe.Active) return true;
        BuffInteractionProbe.GlobalClicks++;
        return !BuffInteractionProbe.VetoGlobal;
    }
}
