#pragma once

// ============================================================================
// ModernWindow.h —— 统一现代窗口 Chrome（头部 + 侧边栏 + 内容区 + 模式切换）
//
// 尺寸体系（关键）：
//   - 几何尺寸用「设计单位」：u = 窗口高度 / 700（窗口 1000x700 时 u=1），
//     即侧栏 198u、头部 84u …… 与 WEB 预览的设计比例完全一致，且不受
//     UI_Scale（字号）影响，任何缩放下布局比例都不变。
//   - 文字尺寸用 fs 的相对倍数（≥0.7fs）：与 ImGui 控件文字同源，
//     不会出现"文字比控件小一半"的错位。
//   - 卡片/窗口对比：窗口底更深、卡片更亮 + 更明显描边。
//
// 扩展方式：新窗口 = 复用 DrawWindow + BeginContent/EndContent；
//           新工具 = 在 Tools.cpp 注册一行即可，菜单页由 MenuPages 自动生成
//           （无需改 MainWindow 导航 / MenuPages 卡片）。
// ============================================================================

#include "../imgui.h"
#include "../imgui_internal.h"
#include "GlassKit.h"
#include "ModernWidgets.h"

#include <cmath>
#include <cfloat>
#include <cstdio>
#include <cstring>
#include <vector>

namespace ModernUI {

// 侧栏模式：主菜单（进入世界后） / 游戏外（未进入世界，控制台 + XD）
enum class Mode : int { Menu = 0, Game = 1 };

// 侧栏导航项。新增页面：往数组里 push_back 一条即可。
struct NavItem {
    const char* key;   // 稳定标识（页面分发用）
    const char* label; // 侧栏显示名
    int icon;          // Glass::SideIcon kind
};

// 持久状态：每个窗口持有自己的实例
struct State {
    bool open = true;            // false = 折叠为悬浮球（调用方负责画球）
    bool openedOnce = false;
    bool smallSide = false;      // 侧栏折叠为图标条
    float sideAnim = 0.0f;
    float fps = 60.0f;
    bool dragging = false;
    ImVec2 pressPos{}, grabOffset{};
    int manualMode = -1;         // -1 自动跟随 Main.gameMenu；0 主菜单；1 游戏外
    bool lastGameMenu = false;
    Mode mode = Mode::Menu;
    int active[2] = {0, 0};
    float tabAnim = 1.0f;
    float slideDir = 0.0f;
    Glass::SpringState sideX, sideW;
    Glass::BeamRain beams;
    Glass::ParticleBurst burst;
    Glass::FloatParticles floats;
    Glass::TwinkleStars twinkles;
    Glass::AuroraWave aurora;
    float navScroll[2] = {0, 0};
    std::vector<float> hoverAnim;
    ImVec2 cMin{}, cSize{};
};

struct Config {
    const char* title;
    const char* subtitle;
    const char* gameTitle;
    const char* gameSubtitle;
    bool gameMenu;
    const NavItem* menuItems; int menuCount;
    const NavItem* gameItems; int gameCount;
};

struct Result {
    bool open = true;
    Mode mode = Mode::Menu;
    bool modeChanged = false;
    int navClicked = -1;
    bool navChanged = false;
};

// ---------- 当前 Chrome 的 UI 缩放（供弹窗等独立窗口按同比例缩放） ----------
inline float& ScaleRef() { static float s = 1.0f; return s; }
inline float  UiScale()  { return ScaleRef(); }
// 当前 Chrome 的窗口实际圆角半径（特效跳角 / 极光避角用）
inline float& CornerRef() { static float r = 0.0f; return r; }
// 当前 Chrome 的正文字号（由全局字号推导）。实现放在 ModernWidgets.h，
// 这样 Toast / 悬浮球等"在 Chrome 之外绘制"的元素也能用同一尺度。
inline float ChromeFontSize() { return UI::ChromeFontSizeShared(); }

// ---------- 设计比例 ----------
// k = 当前正文字号 / 20（设计正文 20px）。几何全部按 k 缩放 —— 与窗口尺寸无关，
// 因此任何分辨率下文字都保持可读（HTML/CSS 也是绝对尺寸 + 可读下限）。
inline float U() {
    const float fs = ImGui::GetFontSize();
    return (fs > 1.0f) ? (fs / 20.0f) : 1.0f;
}
inline float SideWidthFull()  { return 172.0f * U(); }   // 品牌标题改为「功能」后可窄一些
inline float SideWidthSmall() { return 62.0f * U(); }
// 头部高度 / 侧栏宽度等全部取 HTML 的绝对像素值（配合 U() 缩放）：
// HTML 层级：.hdr 84px > .side-brand 56px > .nav-item 46px
inline float HeaderH()        { return 84.0f * U(); }
inline float FxPad()          { return 24.0f * U(); }
inline float Gap()            { return 34.0f * U(); }

// ============================================================================
// DrawWindow —— 绘制统一 Chrome（背景特效 + 头部 + 侧栏 + 模式切换）
// ============================================================================
inline Result DrawWindow(State& st, const Config& cfg) {
    Result res;
    ImGuiIO& io = ImGui::GetIO();
    const float dt = io.DeltaTime;

    if (!st.open) { res.open = false; return res; }

    const Glass::Palette p = Glass::DarkPalette();

    // ---- 模式：跟随 Main.gameMenu（true = 游戏外） ----
    const bool gm = cfg.gameMenu != false;
    if (st.lastGameMenu != gm) { st.lastGameMenu = gm; st.manualMode = -1; }
    Mode mode = gm ? Mode::Game : Mode::Menu;
    // 未进入世界（gm = 游戏外）时只允许「控制台」页：忽略手动模式，避免切到主菜单作弊页
    if (!gm) {
        if (st.manualMode == 0) mode = Mode::Menu;
        if (st.manualMode == 1) mode = Mode::Game;
    }
    if (mode == Mode::Game && cfg.gameCount <= 0) mode = Mode::Menu;
    res.mode = mode;
    res.modeChanged = (mode != st.mode);
    st.mode = mode;
    if (res.modeChanged) {
        st.tabAnim = 0.0f;
        st.slideDir = 0.0f;
        st.burst.Emit(ImVec2(io.DisplaySize.x * 0.5f, io.DisplaySize.y * 0.42f), p.accent, 18);
    }

    // FPS
    if (dt > 0.0001f) {
        const float inst = 1.0f / dt;
        st.fps += (inst - st.fps) * (1.0f - expf(-8.0f * dt));
    }

    // ---- 字号与度量统一缩放 ----
    // 必须在 Begin 之前推入：窗口装饰（右下角缩放手柄按 g.FontSize 计算大小）
    // 与内部控件才会用同一尺度，否则手柄会按全局字号画、显得过大。
    const float chromeFont = ChromeFontSize();
    const float gFont = ImGui::GetFontSize();
    const float kPlan = chromeFont / 20.0f;        // = clamp(全局字号/40, 0.85, 1.90)
    {
        const float fontDiv = ImMax(ImGui::GetStyle().FontScaleMain * ImGui::GetStyle().FontScaleDpi, 0.01f);
        ImGui::PushFont(nullptr, chromeFont / fontDiv);
        ScaleRef() = chromeFont / 45.0f;                                 // 供弹窗等按同比例缩放
        // 度量按"目标字号 / 当前字号"同比例缩放（保持控件与文字的比例一致）
        const ImGuiStyle& gs = ImGui::GetStyle();
        const float mk = chromeFont / ImMax(gFont, 1.0f);
        const ImVec2 fp = gs.FramePadding, isp = gs.ItemSpacing;
        const ImVec2 iisp = gs.ItemInnerSpacing, cp = gs.CellPadding;
        const float fr = gs.FrameRounding, cr = gs.ChildRounding, pr = gs.PopupRounding;
        const float grab = gs.GrabMinSize, sb = gs.ScrollbarSize, ind = gs.IndentSpacing;
        ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(fp.x * mk, fp.y * mk));
        ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(isp.x * mk, isp.y * mk));
        ImGui::PushStyleVar(ImGuiStyleVar_ItemInnerSpacing, ImVec2(iisp.x * mk, iisp.y * mk));
        ImGui::PushStyleVar(ImGuiStyleVar_CellPadding, ImVec2(cp.x * mk, cp.y * mk));
        ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, fr * mk);
        ImGui::PushStyleVar(ImGuiStyleVar_ChildRounding, cr * mk);
        ImGui::PushStyleVar(ImGuiStyleVar_PopupRounding, pr * mk);
        ImGui::PushStyleVar(ImGuiStyleVar_GrabMinSize, grab * mk);
        ImGui::PushStyleVar(ImGuiStyleVar_ScrollbarSize, sb * mk);
        ImGui::PushStyleVar(ImGuiStyleVar_IndentSpacing, ind * mk);
    }

    // ---- 窗口框架 ----
    const float winW = ImMin(1000.0f, io.DisplaySize.x * 0.96f);
    const float winH = ImMin(700.0f, io.DisplaySize.y * 0.92f);
    ImGui::SetNextWindowPos(ImVec2((io.DisplaySize.x - winW) * 0.5f, (io.DisplaySize.y - winH) * 0.5f),
                            ImGuiCond_FirstUseEver);
    ImGui::SetNextWindowSize(ImVec2(winW, winH), ImGuiCond_FirstUseEver);
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(0, 0));
    // 窗口底更深（与卡片拉开对比）
    ImGui::PushStyleColor(ImGuiCol_WindowBg, ImVec4(0.043f, 0.043f, 0.063f, 0.97f));   // #0B0B10
    ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, ImGui::GetStyle().WindowRounding * kPlan * 0.44f);
    const bool winOpen = ImGui::Begin("##ModernChrome", nullptr,
        ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_NoTitleBar |
        ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoCollapse |
        ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
    ImGui::PopStyleVar();    // WindowRounding
    ImGui::PopStyleColor();  // WindowBg
    ImGui::PopStyleVar();    // WindowPadding
    if (!winOpen) {
        // Begin 为 false 时也要把先前推入的度量/字号弹干净
        ImGui::PopStyleVar(10);
        ImGui::PopFont();
        ImGui::End();
        res.open = false;
        return res;
    }

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const ImVec2 wMin = ImGui::GetWindowPos();
    const ImVec2 wMax(wMin.x + ImGui::GetWindowWidth(), wMin.y + ImGui::GetWindowHeight());
    UI::ChromeMinRef() = wMin;      // 供 Toast 定位到"窗口内顶部"（HTML .toasts{top:96px}）
    UI::ChromeMaxRef() = wMax;
    const float fs = ImGui::GetFontSize();
    const float u  = U();
    const float fxPad = FxPad();
    const ImVec2 fxMin(wMin.x + fxPad, wMin.y + fxPad);
    const ImVec2 fxMax(wMax.x - fxPad, wMax.y - fxPad);

    // ---- 背景特效层：铺满整窗（含最左/最右边缘），圆角由"跳角/避角"处理 ----
    // 之前特效按 FxPad 内缩 → 窗口四周那一圈没有动画（就是"两侧的竖条没效果"）。
    const float winRad = ImGui::GetStyle().WindowRounding * kPlan * 0.44f;  // 窗口实际圆角
    CornerRef() = winRad;
    Glass::DrawDotGrid(dl, wMin, wMax, p.dotGrid, 22.0f * u, winRad);
    st.aurora.Update(dt);
    st.aurora.Draw(dl, wMin, wMax, p, 1.0f, winRad);
    st.beams.Update(dt);   st.beams.Draw(dl, wMin, wMax, p.accent);
    st.twinkles.Update(dt); st.twinkles.Draw(dl, wMin, wMax);
    st.floats.Update(dt);  st.floats.Draw(dl, wMin, wMax);
    if (!st.openedOnce) {
        st.openedOnce = true;
        st.burst.Emit(ImVec2((fxMin.x + fxMax.x) * 0.5f, fxMin.y + 60.0f * u), p.accent, 36);
    }
    st.burst.Update(dt);

    // ==================== 头部 ====================
    // 贴着窗口顶部（HTML 里 .hdr 就是窗口第一个元素、上方无留白），
    // 内容在 headerH 内垂直居中；左右仍用 FxPad 内边距。
    const float headerH = HeaderH();
    const ImVec2 hMin(fxMin.x, wMin.y);
    const ImVec2 hMax(fxMax.x, wMin.y + headerH);
    const float hMidY = (hMin.y + hMax.y) * 0.5f;

    const float cbR = 19.0f * u;                 // 关闭按钮半径（HTML .xbtn 38px）
    ImGui::SetCursorScreenPos(hMin);
    ImGui::InvisibleButton("##hdrDrag", ImVec2(hMax.x - hMin.x - cbR * 2.0f - 44.0f * u, hMax.y - hMin.y));
    const bool hdrHeld = ImGui::IsItemActive();
    if (ImGui::IsItemActivated()) {
        st.pressPos = io.MousePos;
        st.grabOffset = ImGui::GetWindowPos() - io.MousePos;
        st.dragging = false;
    }
    if (hdrHeld && !st.dragging) {
        const float dx = io.MousePos.x - st.pressPos.x;
        const float dy = io.MousePos.y - st.pressPos.y;
        if (dx * dx + dy * dy > 14.0f * 14.0f) st.dragging = true;
    }
    if (st.dragging && hdrHeld) ImGui::SetWindowPos(io.MousePos + st.grabOffset);
    if (ImGui::IsItemDeactivated()) st.dragging = false;

    // logo（HTML .hdr .logo：46px 圆形，左右 padding 22px、与标题间距 16px）
    const float lr = 23.0f * u;
    const ImVec2 lc(hMin.x + 45.0f * u, hMidY);
    dl->AddCircleFilled(lc, lr, p.accentSoft, 40);
    dl->AddCircleFilled(lc, lr * 0.78f, p.accent, 40);
    dl->AddCircle(lc, lr, p.accent, 40, 1.8f);
    Glass::DrawSpark(dl, lc, lr * 0.46f, IM_COL32_WHITE);

    // 标题（按模式）：字号按 HTML 的比例（正文 13px → 标题 21px、副标题 12px）
    const char* title = (mode == Mode::Menu) ? cfg.title : cfg.gameTitle;
    const char* subtitle = (mode == Mode::Menu) ? cfg.subtitle : cfg.gameSubtitle;
    const float titleX = hMin.x + 84.0f * u;
    const float tFont = fs * 0.95f;
    const float sFont = fs * 0.55f;
    const ImVec2 tsz = ImGui::GetFont()->CalcTextSizeA(tFont, FLT_MAX, 0.0f, title);
    const ImVec2 ssz = ImGui::GetFont()->CalcTextSizeA(sFont, FLT_MAX, 0.0f, subtitle);
    const float tTotal = tsz.y + ssz.y + 3.0f * u;
    const float tTop = hMidY - tTotal * 0.5f;
    dl->AddText(ImGui::GetFont(), tFont, ImVec2(titleX, tTop), p.text, title);
    dl->AddText(ImGui::GetFont(), sFont,
                ImVec2(titleX + 2.0f * u, tTop + tsz.y + 3.0f * u), p.textDim, subtitle);

    // 关闭按钮（折叠为悬浮球）
    const float cbCX = hMax.x - 26.0f * u;
    const ImVec2 cbC(cbCX, hMidY);
    ImGui::SetCursorScreenPos(ImVec2(cbC.x - cbR, cbC.y - cbR));
    ImGui::InvisibleButton("##hdrClose", ImVec2(cbR * 2.0f, cbR * 2.0f));
    const bool cbHover = ImGui::IsItemHovered();
    if (ImGui::IsItemClicked()) st.open = false;
    dl->AddCircleFilled(cbC, cbR, cbHover ? p.danger : p.cardBorder, 28);
    dl->AddCircle(cbC, cbR, cbHover ? p.danger : p.cardBorder, 28, 1.6f);
    {
        const ImU32 xCol = cbHover ? IM_COL32_WHITE : p.textDim;
        const float xl = cbR * 0.34f;
        dl->AddLine(ImVec2(cbC.x - xl, cbC.y - xl), ImVec2(cbC.x + xl, cbC.y + xl), xCol, 2.0f);
        dl->AddLine(ImVec2(cbC.x + xl, cbC.y - xl), ImVec2(cbC.x - xl, cbC.y + xl), xCol, 2.0f);
    }

    // FPS 徽标
    char fpsBuf[16];
    snprintf(fpsBuf, sizeof(fpsBuf), "%.0f FPS", st.fps);
    const float bFont = fs * 0.85f;
    const ImVec2 bs = ImGui::GetFont()->CalcTextSizeA(bFont, FLT_MAX, 0.0f, fpsBuf);
    const float dotR = 4.5f * u, gapDot = 8.0f * u, padX = 14.0f * u, padY = 6.0f * u;
    const float badgeW = padX + dotR * 2.0f + gapDot + bs.x + padX;
    const float badgeH = bs.y + padY * 2.0f;
    const ImVec2 bMax(cbCX - cbR - 12.0f * u, hMidY + badgeH * 0.5f);
    const ImVec2 bMin(bMax.x - badgeW, bMax.y - badgeH);
    dl->AddRectFilled(bMin, bMax, p.accentSoft, badgeH * 0.5f);
    dl->AddRect(bMin, bMax, p.cardBorder, badgeH * 0.5f, 0, 1.0f);
    dl->AddCircleFilled(ImVec2(bMin.x + padX + dotR, (bMin.y + bMax.y) * 0.5f), dotR, p.success);
    dl->AddText(ImGui::GetFont(), bFont,
                ImVec2(bMin.x + padX + dotR * 2.0f + gapDot, (bMin.y + bMax.y) * 0.5f - bs.y * 0.5f),
                p.text, fpsBuf);

    // 头部渐变分隔线：移到侧栏之后绘制（见下方），这里不再画

    // ==================== 侧边栏 ====================
    const int itemCount = (mode == Mode::Menu) ? cfg.menuCount : cfg.gameCount;
    const NavItem* items = (mode == Mode::Menu) ? cfg.menuItems : cfg.gameItems;
    const int modeIdx = (int)mode;
    int& active = st.active[modeIdx];
    if (active < 0 || active >= itemCount) active = 0;
    if ((int)st.hoverAnim.size() != itemCount) st.hoverAnim.assign(itemCount, 0.0f);

    // 侧栏宽度：设计值，但保证能放下最长的标签（任何字号下都够宽）
    const float labelFont = fs * 0.95f;
    float maxLabelW = 0.0f;
    for (int i = 0; i < itemCount; ++i) {
        const float w = ImGui::GetFont()->CalcTextSizeA(labelFont, FLT_MAX, 0.0f, items[i].label).x;
        if (w > maxLabelW) maxLabelW = w;
    }
    const float sideFullDesign = SideWidthFull();
    const float sideFullNeed = 20.0f * u + 19.0f * u + 12.0f * u + maxLabelW + 24.0f * u;
    float sideFull = ImMax(sideFullDesign, sideFullNeed);
    const float sideMax = ImGui::GetWindowWidth() * 0.26f;   // 侧栏最多占窗口 26%（预览约 20%）
    if (sideFull > sideMax) sideFull = sideMax;
    const float sideSmall = SideWidthSmall();

    st.sideAnim = Glass::Approach(st.sideAnim, st.smallSide ? 1.0f : 0.0f, dt, 12.0f);
    const float sideW = sideFull + (sideSmall - sideFull) * st.sideAnim;

    // 侧栏紧贴窗口最左侧（HTML 里 .sidebar 就是贴着窗口左缘的通栏）；
    // 向左多画 2px 以吃掉圆角/AA 造成的 1px 视觉缝隙（会被窗口裁剪，不会溢出）。
    // 顶部紧贴头部下方的分割线；底部延伸到窗口最底，底角圆角贴合窗口圆角。
    const ImVec2 sbMin(wMin.x - 2.0f, hMax.y);
    const ImVec2 sbMax(sbMin.x + sideW, wMax.y);
    dl->AddRectFilled(sbMin, sbMax, IM_COL32(19, 19, 27, 170), 14.0f * u, ImDrawFlags_RoundCornersBottom);
    dl->AddRect(sbMin, sbMax, p.cardBorder, 14.0f * u, ImDrawFlags_RoundCornersBottom, 1.0f);

    // 头部渐变分隔线：贯穿整个窗口宽度，且在侧栏之后绘制
    // （否则会被侧栏的半透明底盖住，看起来"侧栏顶部与分割线之间有缝"）
    {
        const ImVec2 sepA(wMin.x - 2.0f, hMax.y - 0.6f);
        const ImVec2 sepB(wMax.x + 2.0f, hMax.y + 1.4f);
        dl->AddRectFilledMultiColor(sepA, sepB,
            IM_COL32(139, 124, 255, 40), IM_COL32(139, 124, 255, 150),
            IM_COL32(139, 124, 255, 150), IM_COL32(139, 124, 255, 40));
    }

    // 品牌区（对齐 HTML .side-brand：padding 14px 12px 10px、gap 10px → 总高 56px）
    const float brandH = 56.0f * u;
    const ImVec2 brMin(sbMin.x, sbMin.y), brMax(sbMax.x, sbMin.y + brandH);
    const float mlr = 16.0f * u;                                  // 图标半宽（32u 见方）
    const bool brandVisible = (sideW > sideFull * 0.6f);          // 展开状态
    // 图标位置：展开时靠左（HTML padding-left 12u），收起时在窄栏里居中；随动画平滑过渡
    const float iconXExp = brMin.x + 12.0f * u + mlr;
    const float iconXSm  = (sbMin.x + sbMax.x) * 0.5f;
    const ImVec2 mlc(iconXExp + (iconXSm - iconXExp) * st.sideAnim, brMin.y + 14.0f * u + mlr);

    // ◆ 品牌图标：始终绘制 —— 收起时它就是"展开"的入口
    dl->AddRectFilled(ImVec2(mlc.x - mlr, mlc.y - mlr), ImVec2(mlc.x + mlr, mlc.y + mlr),
                      p.accent, 10.0f * u);
    {
        const float dh = mlr * 0.80f, dw = mlr * 0.64f;
        dl->AddQuadFilled(ImVec2(mlc.x, mlc.y - dh), ImVec2(mlc.x + dw, mlc.y),
                          ImVec2(mlc.x, mlc.y + dh), ImVec2(mlc.x - dw, mlc.y), IM_COL32_WHITE);
    }
    if (brandVisible) {        // 文字：与图标垂直居中；标题逐字符绘制以加上字距（HTML letter-spacing:1.2px），
        // 并在放不下时自动缩小字号，避免与右侧折叠按钮重叠。
        float bTitleFont = fs * 0.90f;
        float track      = 1.2f * u;
        const float ccBtnLeft = sbMax.x - 12.0f * u - 24.0f * u;   // 折叠按钮左边界
        const float textX     = mlc.x + mlr + 10.0f * u;
        const float avail     = ImMax(ccBtnLeft - 6.0f * u - textX, 20.0f * u);
        // UTF-8 逐字符提取（标题是中文，不能按字节遍历）
        auto nextUtf8 = [](const char* p, char* out) -> int {
            const unsigned char ch = (unsigned char)*p;
            const int len = (ch < 0x80) ? 1 : ((ch >> 5) == 0x06 ? 2 : ((ch >> 4) == 0x0E ? 3 : 4));
            int k = 0;
            for (; k < len && p[k] && k < 7; ++k) out[k] = p[k];
            out[k] = 0;
            return k;
        };
        auto trackedW = [&](float font, const char* s, float tr) {
            float w = 0.0f;
            char one[8];
            for (const char* c = s; *c; ) {
                const int n = nextUtf8(c, one);
                if (n <= 0) break;
                w += ImGui::GetFont()->CalcTextSizeA(font, FLT_MAX, 0.0f, one).x + tr;
                c += n;
            }
            return ImMax(w - tr, 0.0f);
        };
        const float titleW = trackedW(bTitleFont, "功能", track);
        if (titleW > avail) {
            const float shrink = avail / titleW;
            bTitleFont *= shrink;
            track      *= shrink;
        }
        const ImVec2 bts = ImGui::GetFont()->CalcTextSizeA(bTitleFont, FLT_MAX, 0.0f, "功能");
        // 只画标题（版本小字已按要求去掉），与图标垂直居中
        const float bTop = mlc.y - bts.y * 0.5f;
        float tx = textX;
        {
            char one[8];
            for (const char* c = "功能"; *c; ) {
                const int n = nextUtf8(c, one);
                if (n <= 0) break;
                dl->AddText(ImGui::GetFont(), bTitleFont, ImVec2(tx, bTop), p.text, one);
                tx += ImGui::GetFont()->CalcTextSizeA(bTitleFont, FLT_MAX, 0.0f, one).x + track;
                c += n;
            }
        }
    }
    // 展开时：右侧显示折叠按钮（HTML .side-collapse：margin-left:auto、24x24 圆角 8、1px 描边）
    // 收起时：不显示折叠按钮，改为「点击 ◆ 图标展开」
    if (brandVisible) {
        const float ccSz = 24.0f * u;
        const ImVec2 ccC(sbMax.x - 12.0f * u - ccSz * 0.5f, mlc.y);
        const ImVec2 ccMin(ccC.x - ccSz * 0.5f, ccC.y - ccSz * 0.5f);
        const ImVec2 ccMax(ccC.x + ccSz * 0.5f, ccC.y + ccSz * 0.5f);
        ImGui::SetCursorScreenPos(ccMin);
        ImGui::InvisibleButton("##sideCollapse", ImVec2(ccSz, ccSz));
        const bool ccHover = ImGui::IsItemHovered();
        if (ImGui::IsItemClicked()) st.smallSide = true;
        dl->AddRectFilled(ccMin, ccMax, ccHover ? IM_COL32(139, 124, 255, 46) : IM_COL32(255, 255, 255, 13), 8.0f * u);
        dl->AddRect(ccMin, ccMax, ccHover ? p.accent : p.cardBorder, 8.0f * u, 0, 1.0f);
        const float chev = 3.6f * u;
        const ImU32 ccCol = ccHover ? IM_COL32_WHITE : p.textDim;
        dl->AddLine(ImVec2(ccC.x + chev, ccC.y - chev), ImVec2(ccC.x - chev, ccC.y), ccCol, 1.6f);
        dl->AddLine(ImVec2(ccC.x - chev, ccC.y), ImVec2(ccC.x + chev, ccC.y + chev), ccCol, 1.6f);
    } else {
        // 收起状态：点击品牌图标展开（悬停时描边高亮）
        ImGui::SetCursorScreenPos(ImVec2(mlc.x - mlr, mlc.y - mlr));
        ImGui::InvisibleButton("##sideExpand", ImVec2(mlr * 2.0f, mlr * 2.0f));
        const bool expHover = ImGui::IsItemHovered();
        if (ImGui::IsItemClicked()) st.smallSide = false;
        if (expHover)
            dl->AddRect(ImVec2(mlc.x - mlr, mlc.y - mlr), ImVec2(mlc.x + mlr, mlc.y + mlr),
                        IM_COL32_WHITE, 10.0f * u, 0, 1.6f);
    }

    // 侧栏底部：仅保留「状态行 + 模式切换」（版本与说明文字已移除）。
    // 收起时底部不占高度 —— 把空间全部让给导航图标，避免下方留一大片空白。
    const float lineH = fs * 0.95f;
    const bool  showSwitch = !gm;        // 游戏外不显示模式切换（不允许切到主菜单）
    const float mgH   = 32.0f * u;
    const float padV  = 14.0f * u;
    const float footH = brandVisible
                        ? (padV + lineH + (showSwitch ? 10.0f * u + mgH : 0.0f) + padV)
                        : (padV * 0.5f);
    const ImVec2 ftMin(sbMin.x, sbMax.y - footH), ftMax(sbMax.x, sbMax.y);
    if (brandVisible)
        dl->AddLine(ImVec2(ftMin.x + 10.0f * u, ftMin.y), ImVec2(ftMax.x - 10.0f * u, ftMin.y), p.cardBorder, 1.0f);

    float rowY = ftMin.y + (brandVisible ? padV : 0.0f);
    // 状态行（仅展开时显示；收起时不显示孤零零的绿点）
    if (brandVisible) {
        const float dR = 4.5f * u;
        const ImVec2 dC(ftMin.x + 16.0f * u + dR, rowY + lineH * 0.5f);
        dl->AddCircleFilled(dC, dR, p.success);
        dl->AddText(ImGui::GetFont(), fs * 0.82f,
                    ImVec2(dC.x + 9.0f * u, rowY + lineH * 0.5f - fs * 0.48f),
                    p.textDim, "已连接 · 覆盖层运行中");
        rowY += lineH + 10.0f * u;
    }

    // 模式切换胶囊（主菜单 / 游戏外）—— 仅展开且进入世界后显示
    const float mgX = sbMin.x + 12.0f * u;
    const float mgW = sbMax.x - sbMin.x - 24.0f * u;
    const float mgY = rowY;
    if (showSwitch && brandVisible && mgW > 60.0f * u) {
        dl->AddRectFilled(ImVec2(mgX, mgY), ImVec2(mgX + mgW, mgY + mgH), IM_COL32(255, 255, 255, 14), mgH * 0.5f);
        dl->AddRect(ImVec2(mgX, mgY), ImVec2(mgX + mgW, mgY + mgH), p.cardBorder, mgH * 0.5f, 0, 1.0f);
        const float btnW = mgW * 0.5f;
        const float mFont = fs * 0.86f;
        for (int m = 0; m < 2; ++m) {
            const float bx0 = mgX + m * btnW;
            const bool isOn = (mode == (m == 0 ? Mode::Menu : Mode::Game));
            ImGui::SetCursorScreenPos(ImVec2(bx0, mgY));
            ImGui::InvisibleButton(m == 0 ? "##modeMenu" : "##modeGame", ImVec2(btnW, mgH));
            if (ImGui::IsItemClicked()) st.manualMode = m;
            if (isOn) {
                dl->AddRectFilled(ImVec2(bx0, mgY), ImVec2(bx0 + btnW, mgY + mgH), p.accent, mgH * 0.5f);
                dl->AddRect(ImVec2(bx0, mgY), ImVec2(bx0 + btnW, mgY + mgH), p.accentHover, mgH * 0.5f, 0, 1.0f);
            }
            const char* lb = (m == 0) ? "主菜单" : "控制台";
            const ImVec2 ls = ImGui::GetFont()->CalcTextSizeA(mFont, FLT_MAX, 0.0f, lb);
            dl->AddText(ImGui::GetFont(), mFont,
                        ImVec2(bx0 + btnW * 0.5f - ls.x * 0.5f, mgY + mgH * 0.5f - ls.y * 0.5f),
                        isOn ? IM_COL32_WHITE : p.textDim, lb);
        }
    }
    rowY += mgH + (showSwitch ? 8.0f * u : 0.0f);
    (void)rowY;

    // 导航项（支持滚动；不可见项跳过）
    const float navTop = brMax.y + 10.0f * u;
    const float navBottom = ftMin.y - 6.0f * u;
    const float navH = navBottom - navTop;
    const float itemH = ImMax(46.0f * u, fs * 1.5f);   // HTML .nav-item 46px
    const float itemGap = 3.0f * u;
    const float step = itemH + itemGap;
    const float totalNav = itemCount * step;

    float& scroll = st.navScroll[modeIdx];
    const float maxScroll = totalNav - navH;
    if (ImGui::IsMouseHoveringRect(ImVec2(sbMin.x, navTop), ImVec2(sbMax.x, navBottom), false))
        scroll -= io.MouseWheel * fs * 1.4f;
    if (scroll < 0.0f) scroll = 0.0f;
    if (scroll > maxScroll) scroll = (maxScroll > 0.0f) ? maxScroll : 0.0f;

    const float indX = sbMin.x + 8.0f * u;
    const float indW = sbMax.x - sbMin.x - 16.0f * u;
    const float pillH = itemH - 6.0f * u;
    const float targetY = navTop + active * step - scroll + (itemH - pillH) * 0.5f;
    const float indY = ImClamp(Glass::SpringTick(st.sideX, targetY, dt, 300.0f, 30.0f),
                               navTop - pillH, navBottom);

    // 选中指示器
    ImVec4 indC = Glass::U32ToVec4(p.accent); indC.w = 0.20f;
    dl->AddRectFilled(ImVec2(indX, indY), ImVec2(indX + indW, indY + pillH),
                      ImGui::ColorConvertFloat4ToU32(indC), 11.0f * u);
    dl->AddRect(ImVec2(indX, indY), ImVec2(indX + indW, indY + pillH), p.accent, 11.0f * u, 0, 1.2f);
    dl->AddRectFilled(ImVec2(indX, indY + 8.0f * u), ImVec2(indX + 3.0f * u, indY + pillH - 8.0f * u),
                      p.accent, 1.5f * u);

    // 导航项
    dl->PushClipRect(ImVec2(sbMin.x, navTop), ImVec2(sbMax.x, navBottom), true);
    int clicked = -1;
    for (int i = 0; i < itemCount; ++i) {
        const float y0 = navTop + i * step - scroll;
        if (y0 + itemH < navTop || y0 > navBottom) continue;
        const float y1 = y0 + itemH;
        const bool sel = (i == active);
        ImGui::SetCursorScreenPos(ImVec2(indX, y0));
        ImGui::PushID(i);
        ImGui::InvisibleButton("##nav", ImVec2(indW, itemH));
        if (ImGui::IsItemClicked()) clicked = i;
        const bool hovered = ImGui::IsItemHovered();
        ImGui::PopID();
        st.hoverAnim[i] = Glass::Approach(st.hoverAnim[i], hovered ? 1.0f : 0.0f, dt, 14.0f);
        if (st.hoverAnim[i] > 0.01f) {
            const float barH = itemH * 0.55f * st.hoverAnim[i];
            dl->AddRectFilled(ImVec2(indX, y0 + (itemH - barH) * 0.5f),
                              ImVec2(indX + 3.0f * u, y0 + (itemH + barH) * 0.5f), p.accent, 1.5f * u);
        }
        const float icy = (y0 + y1) * 0.5f;
        const ImU32 icCol = sel ? IM_COL32(185, 175, 255, 255) : (hovered ? p.text : p.textDim);
        Glass::SideIcon(dl, items[i].icon, ImVec2(indX + 20.0f * u, icy), 19.0f * u, icCol, 1.7f * u);
        if (sideW > sideFull * 0.6f) {
            const ImVec2 ts = ImGui::GetFont()->CalcTextSizeA(labelFont, FLT_MAX, 0.0f, items[i].label);
            dl->AddText(ImGui::GetFont(), labelFont,
                        ImVec2(indX + 38.0f * u, icy - ts.y * 0.5f),
                        sel ? p.text : (hovered ? p.text : p.textDim), items[i].label);
        }
    }
    dl->PopClipRect();

    if (clicked >= 0) {
        res.navClicked = clicked;
        if (clicked != active) {
            res.navChanged = true;
            st.tabAnim = 0.0f;
            st.slideDir = (clicked > active) ? 1.0f : -1.0f;
            st.burst.Emit(ImVec2(indX + indW * 0.5f, navTop + clicked * step - scroll + itemH * 0.5f),
                          p.accent, 12);
            active = clicked;
        }
    }

    // 内容区几何
    st.cMin = ImVec2(sbMax.x + Gap(), hMax.y + 16.0f * u);
    st.cSize = ImVec2(fxMax.x - st.cMin.x, (wMax.y - fxPad) - st.cMin.y);
    if (st.cSize.x < 40.0f * u) st.cSize.x = 40.0f * u;
    if (st.cSize.y < 40.0f * u) st.cSize.y = 40.0f * u;

    return res;
}

// 内容区开始（页面切换动画：淡入 + 轻微水平滑动）
inline void BeginContent(State& st) {
    ImGui::SetCursorScreenPos(st.cMin);
    ImGui::BeginChild("##content", st.cSize, false, ImGuiWindowFlags_NoBackground);
    ImGuiIO& io = ImGui::GetIO();
    st.tabAnim += (1.0f - st.tabAnim) * (1.0f - expf(-io.DeltaTime * 12.0f));
    ImGui::PushStyleVar(ImGuiStyleVar_Alpha, st.tabAnim);
    const float slide = (1.0f - st.tabAnim) * 20.0f * st.slideDir;
    ImGui::SetCursorPos(ImVec2(ImGui::GetCursorPosX() + slide, ImGui::GetCursorPosY()));
}

inline void EndContent(State& st) {
    (void)st;
    ImGui::PopStyleVar();
    // 子窗口结束前先提交一个零尺寸 item，撑开父内容边界：
    // BeginContent 里用过 SetCursorPos（页面滑动动画），若本页什么都没画
    // （例如 XD 房列在未进入多人列表时直接 return），结束子窗口时会触发
    // ImGui 的 "SetCursorPos() to extend window boundaries" 断言。
    ImGui::Dummy(ImVec2(0.0f, 0.0f));
    ImGui::EndChild();
}

// 前景特效层：在内容之上再画一层低透明的雨滴/极光/浮点。
// 只画背景层的话会被卡片与侧栏完全挡住；这一层让动画"穿过"面板可见（与 WEB 预览一致）。
// 同样铺满整窗，保证最左/最右边缘也有动画。
inline void DrawForegroundFX(State& st) {
    const Glass::Palette p = Glass::DarkPalette();
    ImDrawList* dl = ImGui::GetWindowDrawList();
    const ImVec2 wMin = ImGui::GetWindowPos();
    const ImVec2 wMax(wMin.x + ImGui::GetWindowWidth(), wMin.y + ImGui::GetWindowHeight());
    // 同样铺满整窗 → 侧栏/卡片之上、最左最右边缘都能看到雨滴与极光
    st.aurora.Draw(dl, wMin, wMax, p, 0.45f, CornerRef());
    st.beams.Draw(dl, wMin, wMax, p.accent, 0.34f);
    st.floats.Draw(dl, wMin, wMax, 0.55f);
}

inline void EndWindow(State& st) {
    st.burst.Draw(ImGui::GetWindowDrawList());
    ImGui::PopStyleVar(10);   // 与 DrawWindow 内的度量缩放（10 项）配对
    ImGui::PopFont();         // 与 DrawWindow 的 PushFont 配对
    ImGui::End();
    UI::DrawBurstFX();
}

// 页面头：标题 + 副标题（内容区内使用）
inline void PageHeader(const char* title, const char* subtitle) {
    const float fs = ImGui::GetFontSize();
    const Glass::Palette p = Glass::DarkPalette();
    ImDrawList* dl = ImGui::GetWindowDrawList();
    const ImVec2 cpos = ImGui::GetCursorScreenPos();
    dl->AddText(ImGui::GetFont(), fs * 1.30f, ImVec2(cpos.x + 2.0f, cpos.y), p.text, title);
    const float subY = cpos.y + fs * 1.45f;
    if (subtitle && *subtitle)
        dl->AddText(ImGui::GetFont(), fs * 0.82f, ImVec2(cpos.x + 3.0f, subY), p.textDim, subtitle);
    ImGui::Dummy(ImVec2(0.0f, fs * 2.45f));
}

} // namespace ModernUI