namespace Pylon.ImGui;

public enum ImGuiCol
{
    Text = 0,
    TextDisabled,
    WindowBg,
    ChildBg,
    PopupBg,
    Border,
    BorderShadow,
    FrameBg,
    FrameBgHovered,
    FrameBgActive,
    TitleBg,
    TitleBgActive,
    TitleBgCollapsed,
    MenuBarBg,
    ScrollbarBg,
    ScrollbarGrab,
    ScrollbarGrabHovered,
    ScrollbarGrabActive,
    CheckMark,
    SliderGrab,
    SliderGrabActive,
    Button,
    ButtonHovered,
    ButtonActive,
    Header,
    HeaderHovered,
    HeaderActive,
    Separator,
    SeparatorHovered,
    SeparatorActive,
    ResizeGrip,
    ResizeGripHovered,
    ResizeGripActive,
    InputTextCursor,
    TabHovered,
    Tab,
    TabSelected,
    TabSelectedOverline,
    TabDimmed,
    TabDimmedSelected,
    TabDimmedSelectedOverline,
    PlotLines,
    PlotLinesHovered,
    PlotHistogram,
    PlotHistogramHovered,
    TableHeaderBg,
    TableBorderStrong,
    TableBorderLight,
    TableRowBg,
    TableRowBgAlt,
    TextLink,
    TextSelectedBg,
    TreeLines,
    DragDropTarget,
    NavCursor,
    NavWindowingHighlight,
    NavWindowingDimBg,
    ModalWindowDimBg,
    Count,
}

/// <summary>样式变量索引（imgui.h enum ImGuiStyleVar_，顺序即数值）。</summary>
public enum ImGuiStyleVar
{
    Alpha = 0,
    DisabledAlpha,
    WindowPadding,          // Vec2
    WindowRounding,         // float
    WindowBorderSize,       // float
    WindowMinSize,          // Vec2
    WindowTitleAlign,       // Vec2
    ChildRounding,          // float
    ChildBorderSize,        // float
    PopupRounding,          // float
    PopupBorderSize,        // float
    FramePadding,           // Vec2
    FrameRounding,          // float
    FrameBorderSize,        // float
    ItemSpacing,            // Vec2
    ItemInnerSpacing,       // Vec2
    IndentSpacing,          // float
    CellPadding,            // Vec2
    ScrollbarSize,          // float
    ScrollbarRounding,      // float
    GrabMinSize,            // float
    GrabRounding,           // float
    ImageBorderSize,        // float
    TabRounding,            // float
    TabBorderSize,          // float
    TabMinWidthBase,        // float
    TabMinWidthShrink,      // float
    TabBarBorderSize,       // float
    TabBarOverlineSize,     // float
    TableAngledHeadersAngle,        // float
    TableAngledHeadersTextAlign,    // Vec2
    TreeLinesSize,          // float
    TreeLinesRounding,      // float
    ButtonTextAlign,        // Vec2
    SelectableTextAlign,    // Vec2
    SeparatorTextBorderSize,// float
    SeparatorTextAlign,     // Vec2
    SeparatorTextPadding,   // Vec2
    Count,
}

/// <summary>窗口标志（imgui.h enum ImGuiWindowFlags_ 常用值）。</summary>
public enum ImGuiWindowFlags
{
    None = 0,
    NoTitleBar = 1 << 0,
    NoResize = 1 << 1,
    NoMove = 1 << 2,
    NoScrollbar = 1 << 3,
    NoScrollWithMouse = 1 << 4,
    NoCollapse = 1 << 5,
    AlwaysAutoResize = 1 << 6,
    NoBackground = 1 << 7,
    NoSavedSettings = 1 << 8,
    NoMouseInputs = 1 << 9,
    NoFocusOnAppearing = 1 << 14,
    NoNav = 1 << 15,
    NoDecoration = NoTitleBar | NoResize | NoScrollbar | NoCollapse,
    MenuBar = 1 << 16,
}

/// <summary>Tab 项标志（ImGuiTabItemFlags_）：关闭按钮（可关标签）。</summary>
public enum ImGuiTabItemFlags
{
    None = 0,
    UnsavedDocument = 1 << 0,
    SetSelected = 1 << 1,
    NoCloseWithMiddleMouseButton = 1 << 2,
    NoPushId = 1 << 3,
    NoTooltip = 1 << 4,
    NoReorder = 1 << 5,
    Leading = 1 << 6,
    Trailing = 1 << 7,
    NoAssumedClosure = 1 << 8,
}

/// <summary>树节点标志常用值。</summary>
public enum ImGuiTreeNodeFlags
{
    None = 0,
    Selected = 1 << 0,
    Framed = 1 << 1,
    AllowItemOverlap = 1 << 2,
    NoTreePushOnOpen = 1 << 3,
    NoAutoOpenOnLog = 1 << 4,
    DefaultOpen = 1 << 5,
    OpenOnDoubleClick = 1 << 6,
    OpenOnArrow = 1 << 7,
    Leaf = 1 << 8,
    Bullet = 1 << 9,
    FramePadding = 1 << 10,
    SpanAvailWidth = 1 << 11,
    SpanFullWidth = 1 << 12,
    NavLeftJumpsBackHere = 1 << 13,
    CollapsingHeader = Framed | NoTreePushOnOpen | NoAutoOpenOnLog,
}