namespace Pylon.ModLoader.Effects;

/// <summary>硬件几何路径的混合方式（与 XNA BlendState 同口径）。</summary>
public enum ScreenMeshBlend
{
    None = 0,

    Alpha = 1,

    Additive = 2,

    Premultiplied = 3,
}
