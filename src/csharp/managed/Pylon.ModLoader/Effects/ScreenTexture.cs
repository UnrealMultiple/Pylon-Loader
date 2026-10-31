namespace Pylon.ModLoader.Effects;

/// <summary>供效果 shader 采样的 RGBA 纹理。</summary>
public sealed class ScreenTexture
{
    /// <summary>效果的内置纹理：uScreen（已画好的画面拷贝）。</summary>
    public static readonly ScreenTexture Screen = new(kTexScreen, 0, 0);

    /// <summary>效果的内置纹理：上一帧世界（已含效果）。</summary>
    public static readonly ScreenTexture History = new(kTexHistory, 0, 0);

    internal const int kTexNone = 0;
    internal const int kTexScreen = -1;
    internal const int kTexHistory = -2;

    /// <summary>内核纹理 id（提交效果时用；负数是内置纹理）。</summary>
    public int Id { get; }

    public int Width { get; }

    public int Height { get; }

    internal ScreenTexture(int id, int width, int height)
    {
        Id = id;
        Width = width;
        Height = height;
    }
}
