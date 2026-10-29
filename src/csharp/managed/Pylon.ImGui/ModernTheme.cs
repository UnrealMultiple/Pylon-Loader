namespace Pylon.ImGui;

public static class ModernTheme
{
    public static ImVec4 Hex(uint rgb, float a = 1f)
    {
        return new ImVec4(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            a);
    }

    /// <summary>应用主题（悬浮窗初始化后调用一次；越界索引由原生侧忽略）。</summary>
    public static void Apply()
    {
        // ---- 强调色（蓝紫 #7C74FF） ----
        var accent = Hex(0x7C74FF);
        var accentHover = Hex(0x8F88FF);
        var accentActive = Hex(0x6A5AE0);

        // ---- 基础表面（近黑 0x0A0A0C / 卡片 0x15151B） ----
        var card = Hex(0x15151B);
        var cardHi = Hex(0x17171E);        // 标题栏/激活表面略亮
        var cardBorder = Hex(0xFFFFFF, 0.08f);

        // ---- 文本 ----
        C(ImGuiCol.Text, Hex(0xF4F4F6));
        C(ImGuiCol.TextDisabled, Hex(0xF4F4F6, 0.45f));
        C(ImGuiCol.TextLink, accentHover);
        C(ImGuiCol.TextSelectedBg, Hex(0x7C74FF, 0.40f));

        // ---- 窗口 / 子窗口 / 弹窗 ----
        C(ImGuiCol.WindowBg, card);
        C(ImGuiCol.ChildBg, new ImVec4(0, 0, 0, 0));   // 子窗口透明，露出背景特效
        C(ImGuiCol.PopupBg, Hex(0x1C1C26));
        C(ImGuiCol.Border, Hex(0xFFFFFF, 0.12f));
        C(ImGuiCol.BorderShadow, new ImVec4(0, 0, 0, 0));

        // ---- 标题栏 / 菜单栏 ----
        C(ImGuiCol.TitleBg, card);
        C(ImGuiCol.TitleBgActive, cardHi);
        C(ImGuiCol.TitleBgCollapsed, card);
        C(ImGuiCol.MenuBarBg, card);

        // ---- 输入框 / 复选框 / 滑条底框（淡紫半透明，玻璃质感） ----
        C(ImGuiCol.FrameBg, Hex(0x7C74FF, 0.14f));
        C(ImGuiCol.FrameBgHovered, Hex(0x7C74FF, 0.22f));
        C(ImGuiCol.FrameBgActive, Hex(0x7C74FF, 0.28f));
        C(ImGuiCol.CheckMark, new ImVec4(1, 1, 1, 1));
        C(ImGuiCol.SliderGrab, accent);
        C(ImGuiCol.SliderGrabActive, accentHover);

        // ---- 按钮（暗色底，悬停泛紫） ----
        C(ImGuiCol.Button, Hex(0x20242F));
        C(ImGuiCol.ButtonHovered, Hex(0x2B3250));
        C(ImGuiCol.ButtonActive, Hex(0x353D63));

        // ---- 折叠头 / 树节点 / 可选项 ----
        C(ImGuiCol.Header, Hex(0x7C74FF, 0.18f));
        C(ImGuiCol.HeaderHovered, Hex(0x7C74FF, 0.26f));
        C(ImGuiCol.HeaderActive, Hex(0x7C74FF, 0.32f));

        // ---- 分隔线 ----
        C(ImGuiCol.Separator, cardBorder);
        C(ImGuiCol.SeparatorHovered, Hex(0x8F88FF, 0.50f));
        C(ImGuiCol.SeparatorActive, accent);

        // ---- 滚动条（更窄更亮） ----
        C(ImGuiCol.ScrollbarBg, new ImVec4(0, 0, 0, 0));
        C(ImGuiCol.ScrollbarGrab, Hex(0x8F88FF, 0.32f));
        C(ImGuiCol.ScrollbarGrabHovered, Hex(0x8F88FF, 0.52f));
        C(ImGuiCol.ScrollbarGrabActive, Hex(0xA99CFF, 0.68f));

        // ---- 标签页（透明底 + 选中淡紫 + 上沿强调条） ----
        C(ImGuiCol.Tab, new ImVec4(0, 0, 0, 0));
        C(ImGuiCol.TabHovered, Hex(0x7C74FF, 0.20f));
        C(ImGuiCol.TabSelected, Hex(0x7C74FF, 0.16f));
        C(ImGuiCol.TabSelectedOverline, accent);
        C(ImGuiCol.TabDimmed, new ImVec4(0, 0, 0, 0));
        C(ImGuiCol.TabDimmedSelected, Hex(0x7C74FF, 0.12f));
        C(ImGuiCol.TabDimmedSelectedOverline, Hex(0x7C74FF, 0.55f));

        // ---- 表格（表头淡紫 + 更亮边框 + 行条纹） ----
        C(ImGuiCol.TableHeaderBg, Hex(0x7C74FF, 0.14f));
        C(ImGuiCol.TableBorderStrong, Hex(0x8F88FF, 0.35f));
        C(ImGuiCol.TableBorderLight, Hex(0xFFFFFF, 0.08f));
        C(ImGuiCol.TableRowBg, new ImVec4(0, 0, 0, 0));
        C(ImGuiCol.TableRowBgAlt, Hex(0xFFFFFF, 0.045f));

        // ---- 其它 ----
        C(ImGuiCol.PlotLines, accent);
        C(ImGuiCol.PlotLinesHovered, accentHover);
        C(ImGuiCol.PlotHistogram, accent);
        C(ImGuiCol.PlotHistogramHovered, accentHover);
        C(ImGuiCol.ResizeGrip, Hex(0x7C74FF, 0.25f));
        C(ImGuiCol.ResizeGripHovered, accent);
        C(ImGuiCol.ResizeGripActive, accentHover);
        C(ImGuiCol.DragDropTarget, Hex(0x7C74FF, 0.90f));
        C(ImGuiCol.NavCursor, accent);
        C(ImGuiCol.ModalWindowDimBg, Hex(0x000000, 0.55f));

        // ---- 度量 / 圆角 / 间距（大圆角 + 宽松间距） ----
        V(ImGuiStyleVar.WindowPadding, 20f, 16f);
        V(ImGuiStyleVar.WindowRounding, 24f);
        V(ImGuiStyleVar.WindowBorderSize, 0f);
        V(ImGuiStyleVar.WindowTitleAlign, 0.5f, 0.5f);
        V(ImGuiStyleVar.ChildRounding, 16f);
        V(ImGuiStyleVar.ChildBorderSize, 0f);
        V(ImGuiStyleVar.PopupRounding, 14f);
        V(ImGuiStyleVar.PopupBorderSize, 0f);
        V(ImGuiStyleVar.FramePadding, 14f, 10f);
        V(ImGuiStyleVar.FrameRounding, 12f);
        V(ImGuiStyleVar.FrameBorderSize, 1f);
        V(ImGuiStyleVar.ItemSpacing, 12f, 12f);
        V(ImGuiStyleVar.ItemInnerSpacing, 8f, 6f);
        V(ImGuiStyleVar.CellPadding, 10f, 6f);
        V(ImGuiStyleVar.IndentSpacing, 26f);
        V(ImGuiStyleVar.ScrollbarSize, 6f);
        V(ImGuiStyleVar.ScrollbarRounding, 6f);
        V(ImGuiStyleVar.GrabMinSize, 18f);
        V(ImGuiStyleVar.GrabRounding, 12f);
        V(ImGuiStyleVar.TabRounding, 10f);
        V(ImGuiStyleVar.TabBorderSize, 0f);
        V(ImGuiStyleVar.TabBarBorderSize, 0f);
        V(ImGuiStyleVar.TabBarOverlineSize, 3f);
        V(ImGuiStyleVar.SeparatorTextBorderSize, 2f);
        V(ImGuiStyleVar.ButtonTextAlign, 0.5f, 0.5f);
    }

    private static void C(ImGuiCol col, ImVec4 v)
        => ImGuiNative.igSetStyleColor((int)col, v.X, v.Y, v.Z, v.W);

    private static void V(ImGuiStyleVar v, float f)
        => ImGuiNative.igSetStyleVarFloat((int)v, f);

    private static void V(ImGuiStyleVar v, float x, float y)
        => ImGuiNative.igSetStyleVarVec2((int)v, x, y);
}