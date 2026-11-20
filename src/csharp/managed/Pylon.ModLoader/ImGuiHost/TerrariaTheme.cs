using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>界面通用绘制基元：设计稿像素换算与描边文字；配色一律取自 <see cref="UiTheme"/>。</summary>
public static class TerrariaTheme
{
    private const float BodyOutline = 1f;
    private static readonly System.Numerics.Vector2[] OutlineOffsets =
    [
        new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f),
        new(-1f, -1f), new(1f, -1f), new(-1f, 1f), new(1f, 1f),
    ];

    /// <summary>把设计稿像素换算成当前 UI 比例下的像素。</summary>
    public static float Px(float designPixels) => designPixels * UiSettings.Scale;

    public static Vector4 Hex(uint rgb, float alpha = 1f) => UiTheme.Hex(rgb, alpha);

    public static Vector4 Alpha(Vector4 color, float alpha) => UiTheme.Alpha(color, alpha);

    public static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    internal static void PushVec2(ImGuiStyleVar index, Vector2 value)
    {
        ImGui.PushStyleVarX(index, value.X);
        ImGui.PushStyleVarY(index, value.Y);
    }

    public static void TextOutlined(ImDrawListPtr dl, Vector2 pos, Vector4 color, string text, float outline = BodyOutline)
    {
        if (string.IsNullOrEmpty(text)) return;
        var offset = Px(outline);
        var shadow = U32(new Vector4(0f, 0f, 0f, 0.92f));
        for (var i = 0; i < OutlineOffsets.Length; i++)
            dl.AddText(pos + OutlineOffsets[i] * offset, shadow, text);
        dl.AddText(pos, U32(color), text);
    }
}
