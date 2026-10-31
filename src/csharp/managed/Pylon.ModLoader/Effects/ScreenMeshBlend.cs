namespace Pylon.ModLoader.Effects;

/// <summary>硬件几何路径的混合方式（与 XNA BlendState 同口径）。</summary>
public enum ScreenMeshBlend
{
    /// <summary>直接写（不混合）。</summary>
    None = 0,

    /// <summary>AlphaBlend：src * src.a + dst * (1 - src.a)。</summary>
    Alpha = 1,

    /// <summary>Additive：src * src.a + dst。</summary>
    Additive = 2,
}
