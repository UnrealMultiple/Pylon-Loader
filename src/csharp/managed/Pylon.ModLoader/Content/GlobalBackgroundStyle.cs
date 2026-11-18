namespace Pylon.ModLoader.Content;

public abstract class GlobalBackgroundStyle : ModType
{
    public virtual void ChooseSurfaceBackgroundStyle(ref int style) { }

    public virtual void ModifyFarSurfaceFades(int style, float[] fades, float transitionSpeed) { }

    public virtual void FillUndergroundTextureArray(int style, int[] textureSlots) { }

    public virtual void ChooseUndergroundBackgroundStyle(ref int style) { }
}