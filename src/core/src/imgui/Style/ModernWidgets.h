#pragma once

// ============================================================================
// ModernWidgets.h
// 带动画、圆角的现代控件封装（UI:: 命名空间）。
//  - Button / SmallButton / AccentButton：悬停过渡 + 按压缩放 + 强调色描边光晕
//  - Checkbox：圆角勾选框，勾选时平滑填充强调色并弹出对勾
//  - CollapsingHeader：旋转箭头 + 左侧强调条 + 悬停强调色
//  - Combo：旋转箭头 + 悬停/展开光晕
//  - Slider* / Input*：悬停平滑过渡
// 所有动画基于帧时间 DeltaTime 做指数平滑，帧率无关；状态按 ImGuiID 缓存，
// 规模过大时自动清理已静止的条目。配色与 ModernTheme.h 保持一致：
// 强调色取 ImGuiCol_SliderGrab；所有颜色经 GetColorU32 输出以正确响应全局 Alpha
// （BeginDisabled 的淡出、窗口透明度等）。
// ============================================================================

#include "../imgui.h"
#include "../backends/InputMethod/ImGuiAndroidIme.h"
#include "GlassKit.h"
#include <algorithm>
#include <cmath>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace UI {

// ---- 颜色 / 缓动小工具 ----
static inline ImVec4 Lerp(const ImVec4& a, const ImVec4& b, float t) {
    return ImVec4(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t,
                  a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
}

static inline float Step01(float cur, float target, float speed, float dt) {
    if (dt <= 0.0f) return target;
    return cur + (target - cur) * (1.0f - std::exp(-speed * dt));
}

// 输出 ImU32 时自动乘以全局 style.Alpha（禁用淡出 / 窗口透明度）
static inline ImU32 Col(const ImVec4& c) { return ImGui::GetColorU32(c); }

// 复刻 ImGui 内部 FindRenderedTextEnd：遇到 "##" 即停止显示（仅用做 ID 后缀）
static inline const char* RenderedTextEnd(const char* text) {
    const char* p = text;
    while (*p) {
        if (p[0] == '#' && p[1] == '#')
            break;
        ++p;
    }
    return p;
}

// ---- 每控件动画状态 ----
struct AnimState {
    float hover = 0.0f;
    float press = 0.0f;
    float check = 0.0f;
    float openAnim = 0.0f;   // 展开/打开状态动画（折叠头/下拉框等）
    float focus = 0.0f;      // 输入框焦点辉光动画
};

inline std::unordered_map<ImGuiID, AnimState>& AnimMap() {
    static std::unordered_map<ImGuiID, AnimState> map;
    return map;
}

inline AnimState& GetAnim(ImGuiID id) {
    auto& map = AnimMap();
    if (map.size() > 4096) {  // 清理已静止的条目，避免长期运行无限增长
        for (auto it = map.begin(); it != map.end();) {
            const AnimState& a = it->second;
            if (a.hover < 0.01f && a.press < 0.01f && a.check < 0.01f)
                it = map.erase(it);
            else
                ++it;
        }
    }
    return map[id];
}

inline ImVec4 Accent()      { return ImGui::GetStyle().Colors[ImGuiCol_SliderGrab]; }
inline ImVec4 AccentHover() { return ImGui::GetStyle().Colors[ImGuiCol_SliderGrabActive]; }

// ============================================================================
// 点击粒子（仿UI “Burst here!”）：UI::Button / UI::AccentButton 点击时在按钮中心
// 爆一小撮强调色粒子。由 MainWindow 每帧调用 UpdateBurstFX / DrawBurstFX 驱动，
// 粒子画在前景层（盖在所有窗口之上）。
// ============================================================================
inline Glass::ParticleBurst& BurstFX() {
    static Glass::ParticleBurst b;
    return b;
}
inline void UpdateBurstFX() { BurstFX().Update(ImGui::GetIO().DeltaTime); }
inline void DrawBurstFX() {
    if (BurstFX().Active())
        BurstFX().Draw(ImGui::GetForegroundDrawList());
}

// 绕圆心旋转一个点
static inline ImVec2 RotateAround(const ImVec2& p, const ImVec2& c, float rad) {
    const float s = std::sin(rad), co = std::cos(rad);
    const float dx = p.x - c.x, dy = p.y - c.y;
    return ImVec2(c.x + dx * co - dy * s, c.y + dx * s + dy * co);
}

// 计算按钮尺寸：size 传 0 时按文字自动
static inline ImVec2 ButtonSize(const char* label, const ImVec2& size_arg) {
    const ImGuiStyle& style = ImGui::GetStyle();
    const ImVec2 label_size = ImGui::CalcTextSize(label, nullptr, true);
    ImVec2 sz = size_arg;
    if (sz.x <= 0.0f) sz.x = label_size.x + style.FramePadding.x * 2.0f;
    if (sz.y <= 0.0f) sz.y = label_size.y + style.FramePadding.y * 2.0f;
    sz.x = std::max(sz.x, 1.0f);
    sz.y = std::max(sz.y, ImGui::GetFrameHeight());
    return sz;
}

// ============================================================================
// 按钮（仿UI Primary）：实心强调色 + 白字 + 悬停变亮 + 按压缩放 + 顶部高光
// 与 ImGui::Button 兼容：返回是否被点击；size 传 0 时按文字自动尺寸。
// ============================================================================
inline bool Button(const char* label, const ImVec2& size_arg = ImVec2(0, 0)) {
    const ImGuiStyle& style = ImGui::GetStyle();
    const ImVec2 label_size = ImGui::CalcTextSize(label, nullptr, true);
    const ImVec2 sz = ButtonSize(label, size_arg);

    const ImGuiID id = ImGui::GetID(label);
    const bool clicked = ImGui::InvisibleButton(label, sz);
    const bool hovered = ImGui::IsItemHovered();
    const bool held    = ImGui::IsItemActive();

    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();

    AnimState& a = GetAnim(id);
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, hovered ? 1.0f : 0.0f, 14.0f, dt);
    a.press = Step01(a.press, held    ? 1.0f : 0.0f, 22.0f, dt);

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const float rounding = style.FrameRounding;

    const float scale = 1.0f - a.press * 0.05f;
    const ImVec2 c((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
    const ImVec2 half((max.x - min.x) * 0.5f * scale, (max.y - min.y) * 0.5f * scale);
    const ImVec2 p0(c.x - half.x, c.y - half.y);
    const ImVec2 p1(c.x + half.x, c.y + half.y);

    // 实心强调色（仿UI Primary）
    ImVec4 col = Lerp(Accent(), AccentHover(), a.hover);
    col = Lerp(col, ImVec4(col.x * 0.85f, col.y * 0.85f, col.z * 0.85f, 1.0f), a.press);
    dl->AddRectFilled(p0, p1, Col(col), rounding);
    // 顶部高光（玻璃质感）
    dl->AddLine(ImVec2(p0.x + rounding, p0.y + 1.5f), ImVec2(p1.x - rounding, p0.y + 1.5f),
                Col(ImVec4(1.0f, 1.0f, 1.0f, 0.10f + 0.12f * a.hover)), 1.2f);
    // 悬停：白色细描边
    if (a.hover > 0.01f)
        dl->AddRect(p0, p1, Col(ImVec4(1.0f, 1.0f, 1.0f, 0.20f + a.hover * 0.35f)), rounding, 0, 1.2f);

    if (label_size.x > 0.0f) {
        const ImVec2 tp(c.x - label_size.x * 0.5f, c.y - label_size.y * 0.5f);
        dl->AddText(tp, Col(ImVec4(1.0f, 1.0f, 1.0f, 1.0f)), label, RenderedTextEnd(label));
    }
    // 点击粒子（仿UI Burst）
    if (clicked)
        BurstFX().Emit(ImVec2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f),
                       ImGui::ColorConvertFloat4ToU32(Accent()), 14);
    return clicked;
}

inline bool SmallButton(const char* label) {
    return Button(label);
}

// 兼容旧名：实心强调色主按钮（同 Primary）
inline bool AccentButton(const char* label, const ImVec2& size_arg = ImVec2(0, 0)) {
    return Button(label, size_arg);
}

// 幽灵按钮（仿UI Ghost）：透明底 + 强调色字，悬停淡紫底 + 强调色描边
inline bool GhostButton(const char* label, const ImVec2& size_arg = ImVec2(0, 0)) {
    const ImGuiStyle& style = ImGui::GetStyle();
    const ImVec2 label_size = ImGui::CalcTextSize(label, nullptr, true);
    const ImVec2 sz = ButtonSize(label, size_arg);

    const ImGuiID id = ImGui::GetID(label);
    const bool clicked = ImGui::InvisibleButton(label, sz);
    const bool hovered = ImGui::IsItemHovered();
    const bool held    = ImGui::IsItemActive();

    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();

    AnimState& a = GetAnim(id);
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, hovered ? 1.0f : 0.0f, 14.0f, dt);
    a.press = Step01(a.press, held    ? 1.0f : 0.0f, 22.0f, dt);

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const float rounding = style.FrameRounding;
    const float scale = 1.0f - a.press * 0.05f;
    const ImVec2 c((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
    const ImVec2 half((max.x - min.x) * 0.5f * scale, (max.y - min.y) * 0.5f * scale);
    const ImVec2 p0(c.x - half.x, c.y - half.y);
    const ImVec2 p1(c.x + half.x, c.y + half.y);

    const ImVec4 accent = Accent();
    // 悬停/按下：淡紫底
    const float bgA = a.hover * 0.18f + a.press * 0.10f;
    if (bgA > 0.001f)
        dl->AddRectFilled(p0, p1, Col(ImVec4(accent.x, accent.y, accent.z, bgA)), rounding);
    // 描边：悬停时强调色
    dl->AddRect(p0, p1, Col(ImVec4(accent.x, accent.y, accent.z, 0.15f + a.hover * 0.55f)), rounding, 0, 1.4f);

    if (label_size.x > 0.0f) {
        const ImVec2 tp(c.x - label_size.x * 0.5f, c.y - label_size.y * 0.5f);
        dl->AddText(tp, Col(accent), label, RenderedTextEnd(label));
    }
    if (clicked)
        BurstFX().Emit(ImVec2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f),
                       ImGui::ColorConvertFloat4ToU32(accent), 12);
    return clicked;
}

// 危险按钮（仿UI Exit）：淡红底 + 红字，悬停加深
inline bool DangerButton(const char* label, const ImVec2& size_arg = ImVec2(0, 0)) {
    const ImGuiStyle& style = ImGui::GetStyle();
    const ImVec2 label_size = ImGui::CalcTextSize(label, nullptr, true);
    const ImVec2 sz = ButtonSize(label, size_arg);

    const ImGuiID id = ImGui::GetID(label);
    const bool clicked = ImGui::InvisibleButton(label, sz);
    const bool hovered = ImGui::IsItemHovered();
    const bool held    = ImGui::IsItemActive();

    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();

    AnimState& a = GetAnim(id);
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, hovered ? 1.0f : 0.0f, 14.0f, dt);
    a.press = Step01(a.press, held    ? 1.0f : 0.0f, 22.0f, dt);

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const float rounding = style.FrameRounding;
    const float scale = 1.0f - a.press * 0.05f;
    const ImVec2 c((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
    const ImVec2 half((max.x - min.x) * 0.5f * scale, (max.y - min.y) * 0.5f * scale);
    const ImVec2 p0(c.x - half.x, c.y - half.y);
    const ImVec2 p1(c.x + half.x, c.y + half.y);

    const ImVec4 danger(1.0f, 0.36f, 0.36f, 1.0f);
    dl->AddRectFilled(p0, p1, Col(ImVec4(danger.x, danger.y, danger.z, 0.12f + a.hover * 0.10f)), rounding);
    dl->AddRect(p0, p1, Col(ImVec4(danger.x, danger.y, danger.z, 0.35f + a.hover * 0.45f)), rounding, 0, 1.3f);

    if (label_size.x > 0.0f) {
        const ImVec2 tp(c.x - label_size.x * 0.5f, c.y - label_size.y * 0.5f);
        dl->AddText(tp, Col(danger), label, RenderedTextEnd(label));
    }
    if (clicked)
        BurstFX().Emit(ImVec2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f),
                       ImGui::ColorConvertFloat4ToU32(danger), 12);
    return clicked;
}

// ============================================================================
// 圆形勾选框（带动画）：圆底 + 对勾弹出，与文字垂直居中对齐。
// 适合选项列表 / 网格，占位小、可 SameLine 并排。返回是否被点击。
// ============================================================================
inline bool Checkbox(const char* label, bool* v) {
    const ImGuiStyle& style = ImGui::GetStyle();
    const float fs = ImGui::GetFontSize();
    const ImVec2 label_size = ImGui::CalcTextSize(label, RenderedTextEnd(label));
    const float sz = fs * 0.95f;                        // 圆形勾选（加大）
    const float gap = style.ItemInnerSpacing.x;
    const float total_w = sz + (label_size.x > 0.0f ? gap + label_size.x : 0.0f);
    const float total_h = std::max(sz, label_size.y);

    const ImGuiID id = ImGui::GetID(label);
    const bool clicked = ImGui::InvisibleButton(label, ImVec2(total_w, total_h));
    if (clicked) *v = !*v;
    const bool hovered = ImGui::IsItemHovered();

    const ImVec2 min = ImGui::GetItemRectMin();
    // 圆心与文字垂直居中（同一行）
    const float r = sz * 0.5f;
    const ImVec2 c(min.x + r, min.y + (total_h - sz) * 0.5f + r);

    AnimState& a = GetAnim(id);
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, hovered ? 1.0f : 0.0f, 14.0f, dt);
    a.check = Step01(a.check, *v ? 1.0f : 0.0f, 16.0f, dt);

    ImDrawList* dl = ImGui::GetWindowDrawList();

    ImVec4 bg = Lerp(style.Colors[ImGuiCol_FrameBg], style.Colors[ImGuiCol_FrameBgHovered], a.hover);
    bg = Lerp(bg, Accent(), a.check);
    dl->AddCircleFilled(c, r, Col(bg), 32);
    const ImU32 border_col = a.check > 0.5f
        ? Col(ImVec4(Accent().x, Accent().y, Accent().z, 0.9f))
        : Col(style.Colors[ImGuiCol_Border]);
    dl->AddCircle(c, r, border_col, 32, 1.4f);

    // 对勾（动画弹出，居中于圆）
    if (a.check > 0.01f) {
        const float t = a.check;
        const float s = 0.7f + 0.3f * t;
        const float w = sz * 0.5f * s;
        const ImVec2 q0(c.x - w * 0.55f, c.y + w * 0.02f);
        const ImVec2 q1(c.x - w * 0.10f, c.y + w * 0.50f);
        const ImVec2 q2(c.x + w * 0.62f, c.y - w * 0.48f);
        const ImU32 tick = Col(ImVec4(1.0f, 1.0f, 1.0f, t));
        const float thick = std::max(2.0f, sz * 0.09f);
        dl->AddLine(q0, q1, tick, thick);
        dl->AddLine(q1, q2, tick, thick);
    }

    if (label_size.x > 0.0f) {
        const ImVec2 lp(min.x + sz + gap, min.y + (total_h - label_size.y) * 0.5f);
        dl->AddText(lp, Col(style.Colors[ImGuiCol_Text]), label, RenderedTextEnd(label));
    }
    return clicked;
}

// ============================================================================
// 大号药丸开关（仿UI RowToggle）：文字在左、开关在右、左右对齐（占满整行）。
// 只用于页面的“主开关”，选项列表请用紧凑的 UI::Checkbox。
// ============================================================================
inline bool Toggle(const char* label, bool* v) {
    const ImGuiStyle& style = ImGui::GetStyle();
    const float fs = ImGui::GetFontSize();
    const ImVec2 label_size = ImGui::CalcTextSize(label, RenderedTextEnd(label));
    const float h = fs * 1.05f, w = h * 1.77f;              // 大号（仿UI 26:46 比例放大）
    const float availW = ImGui::GetContentRegionAvail().x;  // 占满整行，开关靠右
    const float total_w = availW;
    const float total_h = std::max(h, label_size.y);

    const ImGuiID id = ImGui::GetID(label);
    const bool clicked = ImGui::InvisibleButton(label, ImVec2(total_w, total_h));
    if (clicked) *v = !*v;
    const bool hovered = ImGui::IsItemHovered();

    const ImVec2 min = ImGui::GetItemRectMin();
    // 药丸画在最右（左右对齐）
    const ImVec2 pMin(min.x + total_w - w, min.y + (total_h - h) * 0.5f);
    const ImVec2 pMax(pMin.x + w, pMin.y + h);

    AnimState& a = GetAnim(id);
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, hovered ? 1.0f : 0.0f, 14.0f, dt);
    a.check = Step01(a.check, *v ? 1.0f : 0.0f, 16.0f, dt);

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const float r = h * 0.5f;

    // 轨道：淡紫 → 实紫（开），悬停微亮（仿UI：无描边环）
    const ImVec4 accent = Accent();
    ImVec4 trackCol = Lerp(style.Colors[ImGuiCol_FrameBg], accent, a.check);
    trackCol = Lerp(trackCol, AccentHover(), a.hover * a.check);
    dl->AddRectFilled(pMin, pMax, Col(trackCol), r);

    // 白色圆钮（弹性滑动，无阴影）
    const float knobR = r - fs * 0.07f;
    const float knobX = pMin.x + r + (w - h) * a.check;
    dl->AddCircleFilled(ImVec2(knobX, pMin.y + r), knobR, IM_COL32_WHITE, 24);

    // 标签靠左
    if (label_size.x > 0.0f) {
        const ImVec2 lp(min.x, min.y + (total_h - label_size.y) * 0.5f);
        dl->AddText(lp, Col(style.Colors[ImGuiCol_Text]), label, RenderedTextEnd(label));
    }
    return clicked;
}

// ============================================================================
// 仿UI 气泡滑块：标签在上，细轨道（左紫右灰）+ 白色圆钮（悬停放大）+ 数值气泡
// 与 ImGui::Slider* 兼容：返回本帧值是否变化。
// ============================================================================

// 绘制滑块主体（轨道 + 圆钮 + 气泡）；dispT/grow 动画状态按 ImGuiID 缓存
// bubbleText 为调用方已格式化好的数值文本（int 用 %d、float 用浮点格式，类型正确）。
inline void FancySliderBody(const ImGuiID id, const ImVec2& pos, float availW,
                            float t, const char* bubbleText,
                            bool hovered, bool active) {
    const ImGuiStyle& style = ImGui::GetStyle();
    ImDrawList* dl = ImGui::GetWindowDrawList();
    ImGuiStorage* stg = ImGui::GetStateStorage();
    const float dt = ImGui::GetIO().DeltaTime;
    const float fs = ImGui::GetFontSize();                  // 随整体 UI 缩放变化

    const float x0 = pos.x + fs * 0.18f, x1 = pos.x + availW - fs * 0.18f;
    const float trackY = pos.y + fs * 1.75f;                // 轨道下移，给上方气泡留空间（不与标签重叠）
    const float trackH = fs * 0.09f;

    // 显示位置 dispT 平滑追赶目标 t（点击/拖动不瞬移）
    const ImGuiID dtk = id + 0x1111;
    float dispT = stg->GetFloat(dtk, t);
    dispT = Step01(dispT, t, 16.0f, dt);
    if (std::fabs(dispT - t) < 0.0005f) dispT = t;
    stg->SetFloat(dtk, dispT);
    const float knobX = x0 + dispT * (x1 - x0);

    // 圆钮缩放：悬停/拖动时放大
    const ImGuiID gk = id + 0x2222;
    float grow = stg->GetFloat(gk, 0.0f);
    const float gTarget = active ? 1.0f : (hovered ? 0.55f : 0.0f);
    grow = Step01(grow, gTarget, 18.0f, dt);
    stg->SetFloat(gk, grow);
    const float knobR = fs * 0.24f + grow * fs * 0.045f;
    const float ringT = fs * 0.045f + grow * fs * 0.022f;

    const ImVec4 accent = Accent();

    // 轨道：灰底 + 紫已选
    dl->AddLine(ImVec2(x0, trackY), ImVec2(x1, trackY), Col(style.Colors[ImGuiCol_FrameBg]), trackH);
    dl->AddLine(ImVec2(x0, trackY), ImVec2(knobX, trackY), Col(accent), trackH);

    // 按下时光晕
    if (grow > 0.01f) {
        dl->AddCircleFilled(ImVec2(knobX, trackY), knobR + fs * 0.11f * grow,
                            Col(ImVec4(accent.x, accent.y, accent.z, 0.15f * grow)), 24);
    }
    // 白色圆钮 + 淡阴影 + 紫描边
    dl->AddCircleFilled(ImVec2(knobX, trackY + fs * 0.033f), knobR + fs * 0.022f,
                        Col(ImVec4(0.0f, 0.0f, 0.0f, 0.18f)), 24);
    dl->AddCircleFilled(ImVec2(knobX, trackY), knobR, IM_COL32_WHITE, 24);
    dl->AddCircle(ImVec2(knobX, trackY), knobR, Col(accent), 24, ringT);

    // 数值气泡（轨道上方、标签下方）
    const ImVec2 ts = ImGui::CalcTextSize(bubbleText);
    const float bw = ts.x + fs * 0.44f, bh = ts.y + fs * 0.22f;
    float bx = knobX - bw * 0.5f;
    if (bx < pos.x) bx = pos.x;
    if (bx + bw > pos.x + availW) bx = pos.x + availW - bw;
    const float by = trackY - fs * 0.45f - bh;
    dl->AddRectFilled(ImVec2(bx, by), ImVec2(bx + bw, by + bh),
                      Col(ImVec4(accent.x, accent.y, accent.z, 0.16f)), bh * 0.5f);
    dl->AddRect(ImVec2(bx, by), ImVec2(bx + bw, by + bh),
                Col(ImVec4(accent.x, accent.y, accent.z, 0.35f)), bh * 0.5f, 0, 1.0f);
    dl->AddText(ImVec2(bx + fs * 0.22f, by + fs * 0.11f), Col(accent), bubbleText);
}

inline bool SliderInt(const char* label, int* v, int v_min, int v_max,
                      const char* format = "%d", ImGuiSliderFlags flags = 0) {
    (void)flags;
    const float fs = ImGui::GetFontSize();                  // 随整体 UI 缩放变化
    const ImVec2 label_size = ImGui::CalcTextSize(label, RenderedTextEnd(label));
    const float labelH = ImGui::GetTextLineHeight();
    const float h = labelH + fs * 2.2f;                     // 标签 + 气泡 + 轨道 + 圆钮
    const float availW = ImGui::GetContentRegionAvail().x;
    const ImVec2 pos = ImGui::GetCursorScreenPos();

    ImGui::InvisibleButton(label, ImVec2(availW, h));
    const bool active = ImGui::IsItemActive();
    const bool hovered = ImGui::IsItemHovered();

    const float range = (float)(v_max - v_min);
    float t = (range != 0.0f) ? ((float)(*v - v_min) / range) : 0.0f;
    if (t < 0.0f) t = 0.0f; else if (t > 1.0f) t = 1.0f;

    bool changed = false;
    if (active) {
        const float x0 = pos.x + fs * 0.18f, x1 = pos.x + availW - fs * 0.18f;
        float nt = (ImGui::GetIO().MousePos.x - x0) / (x1 - x0);
        if (nt < 0.0f) nt = 0.0f; else if (nt > 1.0f) nt = 1.0f;
        const int nv = v_min + (int)(nt * range + 0.5f);
        if (nv != *v) { *v = nv; changed = true; }
        t = nt;
    }

    ImDrawList* dl = ImGui::GetWindowDrawList();
    if (label_size.x > 0.0f)
        dl->AddText(pos, Col(ImGui::GetStyle().Colors[ImGuiCol_Text]), label, RenderedTextEnd(label));

    // 数值文本按 int 正确格式化（避免 %d 传 float 的未定义行为）
    char buf[32];
    snprintf(buf, sizeof(buf), format, *v);
    FancySliderBody(ImGui::GetID(label), ImVec2(pos.x, pos.y + labelH), availW,
                    t, buf, hovered, active);
    return changed;
}

inline bool SliderFloat(const char* label, float* v, float v_min, float v_max,
                        const char* format = "%.3f", ImGuiSliderFlags flags = 0) {
    (void)flags;
    const float fs = ImGui::GetFontSize();                  // 随整体 UI 缩放变化
    const ImVec2 label_size = ImGui::CalcTextSize(label, RenderedTextEnd(label));
    const float labelH = ImGui::GetTextLineHeight();
    const float h = labelH + fs * 2.2f;                     // 标签 + 气泡 + 轨道 + 圆钮
    const float availW = ImGui::GetContentRegionAvail().x;
    const ImVec2 pos = ImGui::GetCursorScreenPos();

    ImGui::InvisibleButton(label, ImVec2(availW, h));
    const bool active = ImGui::IsItemActive();
    const bool hovered = ImGui::IsItemHovered();

    const float range = v_max - v_min;
    float t = (range != 0.0f) ? ((*v - v_min) / range) : 0.0f;
    if (t < 0.0f) t = 0.0f; else if (t > 1.0f) t = 1.0f;

    bool changed = false;
    if (active) {
        const float x0 = pos.x + fs * 0.18f, x1 = pos.x + availW - fs * 0.18f;
        float nt = (ImGui::GetIO().MousePos.x - x0) / (x1 - x0);
        if (nt < 0.0f) nt = 0.0f; else if (nt > 1.0f) nt = 1.0f;
        const float nv = v_min + nt * range;
        if (nv != *v) { *v = nv; changed = true; }
        t = nt;
    }

    ImDrawList* dl = ImGui::GetWindowDrawList();
    if (label_size.x > 0.0f)
        dl->AddText(pos, Col(ImGui::GetStyle().Colors[ImGuiCol_Text]), label, RenderedTextEnd(label));

    char buf[32];
    snprintf(buf, sizeof(buf), format, *v);
    FancySliderBody(ImGui::GetID(label), ImVec2(pos.x, pos.y + labelH), availW,
                    t, buf, hovered, active);
    return changed;
}

// ============================================================================
// 预览风格内容组件（复刻 WEB 演示的卡片内呈现）
// ============================================================================

// 行式开关（预览"主开关"）：左标签 + 右侧药丸开关，占满整行。返回是否切换。
inline bool RowToggle(const char* label, bool* v) { return Toggle(label, v); }

// 信息行（预览 .info 行）：左灰标签 + 右值（可指定颜色，默认亮白）。
inline void InfoRow(const char* label, const char* value, const ImVec4& vcol = ImVec4(1, 1, 1, 1)) {
    const ImGuiStyle& style = ImGui::GetStyle();
    const float fs = ImGui::GetFontSize();
    const float availW = ImGui::GetContentRegionAvail().x;
    const ImVec2 pos = ImGui::GetCursorScreenPos();
    const ImVec2 ls = ImGui::CalcTextSize(label);
    const ImVec2 vs = ImGui::CalcTextSize(value);
    ImDrawList* dl = ImGui::GetWindowDrawList();
    dl->AddText(pos, Col(style.Colors[ImGuiCol_TextDisabled]), label);
    dl->AddText(ImVec2(pos.x + availW - vs.x, pos.y), Col(vcol), value);
    ImGui::Dummy(ImVec2(0.0f, ls.y * 1.35f));
}

// 胶囊（预览 .chip）：可选中圆角胶囊。返回是否被点击。
inline bool Chip(const char* label, bool active, const ImVec2& size_arg = ImVec2(0, 0)) {
    const float fs = ImGui::GetFontSize();
    const ImVec2 label_size = ImGui::CalcTextSize(label, RenderedTextEnd(label));
    ImVec2 sz = size_arg;
    const float padX = fs * 0.30f, padY = fs * 0.16f;
    if (sz.x <= 0.0f) sz.x = label_size.x + padX * 2.0f;
    if (sz.y <= 0.0f) sz.y = label_size.y + padY * 2.0f;

    const ImGuiID id = ImGui::GetID(label);
    const bool clicked = ImGui::InvisibleButton(label, sz);
    const bool hovered = ImGui::IsItemHovered();

    AnimState& a = GetAnim(id);
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, hovered ? 1.0f : 0.0f, 14.0f, dt);
    a.check = Step01(a.check, active ? 1.0f : 0.0f, 16.0f, dt);

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();
    const float rounding = (max.y - min.y) * 0.5f;

    const ImVec4 accent = Accent();
    const ImVec4 bg = Lerp(ImVec4(accent.x, accent.y, accent.z, 0.12f),
                           ImVec4(accent.x, accent.y, accent.z, 0.20f + a.hover * 0.10f),
                           a.check);
    dl->AddRectFilled(min, max, Col(bg), rounding);
    const ImU32 border = a.check > 0.5f
        ? Col(ImVec4(accent.x, accent.y, accent.z, 0.85f))
        : Col(ImVec4(accent.x, accent.y, accent.z, 0.30f + a.hover * 0.25f));
    dl->AddRect(min, max, border, rounding, 0, 1.2f);

    const ImVec2 tp((min.x + max.x) * 0.5f - label_size.x * 0.5f,
                    (min.y + max.y) * 0.5f - label_size.y * 0.5f);
    const ImVec4 tc = Lerp(ImVec4(0.80f, 0.78f, 1.0f, 1.0f), ImVec4(1, 1, 1, 1), a.check);
    dl->AddText(tp, Col(Lerp(tc, ImVec4(1, 1, 1, 1), a.hover * 0.3f)), label, RenderedTextEnd(label));
    return clicked;
}

// 颜色块（预览 .sw）：点击弹出取色器（原生 ColorEdit4，隐藏输入框）。返回是否改变。
inline bool ColorSwatch(const char* label, ImVec4* color) {
    const float fs = ImGui::GetFontSize();
    const float sw = fs * 0.55f;
    ImGui::PushID(label);
    ImGui::AlignTextToFramePadding();
    ImGui::TextUnformatted(label);
    ImGui::SameLine();
    const float avail = ImGui::GetContentRegionAvail().x;
    ImGui::SetCursorPosX(ImGui::GetCursorPosX() + avail - sw);
    const bool changed = ImGui::ColorEdit4("##sw", &color->x,
        ImGuiColorEditFlags_NoInputs | ImGuiColorEditFlags_NoLabel | ImGuiColorEditFlags_NoBorder);
    ImGui::PopID();
    return changed;
}

// 本卡片内是否出现过折叠头（CollapsingHeader 置位、CardBegin 清零、CardEnd 快照）。
// 含折叠头的卡片不参与"卡片行内等高"：既不会被同行撑高，展开时也不会把同行撑高。
inline bool& CardCollapsibleFlagRef() { static bool v = false; return v; }
inline bool& CardWasCollapsibleRef() { static bool v = false; return v; }

// 折叠头（带动画）：圆角背景 + 旋转箭头 + 悬停强调色 + 打开时左侧强调条
struct HeaderState {
    bool open = false;
    bool initialized = false;
    float openAnim = 0.0f;
    float hover = 0.0f;
};

inline std::unordered_map<ImGuiID, HeaderState>& HeaderStates() {
    static std::unordered_map<ImGuiID, HeaderState> map;
    return map;
}

inline bool CollapsingHeader(const char* label, ImGuiTreeNodeFlags flags = 0) {
    const ImGuiStyle& style = ImGui::GetStyle();
    const ImGuiID id = ImGui::GetID(label);
    CardCollapsibleFlagRef() = true;   // 标记：本卡片内含可折叠内容（不参与卡片行内等高）
    HeaderState& st = HeaderStates()[id];
    if (!st.initialized) {
        // 默认收起（运行时点击头部展开）
        st.open = false;
        st.openAnim = st.open ? 1.0f : 0.0f;
        st.initialized = true;
    }

    const float dt = ImGui::GetIO().DeltaTime;
    const float headerH = ImGui::GetFrameHeight();
    const float width = std::max(ImGui::GetContentRegionAvail().x, 1.0f);

    const bool toggled = ImGui::InvisibleButton(label, ImVec2(width, headerH));
    if (toggled) st.open = !st.open;
    const bool hovered = ImGui::IsItemHovered();

    st.hover = Step01(st.hover, hovered ? 1.0f : 0.0f, 14.0f, dt);
    st.openAnim = Step01(st.openAnim, st.open ? 1.0f : 0.0f, 16.0f, dt);

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const ImVec2 rmin = ImGui::GetItemRectMin();
    const ImVec2 rmax = ImGui::GetItemRectMax();
    const float rounding = style.FrameRounding;

    // 背景
    ImVec4 bg = Lerp(style.Colors[ImGuiCol_Header], style.Colors[ImGuiCol_HeaderHovered], st.hover);
    dl->AddRectFilled(rmin, rmax, Col(bg), rounding);

    // 左侧强调条：打开时展开
    if (st.openAnim > 0.01f) {
        const float barH = (rmax.y - rmin.y) * 0.6f * st.openAnim;
        const ImVec2 bmin(rmin.x + 3.0f, (rmin.y + rmax.y) * 0.5f - barH * 0.5f);
        const ImVec2 bmax(bmin.x + 3.0f, bmin.y + barH);
        dl->AddRectFilled(bmin, bmax, Col(Accent()), 1.5f);
    }

    // 旋转箭头（0° → 90°）
    const float arrowSize = headerH * 0.36f;
    const ImVec2 ac(rmin.x + style.FramePadding.x + 6.0f + arrowSize * 0.5f,
                    (rmin.y + rmax.y) * 0.5f);
    const float ang = st.openAnim * 1.5707963f;
    const ImVec2 ap0 = RotateAround(ImVec2(ac.x - arrowSize * 0.5f, ac.y - arrowSize * 0.4f), ac, ang);
    const ImVec2 ap1 = RotateAround(ImVec2(ac.x + arrowSize * 0.5f, ac.y), ac, ang);
    const ImVec2 ap2 = RotateAround(ImVec2(ac.x - arrowSize * 0.5f, ac.y + arrowSize * 0.4f), ac, ang);
    dl->AddTriangleFilled(ap0, ap1, ap2, Col(Lerp(style.Colors[ImGuiCol_Text], AccentHover(), st.hover)));

    // 标签
    const ImVec2 labelSize = ImGui::CalcTextSize(label, RenderedTextEnd(label));
    const float textX = rmin.x + style.FramePadding.x * 2.0f + arrowSize + 4.0f;
    dl->AddText(ImVec2(textX, (rmin.y + rmax.y - labelSize.y) * 0.5f),
                Col(Lerp(style.Colors[ImGuiCol_Text], AccentHover(), st.hover)), label, RenderedTextEnd(label));

    return st.open;
}

// 无边框下拉（仿UI RowDropdown）：文字在左。
// rightAlign=false（默认）：当前值 + 小箭头紧跟标签（紧凑）；
// rightAlign=true：当前值 + 小箭头对齐到行尾。
inline bool Combo(const char* label, int* current_item, const char* const items[],
                  int items_count, bool rightAlign = false) {
    const ImGuiStyle& style = ImGui::GetStyle();
    const float fs = ImGui::GetFontSize();
    const float h = fs * 0.85f;
    const float availW = ImGui::GetContentRegionAvail().x;
    const ImVec2 pos = ImGui::GetCursorScreenPos();

    ImGui::InvisibleButton(label, ImVec2(availW, h));
    const bool hovered = ImGui::IsItemHovered();
    if (ImGui::IsItemClicked()) ImGui::OpenPopup(label);

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const float cy = pos.y + h * 0.5f;
    // 左：标签
    const ImVec2 ls = ImGui::CalcTextSize(label, RenderedTextEnd(label));
    dl->AddText(ImVec2(pos.x, cy - ls.y * 0.5f), Col(style.Colors[ImGuiCol_Text]), label, RenderedTextEnd(label));
    // 当前值（灰，悬停变紫）+ 小箭头：默认紧跟标签，rightAlign=true 时贴行尾
    const char* cur = (items && *current_item >= 0 && *current_item < items_count)
        ? items[*current_item] : "";
    const ImVec2 vs = ImGui::CalcTextSize(cur);
    const float chevW = fs * 0.45f;
    const ImVec4 vcol = hovered ? Accent() : style.Colors[ImGuiCol_TextDisabled];
    const float vx = rightAlign
        ? (pos.x + availW - chevW - vs.x - fs * 0.10f)
        : (pos.x + ls.x + fs * 0.35f);
    dl->AddText(ImVec2(vx, cy - vs.y * 0.5f), Col(vcol), cur);
    const float ax = rightAlign ? (pos.x + availW - chevW * 0.5f) : (vx + vs.x + chevW * 0.5f);
    dl->AddLine(ImVec2(ax - fs * 0.09f, cy - fs * 0.05f), ImVec2(ax, cy + fs * 0.05f), Col(vcol), 1.8f);
    dl->AddLine(ImVec2(ax, cy + fs * 0.05f), ImVec2(ax + fs * 0.09f, cy - fs * 0.05f), Col(vcol), 1.8f);

    // 弹出列表（选中项右侧打勾）；宽度按最长项自适应、夹紧在视口内；
    // 项数太多超高时限制高度并出现滚动条，保证底部项也能点到。
    bool changed = false;
    float popupW = 180.0f;
    for (int i = 0; i < items_count; ++i)
        popupW = std::max(popupW, ImGui::CalcTextSize(items[i]).x + fs * 1.4f);
    const ImGuiViewport* vp = ImGui::GetMainViewport();
    const float maxW = vp->WorkSize.x - pos.x - 8.0f;
    if (popupW > maxW) popupW = maxW;
    // 弹窗锚定到“当前值”所在位置（rightAlign=true 时贴行尾），避免弹到最右侧
    const float popupX = rightAlign
        ? std::max(0.0f, std::min(pos.x + availW - popupW, vp->WorkSize.x - popupW - 4.0f))
        : std::max(0.0f, std::min(vx, vp->WorkSize.x - popupW - 4.0f));
    const float popupY = pos.y + h + fs * 0.15f;
    // 估算自然高度（项高 × 数量 + 边距）；超过视口剩余高度则限高，让 ImGui 出滚动条
    const float itemH = fs * 1.15f + ImGui::GetStyle().FramePadding.y * 2.0f;
    const float naturalH = items_count * itemH + fs * 0.6f;
    float maxH = vp->WorkSize.y - popupY - 8.0f;
    if (maxH < fs * 2.5f) maxH = fs * 2.5f;
    const float popupH = naturalH > maxH ? maxH : 0.0f;   // 0 = 自动高度
    ImGui::SetNextWindowPos(ImVec2(popupX, popupY));
    ImGui::SetNextWindowSize(ImVec2(popupW, popupH));
    if (ImGui::BeginPopup(label)) {
        ImDrawList* pdl = ImGui::GetWindowDrawList();
        for (int i = 0; i < items_count; ++i) {
            ImGui::PushID(i);   // 防止相同文案的选项 ID 冲突
            const bool sel = (i == *current_item);
            if (ImGui::Selectable(items[i], sel)) { *current_item = i; changed = true; }
            if (sel) {
                const ImVec2 im = ImGui::GetItemRectMin(), iM = ImGui::GetItemRectMax();
                const float mcy = (im.y + iM.y) * 0.5f;
                const float mx = iM.x - 16.0f;
                pdl->AddLine(ImVec2(mx - 4, mcy), ImVec2(mx - 1, mcy + 3), Col(Accent()), 1.8f);
                pdl->AddLine(ImVec2(mx - 1, mcy + 3), ImVec2(mx + 4, mcy - 3), Col(Accent()), 1.8f);
            }
            ImGui::PopID();
        }
        ImGui::EndPopup();
    }
    return changed;
}

inline bool Combo(const char* label, int* current_item, const char* items_separated_by_zeros,
                  bool rightAlign = false) {
    // 把 \0 分隔的字符串拆成数组，统一走上面的仿UI RowDropdown
    const char* list[64];
    int count = 0;
    const char* p = items_separated_by_zeros;
    while (p && *p && count < 64) {
        list[count++] = p;
        while (*p) ++p;   // 跳到字符串末尾的 \0
        ++p;              // 跳过 \0
    }
    return Combo(label, current_item, list, count, rightAlign);
}

// 输入框（走 IME 桥）：底框随悬停平滑过渡
inline bool InputText(const char* label, char* buffer, size_t bufferSize,
                      ImGuiInputTextFlags flags = 0) {
    const ImGuiStyle& style = ImGui::GetStyle();
    AnimState& a = GetAnim(ImGui::GetID(label));
    ImGui::PushStyleColor(ImGuiCol_FrameBg,
        Lerp(style.Colors[ImGuiCol_FrameBg], style.Colors[ImGuiCol_FrameBgHovered], a.hover));
    const bool ret = ImGuiAndroidIme::InputText(label, buffer, bufferSize, flags);
    ImGui::PopStyleColor();
    a.hover = Step01(a.hover, ImGui::IsItemHovered() ? 1.0f : 0.0f, 14.0f, ImGui::GetIO().DeltaTime);
    return ret;
}

// 输入框焦点辉光（在控件提交后调用）：激活时外圈两层柔光 + 描边提亮，t 为焦点动画值
inline void DrawInputGlow(ImDrawList* dl, const ImVec2& min, const ImVec2& max, float t) {
    if (t <= 0.01f) return;
    const float rad = ImGui::GetStyle().FrameRounding;
    const ImVec4 accent = Accent();
    for (int g = 2; g >= 1; --g) {
        ImVec4 c = accent;
        c.w = 0.10f * t / (float)g;
        dl->AddRectFilled(ImVec2(min.x - (float)g * 2.0f, min.y - (float)g * 2.0f),
                          ImVec2(max.x + (float)g * 2.0f, max.y + (float)g * 2.0f),
                          Col(c), rad + (float)g * 2.0f);
    }
    ImVec4 bc = accent;
    bc.w = 0.55f * t;
    dl->AddRect(min, max, Col(bc), rad, 0, 1.4f);
}

inline bool InputTextWithHint(const char* label, const char* hint, char* buffer,
                              size_t bufferSize, ImGuiInputTextFlags flags = 0) {
    const ImGuiStyle& style = ImGui::GetStyle();
    AnimState& a = GetAnim(ImGui::GetID(label));
    ImGui::PushStyleColor(ImGuiCol_FrameBg,
        Col(Lerp(style.Colors[ImGuiCol_FrameBg], style.Colors[ImGuiCol_FrameBgHovered], a.hover)));
    const bool ret = ImGuiAndroidIme::InputTextWithHint(label, hint, buffer, bufferSize, flags);
    ImGui::PopStyleColor();
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, ImGui::IsItemHovered() ? 1.0f : 0.0f, 14.0f, dt);
    a.focus = Step01(a.focus, ImGui::IsItemActive() ? 1.0f : 0.0f, 18.0f, dt);
    if (a.focus > 0.01f)
        DrawInputGlow(ImGui::GetWindowDrawList(), ImGui::GetItemRectMin(), ImGui::GetItemRectMax(), a.focus);
    return ret;
}

inline bool InputInt(const char* label, int* value, int step = 1, int step_fast = 100,
                     ImGuiInputTextFlags flags = 0) {
    const ImGuiStyle& style = ImGui::GetStyle();
    AnimState& a = GetAnim(ImGui::GetID(label));
    ImGui::PushStyleColor(ImGuiCol_FrameBg,
        Col(Lerp(style.Colors[ImGuiCol_FrameBg], style.Colors[ImGuiCol_FrameBgHovered], a.hover)));
    const bool ret = ImGuiAndroidIme::InputInt(label, value, step, step_fast, flags);
    ImGui::PopStyleColor();
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, ImGui::IsItemHovered() ? 1.0f : 0.0f, 14.0f, dt);
    a.focus = Step01(a.focus, ImGui::IsItemActive() ? 1.0f : 0.0f, 18.0f, dt);
    if (a.focus > 0.01f)
        DrawInputGlow(ImGui::GetWindowDrawList(), ImGui::GetItemRectMin(), ImGui::GetItemRectMax(), a.focus);
    return ret;
}

// 整数输入框 + 灰色提示语（显示在框内，如“时长(秒)”）；失焦时把输入解析回 int。
// 状态（缓冲 + 焦点）按控件 ID 存在当前窗口的 StateStorage 里。
inline bool InputIntWithHint(const char* label, const char* hint, int* value,
                             ImGuiInputTextFlags flags = 0) {
    ImGuiStorage* stg = ImGui::GetStateStorage();
    const ImGuiID id = ImGui::GetID(label);
    const ImGuiID bufId = id + 0x3333;
    char* buf = (char*)stg->GetVoidPtr(bufId);
    if (!buf) {
        buf = (char*)ImGui::MemAlloc(32);
        stg->SetVoidPtr(bufId, buf);
        snprintf(buf, 32, "%d", *value);
    }
    const bool wasActive = stg->GetBool(bufId + 1, false);
    const bool ret = ImGuiAndroidIme::InputTextWithHint(label, hint, buf, 32, flags);
    const bool isActive = ImGui::IsItemActive();
    if (wasActive && !isActive) {
        // 刚失焦：解析输入并提交，再规范化为数字文本
        *value = atoi(buf);
        snprintf(buf, 32, "%d", *value);
    } else if (!isActive) {
        // 未聚焦：跟随外部值变化（如被其它逻辑修改）
        snprintf(buf, 32, "%d", *value);
    }
    stg->SetBool(bufId + 1, isActive);
    return ret;
}

inline bool InputFloat(const char* label, float* value, float step = 0.0f, float step_fast = 0.0f,
                       const char* format = "%.3f", ImGuiInputTextFlags flags = 0) {
    const ImGuiStyle& style = ImGui::GetStyle();
    AnimState& a = GetAnim(ImGui::GetID(label));
    ImGui::PushStyleColor(ImGuiCol_FrameBg,
        Col(Lerp(style.Colors[ImGuiCol_FrameBg], style.Colors[ImGuiCol_FrameBgHovered], a.hover)));
    const bool ret = ImGuiAndroidIme::InputFloat(label, value, step, step_fast, format, flags);
    ImGui::PopStyleColor();
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, ImGui::IsItemHovered() ? 1.0f : 0.0f, 14.0f, dt);
    a.focus = Step01(a.focus, ImGui::IsItemActive() ? 1.0f : 0.0f, 18.0f, dt);
    if (a.focus > 0.01f)
        DrawInputGlow(ImGui::GetWindowDrawList(), ImGui::GetItemRectMin(), ImGui::GetItemRectMax(), a.focus);
    return ret;
}

// 标准下拉（输入框外观 + 悬停/焦点动画）。语义同 ImGui::Combo。
inline bool ComboBox(const char* label, int* current_item, const char* const items[], int items_count) {
    const ImGuiStyle& style = ImGui::GetStyle();
    AnimState& a = GetAnim(ImGui::GetID(label));
    ImGui::PushStyleColor(ImGuiCol_FrameBg,
        Col(Lerp(style.Colors[ImGuiCol_FrameBg], style.Colors[ImGuiCol_FrameBgHovered], a.hover)));
    const bool ret = ImGui::Combo(label, current_item, items, items_count);
    ImGui::PopStyleColor();
    const float dt = ImGui::GetIO().DeltaTime;
    a.hover = Step01(a.hover, ImGui::IsItemHovered() ? 1.0f : 0.0f, 14.0f, dt);
    a.focus = Step01(a.focus, ImGui::IsItemActive() ? 1.0f : 0.0f, 18.0f, dt);
    if (a.focus > 0.01f)
        DrawInputGlow(ImGui::GetWindowDrawList(), ImGui::GetItemRectMin(), ImGui::GetItemRectMax(), a.focus);
    return ret;
}

// ============================================================================
// Toast 提示：顶部居中、圆角、出现(淡入+上滑)/淡出(上滑)、按时间上下堆叠。
// 线程安全：showToast 可从任意线程调用；DrawToasts 在渲染线程调用。
// ============================================================================
struct Toast {
    std::string text;
    double time = 0.0;
};

inline std::vector<Toast>& ToastQueue() {
    static std::vector<Toast> q;
    return q;
}

inline std::mutex& ToastMutex() {
    static std::mutex m;
    return m;
}

inline double NowSeconds() {
    return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

inline void showToast(const std::string& msg) {
    std::lock_guard<std::mutex> lock(ToastMutex());
    auto& q = ToastQueue();
    q.push_back({msg, NowSeconds()});   // 新消息追加在末尾
    if (q.size() > 5) q.erase(q.begin());   // 最多保留 5 条
}

// Chrome 正文字号（由全局字号推导）。在 Chrome 之外绘制的元素（Toast / 悬浮球）也用它，
// 才能和缩小后的菜单保持同一尺度。
inline float ChromeFontSizeShared() {
    const float gFont = ImGui::GetFontSize();
    const float k = ImClamp(gFont / 40.0f, 0.85f, 1.90f);
    return 20.0f * k;
}

// Chrome 窗口矩形（供 Toast 定位到"窗口内顶部"——HTML .toasts{top:96px} 就是窗口内定位）
inline ImVec2& ChromeMinRef() { static ImVec2 v(0.0f, 0.0f); return v; }
inline ImVec2& ChromeMaxRef() { static ImVec2 v(0.0f, 0.0f); return v; }

// 计算单条 toast 的透明度/位移/缩放（对齐 HTML：in 0.35s 自 -16px + scale .9；2.2s 后上移 14px 淡出）
inline void ToastAnim(double age, float u, float& alpha, float& offsetY, float& scale) {
    constexpr float kIn      = 0.35f;
    constexpr float kOutAt   = 2.2f;
    constexpr float kOutDur  = 0.45f;
    alpha = 1.0f; offsetY = 0.0f; scale = 1.0f;
    if (age < kIn) {
        const float t = (float)(age / kIn);
        const float e = t * t * (3.0f - 2.0f * t);
        alpha = e;
        offsetY = (1.0f - e) * -16.0f * u;
        scale = 0.90f + 0.10f * e;
    } else if (age > kOutAt) {
        const float t = (float)ImMin((age - kOutAt) / kOutDur, 1.0);
        const float e = t * t * (3.0f - 2.0f * t);
        alpha = 1.0f - e;
        offsetY = -14.0f * u * e;
    }
}

// Toast：复刻 HTML .toasts / .toast ——
//   窗口内顶部 96u 居中、纵向堆叠（新的在下方、gap 8u）、药丸形、文字居中、
//   宽度按内容自适应（nowrap；过长才限宽换行）。画在前景 draw list 上，保证盖在菜单之上。
inline void DrawToasts() {
    std::lock_guard<std::mutex> lock(ToastMutex());
    auto& q = ToastQueue();

    const double now = NowSeconds();
    for (auto it = q.begin(); it != q.end();) {
        if (now - it->time > 2.8) it = q.erase(it); else ++it;   // HTML: 2.8s 后移除
    }
    if (q.empty()) return;

    ImGuiIO& io = ImGui::GetIO();
    ImFont* font = ImGui::GetFont();
    const float chromeFs = ChromeFontSizeShared();
    const float u = (chromeFs > 1.0f) ? (chromeFs / 20.0f) : 1.0f;
    const float tFont = chromeFs * 0.95f;           // HTML 12.5px / 正文 13px
    const float padX = 20.0f * u, padY = 9.0f * u, gap = 8.0f * u;
    const float maxBoxW = io.DisplaySize.x * 0.72f;

    // Toast 锚点：默认从【屏幕顶部】居中弹出（不再跟随菜单窗口位置）。
    // 若要改回“菜单窗口内顶部”，把 kAnchorToScreen 设为 false 即可。
    const bool kAnchorToScreen = true;
    const ImVec2 cm = ChromeMinRef();
    const ImVec2 cM = ChromeMaxRef();
    const bool hasWin = (cM.x > cm.x + 1.0f) && (cM.y > cm.y + 1.0f);
    const float centerX = (!kAnchorToScreen && hasWin) ? (cm.x + cM.x) * 0.5f : io.DisplaySize.x * 0.5f;
    float y = kAnchorToScreen ? (28.0f * u) : (hasWin ? (cm.y + 96.0f * u) : (28.0f * u));

    ImDrawList* fg = ImGui::GetForegroundDrawList();
    for (auto it = q.begin(); it != q.end(); ++it) {
        float alpha = 1.0f, dy = 0.0f, scale = 1.0f;
        ToastAnim(now - it->time, u, alpha, dy, scale);
        if (alpha <= 0.01f) continue;

        const char* txt = it->text.c_str();
        ImVec2 ts = font->CalcTextSizeA(tFont, FLT_MAX, 0.0f, txt);
        bool wrap = false;
        float boxW = ts.x + padX * 2.0f;
        if (boxW > maxBoxW) {
            wrap = true;
            boxW = maxBoxW;
            ts = font->CalcTextSizeA(tFont, FLT_MAX, boxW - padX * 2.0f, txt);
        }
        const float boxH = ImMax(ts.y + padY * 2.0f, tFont + padY * 2.0f);
        const ImVec2 pMin(centerX - boxW * 0.5f, y + dy);
        const ImVec2 pMax(pMin.x + boxW, pMin.y + boxH);
        const ImVec2 c((pMin.x + pMax.x) * 0.5f, (pMin.y + pMax.y) * 0.5f);
        const float hw = boxW * 0.5f * scale, hh = boxH * 0.5f * scale;
        const ImVec2 q0(c.x - hw, c.y - hh), q1(c.x + hw, c.y + hh);
        const float r = hh;   // 药丸（HTML border-radius:100px）
        const int A = (int)ImClamp(alpha * 255.0f, 0.0f, 255.0f);

        // 紫色辉光（HTML box-shadow 第二项）
        for (int gi = 3; gi >= 1; --gi) {
            const int ga = (int)((float)A * 0.06f * (float)(4 - gi));
            fg->AddRectFilled(ImVec2(q0.x - (float)gi * u, q0.y - (float)gi * u + u),
                              ImVec2(q1.x + (float)gi * u, q1.y + (float)gi * u + 2.0f * u),
                              IM_COL32(139, 124, 255, ga), r + (float)gi * u);
        }
        // 向下投影（HTML box-shadow 第一项）
        fg->AddRectFilled(ImVec2(q0.x, q0.y + 4.0f * u), ImVec2(q1.x, q1.y + 6.0f * u),
                          IM_COL32(0, 0, 0, (int)((float)A * 0.55f)), r);
        // 药丸底 + 1px 描边（HTML background:rgba(26,26,36,.9) / border:var(--line-strong)）
        fg->AddRectFilled(q0, q1, IM_COL32(26, 26, 36, (int)(230.0f * alpha)), r);
        fg->AddRect(q0, q1, IM_COL32(255, 255, 255, (int)(42.0f * alpha)), r, 0, 1.0f);

        // 文字（居中；过长才换行）
        if (wrap) {
            // 手动换行 + 逐行居中，直接画在前景列表上（不依赖"当前窗口"的 draw list）
            ImFont* f = font;
            const char* s = txt;
            const char* end = txt + std::strlen(txt);
            const float wrapW = boxW - padX * 2.0f;
            float ly = q0.y + padY;
            while (s < end) {
                const char* lineEnd = f->CalcWordWrapPosition(tFont, s, end, wrapW);
                if (lineEnd <= s) lineEnd = s + 1;      // 防御：至少前进一个字节
                if (lineEnd > end) lineEnd = end;
                const float lw = f->CalcTextSizeA(tFont, FLT_MAX, 0.0f, s, lineEnd).x;
                fg->AddText(f, tFont, ImVec2(c.x - lw * 0.5f, ly), IM_COL32(255, 255, 255, A), s, lineEnd);
                ly += tFont * 1.25f;
                s = lineEnd;
                while (s < end && (*s == ' ' || *s == '\n' || *s == '\r')) ++s;
            }
        } else {
            fg->AddText(font, tFont, ImVec2(c.x - ts.x * 0.5f, c.y - ts.y * 0.5f),
                        IM_COL32(255, 255, 255, A), txt);
        }

        y += boxH + gap;   // 按"未动画"的高度推进，堆叠稳定
    }
}

// ============================================================================
// 卡片容器（新 UI）：圆角深色卡片 + 可选标题条，内容自适应高度。
// 用法：UI::CardBegin("##card", "标题"); ...内容...; UI::CardEnd();
// 标题可为空；卡片间自动留间距。内容自适应高度，随整体 UI 缩放。
// ============================================================================
inline void CardBegin(const char* id, const char* title, float width = 0.0f) {
    const Glass::Palette p = Glass::DarkPalette();
    const float fs = ImGui::GetFontSize();
    CardCollapsibleFlagRef() = false;   // 新卡片：重置"含折叠头"标记
    // 卡片表面比窗口底更亮 + 更明显描边；略透明（0.88，与预览 rgba(...,.86) 一致）
    // → 背景的光束雨 / 极光 / 浮点能透出来
    ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(0.094f, 0.094f, 0.125f, 0.88f));   // #181820
    ImGui::PushStyleColor(ImGuiCol_Border, ImVec4(1.0f, 1.0f, 1.0f, 0.13f));
    ImGui::PushStyleVar(ImGuiStyleVar_ChildRounding, fs * 0.45f);
    ImGui::PushStyleVar(ImGuiStyleVar_ChildBorderSize, 1.2f);
    ImGui::BeginChild(id, ImVec2(width, 0), ImGuiChildFlags_AutoResizeY | ImGuiChildFlags_Borders);
    if (title && *title) {
        ImDrawList* dl = ImGui::GetWindowDrawList();
        const ImVec2 cpos = ImGui::GetCursorScreenPos();
        const float cw = ImGui::GetContentRegionAvail().x;
        const float tFont = fs * 0.95f;
        const ImVec2 ts = ImGui::GetFont()->CalcTextSizeA(tFont, FLT_MAX, 0.0f, title);
        const float titleH = ts.y + fs * 0.55f;
        // 标题左侧强调条 + 标题文字
        dl->AddRectFilled(ImVec2(cpos.x + 2.0f, cpos.y + 3.0f),
                          ImVec2(cpos.x + 2.0f + fs * 0.18f, cpos.y + 3.0f + ts.y - 2.0f), p.accent, fs * 0.09f);
        dl->AddText(ImGui::GetFont(), tFont, ImVec2(cpos.x + fs * 0.42f, cpos.y), p.text, title);
        ImGui::Dummy(ImVec2(0.0f, titleH));
        // 标题下分隔线
        dl->AddLine(ImVec2(cpos.x + 2.0f, cpos.y + titleH),
                    ImVec2(cpos.x + cw - 4.0f, cpos.y + titleH), p.cardBorder, 1.0f);
        ImGui::Dummy(ImVec2(0.0f, fs * 0.35f));
    }
}

// 卡片顶部 Y / 原始内容高度（CardEnd 内记录；供网格统计行高）
inline float& CardTopRef() { static float y = 0.0f; return y; }
inline float& CardRawHRef() { static float h = 0.0f; return h; }

// 结束卡片并返回卡片底部 Y。
// minH > 0 时：内容不足该高度就用 Dummy 补足（网格行内等高用）；
//              内容更高时不做任何裁剪，卡片照常被内容撑高（带折叠展开的卡片因此不受影响）。
inline float CardEnd(float minH = 0.0f) {
    const ImGuiStyle& style = ImGui::GetStyle();
    // 补足「之前」的原始内容高度：必须用它回填网格缓存，
    // 否则会把补出来的 Dummy 也算进去 → 每帧越补越高（拉伸/回弹闪烁）。
    CardRawHRef() = ImGui::GetCursorPosY() + style.WindowPadding.y;
    CardWasCollapsibleRef() = CardCollapsibleFlagRef();   // 快照：本卡片是否含折叠头
    if (minH > 0.0f) {
        const float pad = (minH - style.WindowPadding.y) - ImGui::GetCursorPosY();
        if (pad > 0.5f) ImGui::Dummy(ImVec2(0.0f, pad));
    }
    ImGui::EndChild();
    CardTopRef() = ImGui::GetItemRectMin().y;
    const float bottom = ImGui::GetItemRectMax().y;
    ImGui::PopStyleVar(2);
    ImGui::PopStyleColor(2);
    ImGui::Dummy(ImVec2(0.0f, ImGui::GetFontSize() * 0.25f));
    return bottom;
}

// ============================================================================
// 卡片网格（复刻 WEB 演示右侧的卡片网格布局：多列并排、行底对齐、可跨列）。
// 用法：
//   UI::CardGrid g;
//   UI::CardGridBegin(g, 2);                 // 2 列网格
//   float w = UI::CardGridNext(g, span);     // 取下一个槽位（span=2 整行）
//   UI::CardBegin("##c", 标题, w);
//   ...内容...
//   UI::CardGridEndCard(g);                  // 记录卡片底，推进网格
//   ...
//   UI::CardGridWrap(g);                     // 收尾（可选）
// ============================================================================
struct CardGrid {
    ImVec2 origin = ImVec2(0, 0);   // 网格原点（首个卡片左上）
    float width = 0.0f;             // 网格总宽
    float rowY = 0.0f;              // 当前行顶
    float rowBottom = 0.0f;         // 当前行底（最高卡片底）
    int col = 0;                    // 当前列
    int cols = 2;                   // 列数
    float gapX = 8.0f, gapY = 8.0f;
    // 行内等高：上一帧记录的行高（本帧先按它补足，帧末再更新）
    ImGuiID id = 0;
    int rowIdx = 0;
    float curMinH = 0.0f;
    float rowMaxH = 0.0f;
};

// 行高缓存：CardGrid id → (行号 → 该行内容最高高度)
inline std::unordered_map<ImGuiID, std::unordered_map<int, float>>& CardRowHeights() {
    static std::unordered_map<ImGuiID, std::unordered_map<int, float>> m;
    return m;
}

inline void CardGridBegin(CardGrid& g, int cols = 2) {
    g.cols = cols > 0 ? cols : 2;
    g.origin = ImGui::GetCursorScreenPos();
    g.width = ImGui::GetContentRegionAvail().x;
    g.rowY = g.origin.y;
    g.rowBottom = g.origin.y;
    g.col = 0;
    g.id = ImGui::GetID("##cardgrid");
    g.rowIdx = 0;
    g.curMinH = 0.0f;
    g.rowMaxH = 0.0f;
    const float fs = ImGui::GetFontSize();
    g.gapX = fs * 0.55f;
    g.gapY = fs * 0.50f;
}

// 换行：把光标移到当前行底 + 间距
inline void CardGridWrap(CardGrid& g) {
    if (g.col == 0 && g.rowMaxH <= 0.0f) return;
    if (g.rowMaxH > 0.0f) CardRowHeights()[g.id][g.rowIdx] = g.rowMaxH;   // 记下本行高度供下一帧使用
    g.rowIdx++;
    g.rowMaxH = 0.0f;
    g.curMinH = 0.0f;
    ImGui::SetCursorScreenPos(ImVec2(g.origin.x, g.rowBottom + g.gapY));
    ImGui::Dummy(ImVec2(0.0f, 0.0f));   // 撑开父边界（否则后面的 SetCursorScreenPos 会触发 ImGui 断言）
    g.rowY = ImGui::GetCursorScreenPos().y;
    g.rowBottom = g.rowY;
    g.col = 0;
}

// 取下一个卡片槽位；span 为占列数（= cols 时整行）。返回卡片宽度。
inline float CardGridNext(CardGrid& g, int span = 1) {
    const int cols = g.cols;
    if (span > cols) span = cols;
    if (span < 1) span = 1;
    if (g.col + span > cols) CardGridWrap(g);
    const float unitW = (g.width - g.gapX * (cols - 1)) / cols;
    const float w = span * unitW + (span - 1) * g.gapX;
    if (g.col == 0) {
        if (g.rowBottom > g.origin.y + 0.5f)
            ImGui::SetCursorScreenPos(ImVec2(g.origin.x, g.rowBottom + g.gapY));
        g.rowY = ImGui::GetCursorScreenPos().y;
    } else {
        const float x = g.origin.x + g.col * (unitW + g.gapX);
        ImGui::SetCursorScreenPos(ImVec2(x, g.rowY));
    }
    g.col += span;
    // 行内等高：取上一帧该行的最高高度作为本卡片的最小高度
    auto it = CardRowHeights().find(g.id);
    g.curMinH = (it != CardRowHeights().end() && it->second.count(g.rowIdx))
              ? it->second[g.rowIdx] : 0.0f;
    return w;
}

// 结束网格中的一张卡片并推进行状态
inline void CardGridEndCard(CardGrid& g) {
    // 含折叠头的卡片：不参与行内等高（不补足、也不影响同行高度）
    const bool collapsible = CardWasCollapsibleRef();
    const float bottom = CardEnd(collapsible ? 0.0f : g.curMinH);
    if (!collapsible && CardRawHRef() > g.rowMaxH) g.rowMaxH = CardRawHRef();
    if (bottom > g.rowBottom) g.rowBottom = bottom;
}

} // namespace UI
