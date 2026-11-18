namespace Pylon.ModLoader.Effects;

/// <summary>供效果 shader 采样的 RGBA 纹理。</summary>
public sealed class ScreenTexture
{
    public static readonly ScreenTexture Screen = new(kTexScreen, 0, 0);

    public static readonly ScreenTexture History = new(kTexHistory, 0, 0);

    internal const int kTexNone = 0;
    internal const int kTexScreen = -1;
    internal const int kTexHistory = -2;
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
