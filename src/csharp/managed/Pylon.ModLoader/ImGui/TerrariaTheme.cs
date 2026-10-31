using System.Numerics;
using Pylon.ImGui;
using ImGuiApi = Pylon.ImGui.ImGui;

namespace Pylon.ModLoader.UI
{
    /// <summary>
    /// Terraria 1.4「蓝钢」界面主题：配色、ImGui 全局样式与像素面板/描边文字绘制基元。
    /// 色值与尺寸对齐仓库根目录的 terraria-ui-preview.html；控件层见 <see cref="TerrariaUI"/>。
    /// </summary>
    public static class TerrariaTheme
    {
        private const float BodyOutline = 1f;
        private static readonly Vector2[] OutlineOffsets =
        [
            new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f),
            new(-1f, -1f), new(1f, -1f), new(-1f, 1f), new(1f, 1f),
        ];

        public static readonly Vector4 PanelTop = Hex(0x3E4A78);
        public static readonly Vector4 PanelMid = Hex(0x333D64);
        public static readonly Vector4 PanelBottom = Hex(0x2A3354);

        public static readonly Vector4 NavTop = Hex(0x2C3354);
        public static readonly Vector4 NavMid = Hex(0x272F4C);
        public static readonly Vector4 NavBottom = Hex(0x232A45);
        public static readonly Vector4 TitleTop = Hex(0x4A5480);
        public static readonly Vector4 TitleBottom = Hex(0x333A5A);

        public static readonly Vector4 Border = Hex(0x0B0E18);
        public static readonly Vector4 TextMain = Hex(0xFFFFFF);
        public static readonly Vector4 TextDim = Hex(0xA9B2D6);
        public static readonly Vector4 Success = Hex(0x9FE8A5);

        public static readonly Vector4 Gold = Hex(0xE9C15B);
        public static readonly Vector4 GoldBright = Hex(0xFFE28A);
        public static readonly Vector4 GoldDeep = Hex(0x8A7430);

        public static readonly Vector4 ButtonTop = Hex(0x5C6C9E);
        public static readonly Vector4 ButtonMid = Hex(0x47567F);
        public static readonly Vector4 ButtonBottom = Hex(0x3A476B);
        public static readonly Vector4 ButtonHoverTop = Hex(0x6E5F2E);
        public static readonly Vector4 ButtonHoverMid = Hex(0x5A4E24);
        public static readonly Vector4 ButtonHoverBottom = Hex(0x4A411E);
        public static readonly Vector4 ButtonDownTop = Hex(0x4A411E);
        public static readonly Vector4 ButtonDownMid = Hex(0x3E3619);
        public static readonly Vector4 ButtonDownBottom = Hex(0x332C14);

        public static readonly Vector4 FieldTop = Hex(0x10141F);
        public static readonly Vector4 FieldMid = Hex(0x1A2036);
        public static readonly Vector4 FieldBottom = Hex(0x232A42);
        public static readonly Vector4 PopupTop = Hex(0x2A3152);
        public static readonly Vector4 PopupMid = Hex(0x262D4A);
        public static readonly Vector4 PopupBottom = Hex(0x20263F);

        private static readonly Vector4 Shadow = new(0f, 0f, 0f, 0.92f);
        private static readonly Vector4 BevelLight = new(1f, 1f, 1f, 0.16f);
        private static readonly Vector4 BevelLightSoft = new(1f, 1f, 1f, 0.07f);
        private static readonly Vector4 BevelDark = new(0f, 0f, 0f, 0.55f);
        private static readonly Vector4 BevelDarkSoft = new(0f, 0f, 0f, 0.40f);

        /// <summary>把设计稿像素换算成当前 UI 比例下的像素。</summary>
        public static float Px(float designPixels) => designPixels * UiSettings.Scale;

        /// <summary>按钮三种状态对应的上/中/下渐变色。</summary>
        public static void ButtonGradient(bool hovered, bool held,
                                          out Vector4 top, out Vector4 middle, out Vector4 bottom)
        {
            top = held ? ButtonDownTop : hovered ? ButtonHoverTop : ButtonTop;
            middle = held ? ButtonDownMid : hovered ? ButtonHoverMid : ButtonMid;
            bottom = held ? ButtonDownBottom : hovered ? ButtonHoverBottom : ButtonBottom;
        }

        public static Vector4 Hex(uint rgb, float alpha = 1f) => new(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            alpha);

        public static Vector4 Alpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

        public static uint U32(Vector4 color) => ImGuiApi.ColorConvertFloat4ToU32(color);

        /// <summary>PushStyleVar 只暴露 float 版本，二维 padding/spacing 需要 X/Y 各压一次。</summary>
        internal static void PushVec2(ImGuiStyleVar index, Vector2 value)
        {
            ImGuiApi.PushStyleVarX(index, value.X);
            ImGuiApi.PushStyleVarY(index, value.Y);
        }

        /// <summary>把 Terraria 配色施加到 ImGui 全局样式。<see cref="ImGuiContext.Create"/> 调用一次。</summary>
        internal static void Apply()
        {
            var style = ImGuiApi.GetStyle();

            style.WindowPadding = new Vector2(12f, 10f);
            style.FramePadding = new Vector2(12f, 6f);
            style.ItemSpacing = new Vector2(8f, 8f);
            style.ItemInnerSpacing = new Vector2(8f, 6f);
            style.WindowRounding = 0f;
            style.ChildRounding = 0f;
            style.PopupRounding = 0f;
            style.FrameRounding = 0f;
            style.GrabRounding = 0f;
            style.TabRounding = 0f;
            style.ScrollbarRounding = 0f;
            style.WindowBorderSize = 0f;
            style.ChildBorderSize = 2f;
            style.PopupBorderSize = 2f;
            style.FrameBorderSize = 2f;
            style.ScrollbarSize = 14f;
            style.GrabMinSize = 20f;
            style.SeparatorTextBorderSize = 2f;
            style.SeparatorTextPadding = new Vector2(16f, 8f);
            style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
            style.SelectableTextAlign = new Vector2(0f, 0.5f);
            style.Alpha = 1f;

            Set(ImGuiCol.Text, TextMain);
            Set(ImGuiCol.TextDisabled, Alpha(TextDim, 0.72f));
            Set(ImGuiCol.WindowBg, PanelMid);
            Set(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
            Set(ImGuiCol.PopupBg, PopupBottom);
            Set(ImGuiCol.Border, Border);
            Set(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f));
            Set(ImGuiCol.FrameBg, FieldTop);
            Set(ImGuiCol.FrameBgHovered, Hex(0x2C3555));
            Set(ImGuiCol.FrameBgActive, FieldBottom);
            Set(ImGuiCol.TitleBg, TitleBottom);
            Set(ImGuiCol.TitleBgActive, TitleTop);
            Set(ImGuiCol.TitleBgCollapsed, TitleBottom);
            Set(ImGuiCol.MenuBarBg, NavBottom);
            Set(ImGuiCol.ScrollbarBg, FieldMid);
            Set(ImGuiCol.ScrollbarGrab, Hex(0x4C5680));
            Set(ImGuiCol.ScrollbarGrabHovered, Hex(0x6E5F2E));
            Set(ImGuiCol.ScrollbarGrabActive, Gold);
            Set(ImGuiCol.CheckMark, GoldBright);
            Set(ImGuiCol.SliderGrab, Gold);
            Set(ImGuiCol.SliderGrabActive, GoldBright);
            Set(ImGuiCol.Button, ButtonMid);
            Set(ImGuiCol.ButtonHovered, ButtonHoverMid);
            Set(ImGuiCol.ButtonActive, ButtonDownMid);
            Set(ImGuiCol.Header, Alpha(Gold, 0.18f));
            Set(ImGuiCol.HeaderHovered, Alpha(Gold, 0.28f));
            Set(ImGuiCol.HeaderActive, Alpha(Gold, 0.38f));
            Set(ImGuiCol.Separator, GoldDeep);
            Set(ImGuiCol.SeparatorHovered, Gold);
            Set(ImGuiCol.SeparatorActive, GoldBright);
            Set(ImGuiCol.ResizeGrip, Alpha(Gold, 0.20f));
            Set(ImGuiCol.ResizeGripHovered, Alpha(Gold, 0.55f));
            Set(ImGuiCol.ResizeGripActive, Gold);
            Set(ImGuiCol.InputTextCursor, Gold);
            Set(ImGuiCol.TabHovered, Alpha(Gold, 0.35f));
            Set(ImGuiCol.Tab, Alpha(Gold, 0.12f));
            Set(ImGuiCol.TabSelected, Alpha(Gold, 0.30f));
            Set(ImGuiCol.TabSelectedOverline, Gold);
            Set(ImGuiCol.TabDimmed, Alpha(Gold, 0.06f));
            Set(ImGuiCol.TabDimmedSelected, Alpha(Gold, 0.18f));
            Set(ImGuiCol.TabDimmedSelectedOverline, Alpha(Gold, 0.55f));
            Set(ImGuiCol.PlotLines, Gold);
            Set(ImGuiCol.PlotLinesHovered, GoldBright);
            Set(ImGuiCol.PlotHistogram, GoldDeep);
            Set(ImGuiCol.PlotHistogramHovered, Gold);
            Set(ImGuiCol.TableHeaderBg, FieldTop);
            Set(ImGuiCol.TableBorderStrong, Border);
            Set(ImGuiCol.TableBorderLight, Alpha(GoldDeep, 0.45f));
            Set(ImGuiCol.TableRowBg, new Vector4(0f, 0f, 0f, 0f));
            Set(ImGuiCol.TableRowBgAlt, Alpha(Gold, 0.04f));
            Set(ImGuiCol.TextLink, Gold);
            Set(ImGuiCol.TextSelectedBg, Alpha(Gold, 0.28f));
            Set(ImGuiCol.TreeLines, GoldDeep);
            Set(ImGuiCol.DragDropTarget, Gold);
            Set(ImGuiCol.NavCursor, Gold);
            Set(ImGuiCol.NavWindowingHighlight, GoldBright);
            Set(ImGuiCol.NavWindowingDimBg, new Vector4(0f, 0f, 0f, 0.55f));
            Set(ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0.55f));
        }

        private static void Set(ImGuiCol index, Vector4 color)
            => ImGuiApi.GetStyle().Colors[(int)index] = color;

        public static void Fill(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 color)
            => dl.AddRectFilled(min, max, U32(color));

        public static void Gradient(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 top, Vector4 bottom)
            => dl.AddRectFilledMultiColor(min, max, U32(top), U32(top), U32(bottom), U32(bottom));

        /// <summary>上/中/下三段渐变，对应 CSS 的 0%/48%/100% 色标。</summary>
        public static void Gradient3(ImDrawListPtr dl, Vector2 min, Vector2 max,
                                     Vector4 top, Vector4 middle, Vector4 bottom)
        {
            if (max.Y <= min.Y) return;
            var split = min.Y + (max.Y - min.Y) * 0.48f;
            Gradient(dl, min, new Vector2(max.X, split), top, middle);
            Gradient(dl, new Vector2(min.X, split), max, middle, bottom);
        }

        public static void Outline(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 color, float thickness)
        {
            var half = thickness * 0.5f;
            dl.AddRect(min + new Vector2(half, half), max - new Vector2(half, half),
                U32(color), 0f, ImDrawFlags.None, thickness);
        }

        /// <summary>凸起内倒角：上/左亮边，下/右暗边。</summary>
        public static void Bevel(ImDrawListPtr dl, Vector2 min, Vector2 max, float inset, float thickness)
        {
            var left = min.X + inset;
            var top = min.Y + inset;
            var right = max.X - inset;
            var bottom = max.Y - inset;
            if (right <= left || bottom <= top || thickness <= 0f) return;

            var half = thickness * 0.5f;
            dl.AddRectFilled(new Vector2(left, top), new Vector2(right, top + thickness), U32(BevelLight));
            dl.AddRectFilled(new Vector2(left, top), new Vector2(left + half, bottom), U32(BevelLightSoft));
            dl.AddRectFilled(new Vector2(left, bottom - thickness), new Vector2(right, bottom), U32(BevelDark));
            dl.AddRectFilled(new Vector2(right - half, top), new Vector2(right, bottom), U32(BevelDarkSoft));
        }

        private static void BevelInverted(ImDrawListPtr dl, Vector2 min, Vector2 max, float inset, float thickness)
        {
            var left = min.X + inset;
            var top = min.Y + inset;
            var right = max.X - inset;
            var bottom = max.Y - inset;
            if (right <= left || bottom <= top || thickness <= 0f) return;

            var half = thickness * 0.5f;
            dl.AddRectFilled(new Vector2(left, top), new Vector2(right, top + thickness), U32(BevelDark));
            dl.AddRectFilled(new Vector2(left, top), new Vector2(left + half, bottom), U32(BevelDark));
            dl.AddRectFilled(new Vector2(left, bottom - thickness), new Vector2(right, bottom), U32(BevelLightSoft));
            dl.AddRectFilled(new Vector2(right - half, top), new Vector2(right, bottom), U32(BevelLightSoft));
        }

        /// <summary>带黑描边 + 内外倒角的凸起面板/按钮。</summary>
        public static void Raised(ImDrawListPtr dl, Vector2 min, Vector2 max,
                                  Vector4 top, Vector4 middle, Vector4 bottom,
                                  float border = 2f, float bevel = 2f)
        {
            Gradient3(dl, min, max, top, middle, bottom);
            Bevel(dl, min, max, border, bevel);
            Outline(dl, min, max, Border, border);
        }

        /// <summary>凹陷面板（滑块轨道、勾选框、下拉列表）。</summary>
        public static void Sunken(ImDrawListPtr dl, Vector2 min, Vector2 max,
                                  Vector4 top, Vector4 middle, Vector4 bottom,
                                  float border = 2f, float bevel = 2f)
        {
            Gradient3(dl, min, max, top, middle, bottom);
            BevelInverted(dl, min, max, border, bevel);
            Outline(dl, min, max, Border, border);
        }

        /// <summary>像素描边文字：先铺 8 向黑边再写正文，模拟 Terraria 的描边字体。</summary>
        public static void TextOutlined(ImDrawListPtr dl, Vector2 pos, Vector4 color, string text, float outline = BodyOutline)
        {
            if (string.IsNullOrEmpty(text)) return;
            var offset = Px(outline);
            var shadow = U32(Shadow);
            for (var i = 0; i < OutlineOffsets.Length; i++)
                dl.AddText(pos + OutlineOffsets[i] * offset, shadow, text);
            dl.AddText(pos, U32(color), text);
        }

        public static void TextCentered(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 color,
                                        string text, float outline = BodyOutline)
        {
            if (string.IsNullOrEmpty(text)) return;
            var size = ImGuiApi.CalcTextSize(text);
            TextOutlined(dl, new Vector2(
                min.X + (max.X - min.X - size.X) * 0.5f,
                min.Y + (max.Y - min.Y - size.Y) * 0.5f), color, text, outline);
        }

        /// <summary>居中且带字距的标题（Terraria 标题栏的 letter-spacing）。</summary>
        public static void TextCenteredSpaced(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 color,
                                              string text, float spacing, float outline = BodyOutline)
        {
            if (string.IsNullOrEmpty(text)) return;

            var letters = text.Length;
            var size = ImGuiApi.CalcTextSize(text);
            var total = size.X + spacing * (letters - 1);
            var x = min.X + (max.X - min.X - total) * 0.5f;
            var y = min.Y + (max.Y - min.Y - size.Y) * 0.5f;
            for (var i = 0; i < letters; i++)
            {
                var glyph = text[i].ToString();
                TextOutlined(dl, new Vector2(x, y), color, glyph, outline);
                x += ImGuiApi.CalcTextSize(glyph).X + spacing;
            }
        }

        public static void TextRight(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 color,
                                     string text, float outline = BodyOutline)
        {
            if (string.IsNullOrEmpty(text)) return;
            var size = ImGuiApi.CalcTextSize(text);
            TextOutlined(dl, new Vector2(
                max.X - size.X,
                min.Y + (max.Y - min.Y - size.Y) * 0.5f), color, text, outline);
        }

        /// <summary>窗口外围的柔和投影（画在背景 draw list 上）。</summary>
        public static void DropShadow(ImDrawListPtr dl, Vector2 min, Vector2 max)
        {
            for (var i = 4; i >= 1; i--)
            {
                var spread = Px(i * 3f);
                var alpha = 0.06f + (4 - i) * 0.02f;
                Fill(dl, new Vector2(min.X - spread, min.Y - spread + Px(6f)),
                    new Vector2(max.X + spread, max.Y + spread + Px(8f)), new Vector4(0f, 0f, 0f, alpha));
            }
        }
    }
}
