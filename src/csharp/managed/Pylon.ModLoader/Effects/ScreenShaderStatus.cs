namespace Pylon.ModLoader.Effects;

/// <summary>着色器编译状态（与内核 ShaderState 一致）。</summary>
public enum ScreenShaderStatus
{
    Pending = 0,
    Ready = 1,
    Failed = -1,
}
