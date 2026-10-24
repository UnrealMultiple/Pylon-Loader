#pragma once

// ============================================================================
// GlassKit.h —— “仿UI” 效果移植（暗色玻璃拟态视觉组件）
// 从 C:\Users\admin\Desktop\仿UI 演示工程移植的纯视觉/动效，全部 header-only：
//   - 暗色配色 Palette（bg 0x0A0A0C / 卡片 0x15151B / 强调色 0x7C74FF）
//   - 圆角卡片 + 多层软阴影 PanelWithShadow
//   - 半透明液态玻璃胶囊 GlassPill（无真实模糊，用半透明叠色 + 顶边高光模拟）
//   - 点阵背景 DrawDotGrid
//   - 光束雨 BeamRain（下落细光 + 落地溅射）
//   - 粒子爆裂 ParticleBurst
//   - 四角星 logo DrawSpark
//   - 活动 Tab 灯条 TabGlow（弹簧跟随 + 顶灯 + 光晕 + 向下光锥）
//   - 弹性 / 指数缓动工具 SpringTick / Approach
//   - 签名控件：药丸开关 ToggleSwitch、翻转勾选 CheckChip、气泡滑块 FancySlider
// 使用方式：MainWindow 等窗口 include 本头文件，按需调用。
// ============================================================================

#include "../imgui.h"
#include "../imgui_internal.h"   // ImGuiTabBar / TabBarFindTabByID / GetCurrentTabBar

#include <cmath>
#include <cstdio>
#include <cstdlib>

namespace Glass {

// ============================================================================
// 配色（暗色为主，抄自 仿UI Theme::Dark()）
// ============================================================================
struct Palette {
    ImU32 bg;            // 画布背景（近黑）
    ImU32 card;          // 卡片面板底色
    ImU32 cardBorder;    // 卡片描边
    ImU32 dotGrid;       // 点阵纹理颜色
    ImU32 text;          // 主文字
    ImU32 textDim;       // 次要文字
    ImU32 accent;        // 主色（蓝紫）
    ImU32 accentHover;   // 主色悬停
    ImU32 accentSoft;    // 主色浅底（选中背景 / tag）
    ImU32 track;         // 滑块轨道底色
    ImU32 danger;        // 危险色（红）
    ImU32 dangerSoft;    // 危险浅底
    ImU32 success;       // 成功色（绿）
    ImU32 navBar;        // 玻璃胶囊底色（半透明磨砂）
    ImU32 shadow;        // 阴影颜色
};

inline Palette DarkPalette() {
    Palette p;
    p.bg          = IM_COL32(0x0A, 0x0A, 0x0C, 0xFF);
    p.card        = IM_COL32(0x15, 0x15, 0x1B, 0xFF);
    p.cardBorder  = IM_COL32(0xFF, 0xFF, 0xFF, 0x14);
    p.dotGrid     = IM_COL32(0xFF, 0xFF, 0xFF, 0x12);   // 点阵纹理颜色
    p.text        = IM_COL32(0xF4, 0xF4, 0xF6, 0xFF);
    p.textDim     = IM_COL32(0xF4, 0xF4, 0xF6, 0x72);
    p.accent      = IM_COL32(0x7C, 0x74, 0xFF, 0xFF);
    p.accentHover = IM_COL32(0x8F, 0x88, 0xFF, 0xFF);
    p.accentSoft  = IM_COL32(0x7C, 0x74, 0xFF, 0x2E);
    p.track       = IM_COL32(0x7C, 0x74, 0xFF, 0x33);
    p.danger      = IM_COL32(0xFF, 0x5A, 0x5A, 0xFF);
    p.dangerSoft  = IM_COL32(0xFF, 0x5A, 0x5A, 0x1E);
    p.success     = IM_COL32(0x3A, 0xD6, 0x7E, 0xFF);
    p.navBar      = IM_COL32(0x2A, 0x2A, 0x38, 0xB4);
    p.shadow      = IM_COL32(0x00, 0x00, 0x00, 0x66);
    return p;
}

inline ImVec4 U32ToVec4(ImU32 c) { return ImGui::ColorConvertU32ToFloat4(c); }

// ============================================================================
// 缓动工具
// ============================================================================

// 指数平滑（帧率无关）：cur 朝 target 以 speed 收敛
inline float Approach(float cur, float target, float dt, float speed = 10.0f) {
    if (dt <= 0.0f) return target;
    const float t = 1.0f - expf(-speed * dt);
    return cur + (target - cur) * t;
}

// 弹簧状态（仿UI Spring：stiff 刚度 / damp 阻尼）
struct SpringState {
    float pos = 0.0f;
    float vel = 0.0f;
};

inline float SpringTick(SpringState& s, float target, float dt,
                        float stiff = 220.0f, float damp = 22.0f) {
    const float force = (target - s.pos) * stiff - s.vel * damp;
    s.vel += force * dt;
    s.pos += s.vel * dt;
    return s.pos;
}

static inline float Rnd01() { return (float)rand() / (float)RAND_MAX; }
static inline float RndRange(float a, float b) { return a + (b - a) * Rnd01(); }

// ============================================================================
// 面板 / 玻璃
// ============================================================================

// 带柔和阴影的圆角面板（多层偏移矩形模拟软阴影）
inline void PanelWithShadow(ImDrawList* dl, ImVec2 min, ImVec2 max,
                            ImU32 fill, ImU32 border, ImU32 shadow,
                            float rounding = 16.0f) {
    for (int i = 6; i >= 1; i--) {
        const float o = (float)i * 2.0f;
        ImVec4 s = U32ToVec4(shadow);
        s.w *= (0.16f * (7 - i) / 6.0f);
        dl->AddRectFilled(ImVec2(min.x - o, min.y - o + 4), ImVec2(max.x + o, max.y + o + 6),
                          ImGui::ColorConvertFloat4ToU32(s), rounding + o);
    }
    dl->AddRectFilled(min, max, fill, rounding);
    dl->AddRect(min, max, border, rounding, 0, 1.5f);
}

// 液态玻璃胶囊：多层软阴影 + 半透明磨砂底 + 顶边高光 + 细描边
// （真模糊需后端支持，此处用半透明叠色模拟磨砂）
inline void GlassPill(ImDrawList* dl, ImVec2 min, ImVec2 max,
                      const Palette& p, float rounding) {
    // 1) 外柔和阴影
    for (int i = 7; i >= 1; i--) {
        const float o = (float)i * 1.8f;
        ImVec4 s = U32ToVec4(p.shadow);
        s.w *= 0.10f * (8 - i) / 7.0f;
        dl->AddRectFilled(ImVec2(min.x - o, min.y - o + 3), ImVec2(max.x + o, max.y + o + 5),
                          ImGui::ColorConvertFloat4ToU32(s), rounding + o);
    }
    // 2) 半透明磨砂底
    dl->AddRectFilled(min, max, p.navBar, rounding);
    // 3) 顶边淡淡高光（玻璃反光）
    ImVec4 hl(1.0f, 1.0f, 1.0f, 0.10f);
    dl->AddLine(ImVec2(min.x + rounding, min.y + 1.0f), ImVec2(max.x - rounding, min.y + 1.0f),
                ImGui::ColorConvertFloat4ToU32(hl), 1.0f);
    // 4) 细描边
    dl->AddRect(min, max, p.cardBorder, rounding, 0, 1.0f);
}

// ============================================================================
// 背景特效
// ============================================================================

// 在矩形内绘制点阵纹理（磨砂面板上的小圆点）
inline void DrawDotGrid(ImDrawList* dl, ImVec2 min, ImVec2 max,
                        ImU32 dotColor, float spacing = 20.0f, float cornerRad = 0.0f) {
    dl->PushClipRect(min, max, true);
    for (float y = min.y + spacing * 0.5f; y < max.y; y += spacing) {
        for (float x = min.x + spacing * 0.5f; x < max.x; x += spacing) {
            // 圆角跳角：落在窗口圆角之外的网点不画（这样点阵可铺满整窗而不露棱角）
            if (cornerRad > 0.5f) {
                float cx = -1.0f, cy = -1.0f;
                if (x < min.x + cornerRad && y < min.y + cornerRad)            { cx = min.x + cornerRad; cy = min.y + cornerRad; }
                else if (x > max.x - cornerRad && y < min.y + cornerRad)       { cx = max.x - cornerRad; cy = min.y + cornerRad; }
                else if (x < min.x + cornerRad && y > max.y - cornerRad)       { cx = min.x + cornerRad; cy = max.y - cornerRad; }
                else if (x > max.x - cornerRad && y > max.y - cornerRad)       { cx = max.x - cornerRad; cy = max.y - cornerRad; }
                if (cx >= 0.0f) {
                    const float dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > cornerRad * cornerRad) continue;
                }
            }
            dl->AddRectFilled(ImVec2(x - 0.8f, y - 0.8f), ImVec2(x + 0.8f, y + 0.8f), dotColor);
        }
    }
    dl->PopClipRect();
}

// ----------------------------------------------------------------------------
// 带圆角 + 纵向渐变的一次性填充（含抗锯齿边缘）。
// 为什么需要它：极光光带若用多个半透明矩形拼接，接缝处的 AA 边缘会叠出/漏出
// 1px 的亮线或暗线（放大后非常明显）。这里按 ImGui 内部 AddConvexPolyFilled 的
// 算法，把"圆角路径 + 逐顶点颜色"一次写成，因此形状与窗口圆角一致、无接缝、不溢出。
// ----------------------------------------------------------------------------
inline void AddRoundedGradientRect(ImDrawList* dl, ImVec2 pMin, ImVec2 pMax,
                                   float rounding, ImDrawFlags flags,
                                   ImU32 colTop, ImU32 colBot) {
    dl->PathRect(pMin, pMax, rounding, flags);
    const int n = dl->_Path.Size;
    if (n < 3) { dl->_Path.Size = 0; return; }

    const float AA = (dl->_FringeScale > 0.0f) ? dl->_FringeScale : 1.0f;
    const float span = ImMax(pMax.y - pMin.y, 1.0f);
    const int a0 = (int)((colTop >> IM_COL32_A_SHIFT) & 0xFF);
    const int a1 = (int)((colBot >> IM_COL32_A_SHIFT) & 0xFF);
    const ImU32 rgb = colTop & ~IM_COL32_A_MASK;
    const ImVec2 uv = dl->_Data->TexUvWhitePixel;
    auto colAt = [&](const ImVec2& p) -> ImU32 {
        const float t = ImClamp((p.y - pMin.y) / span, 0.0f, 1.0f);
        const int a = (int)ImClamp((float)a0 + (float)(a1 - a0) * t, 0.0f, 255.0f);
        return rgb | ((ImU32)a << IM_COL32_A_SHIFT);
    };

    // 每条边的法线（用于外侧羽化边）
    dl->_Data->TempBuffer.reserve_discard(n);
    ImVec2* normals = dl->_Data->TempBuffer.Data;
    for (int i0 = n - 1, i1 = 0; i1 < n; i0 = i1++) {
        float dx = dl->_Path[i1].x - dl->_Path[i0].x;
        float dy = dl->_Path[i1].y - dl->_Path[i0].y;
        const float len = sqrtf(dx * dx + dy * dy);
        if (len > 1e-6f) { dx /= len; dy /= len; } else { dx = 0.0f; dy = 0.0f; }
        normals[i0].x = dy;
        normals[i0].y = -dx;
    }

    if ((dl->Flags & ImDrawListFlags_AntiAliasedFill) == 0) {
        // 无 AA：三角扇 + 逐顶点颜色
        dl->PrimReserve((n - 2) * 3, n);
        const ImDrawIdx base = (ImDrawIdx)dl->_VtxCurrentIdx;
        for (int i = 2; i < n; ++i) {
            dl->PrimWriteIdx(base);
            dl->PrimWriteIdx((ImDrawIdx)(base + i - 1));
            dl->PrimWriteIdx((ImDrawIdx)(base + i));
        }
        for (int i = 0; i < n; ++i) dl->PrimWriteVtx(dl->_Path[i], uv, colAt(dl->_Path[i]));
    } else {
        // 有 AA：主体三角扇 + 每条边的外侧羽化（外圈 alpha 置 0）
        dl->PrimReserve((n - 2) * 3 + n * 6, n * 2);
        const unsigned int inner = dl->_VtxCurrentIdx;
        const unsigned int outer = dl->_VtxCurrentIdx + 1;
        for (int i = 2; i < n; ++i) {
            dl->_IdxWritePtr[0] = (ImDrawIdx)inner;
            dl->_IdxWritePtr[1] = (ImDrawIdx)(inner + ((i - 1) << 1));
            dl->_IdxWritePtr[2] = (ImDrawIdx)(inner + (i << 1));
            dl->_IdxWritePtr += 3;
        }
        for (int i0 = n - 1, i1 = 0; i1 < n; i0 = i1++) {
            float dmx = (normals[i0].x + normals[i1].x) * 0.5f;
            float dmy = (normals[i0].y + normals[i1].y) * 0.5f;
            const float l = sqrtf(dmx * dmx + dmy * dmy);
            if (l > 1e-6f) { dmx /= l; dmy /= l; } else { dmx = 0.0f; dmy = 0.0f; }
            dmx *= AA * 0.5f;
            dmy *= AA * 0.5f;
            const ImU32 c  = colAt(dl->_Path[i1]);
            const ImU32 cT = c & ~IM_COL32_A_MASK;
            dl->_VtxWritePtr[0].pos = ImVec2(dl->_Path[i1].x - dmx, dl->_Path[i1].y - dmy);
            dl->_VtxWritePtr[0].uv = uv;
            dl->_VtxWritePtr[0].col = c;
            dl->_VtxWritePtr[1].pos = ImVec2(dl->_Path[i1].x + dmx, dl->_Path[i1].y + dmy);
            dl->_VtxWritePtr[1].uv = uv;
            dl->_VtxWritePtr[1].col = cT;
            dl->_VtxWritePtr += 2;
            dl->_IdxWritePtr[0] = (ImDrawIdx)(inner + (i1 << 1));
            dl->_IdxWritePtr[1] = (ImDrawIdx)(inner + (i0 << 1));
            dl->_IdxWritePtr[2] = (ImDrawIdx)(outer + (i0 << 1));
            dl->_IdxWritePtr[3] = (ImDrawIdx)(outer + (i0 << 1));
            dl->_IdxWritePtr[4] = (ImDrawIdx)(outer + (i1 << 1));
            dl->_IdxWritePtr[5] = (ImDrawIdx)(inner + (i1 << 1));
            dl->_IdxWritePtr += 6;
        }
        dl->_VtxCurrentIdx += (ImDrawIdx)(n * 2);
    }
    dl->_Path.Size = 0;
}

// 竖向光束雨：一束束向下掠过的细光线，叠在面板上像下雨；落地溅射
class BeamRain {
public:
    void Update(float dt) {
        for (int i = 0; i < kMax; i++)
            beams[i].y += beams[i].speed * dt;
        for (int i = 0; i < kSplash; i++) {
            if (!splashes[i].alive) continue;
            Splash& s = splashes[i];
            s.life -= dt;
            for (int k = 0; k < s.pn; k++) {
                s.pv[k].y += 55.0f * dt;
                s.pv[k].x *= (1.0f - 1.4f * dt);
                s.pv[k].y *= (1.0f - 1.4f * dt);
                s.pp[k].x += s.pv[k].x * dt;
                s.pp[k].y += s.pv[k].y * dt;
            }
            if (s.life <= 0.0f) s.alive = false;
        }
    }

    void Draw(ImDrawList* dl, ImVec2 min, ImVec2 max, ImU32 color, float alphaScale = 1.0f) {
        if (!inited) {
            for (int i = 0; i < kMax; i++) Respawn(beams[i], min, max);
            inited = true;
        }
        const float floorY = max.y - 3.0f;
        dl->PushClipRect(min, max, true);
        const ImVec4 base = U32ToVec4(color);

        // ---- 光束：外层辉光 + 亮芯 + 亮头；四色循环（与 WEB 预览一致） ----
        static const ImU32 kBeamTints[4] = {
            IM_COL32(139, 124, 255, 255),   // 紫（主色）
            IM_COL32(90, 156, 255, 255),    // 蓝
            IM_COL32(192, 132, 252, 255),   // 粉
            IM_COL32(58, 214, 126, 255)     // 绿
        };
        for (int i = 0; i < kMax; i++) {
            Beam& b = beams[i];
            if (!b.hit && b.y >= floorY) {
                b.hit = true;
                SpawnSplash(b.x, floorY);
            }
            if (b.y - b.len > floorY) { Respawn(b, min, max); continue; }
            float fade = 1.0f;
            if (b.y < min.y + 40.0f) fade = (b.y - (min.y - b.len)) / (b.len + 40.0f);
            if (fade < 0.0f) fade = 0.0f;
            if (fade > 1.0f) fade = 1.0f;
            const float y0 = (b.y < floorY) ? b.y : floorY;
            const float y1 = b.y - b.len;

            const ImU32 cu = (i % 4 == 0) ? color : kBeamTints[i % 4];
            const ImVec4 cb = U32ToVec4(cu);

            // 外层辉光（宽而淡，模拟 box-shadow 的柔光）
            {
                ImVec4 g1 = cb; g1.w = 0.18f * fade * alphaScale;
                dl->AddLine(ImVec2(b.x, y1 + b.len * 0.12f), ImVec2(b.x, y0),
                            ImGui::ColorConvertFloat4ToU32(g1), 5.0f);
                ImVec4 g2 = cb; g2.w = 0.09f * fade * alphaScale;
                dl->AddLine(ImVec2(b.x, y1), ImVec2(b.x, y0),
                            ImGui::ColorConvertFloat4ToU32(g2), 10.0f);
            }
            // 亮芯：尾部透明 → 头部最亮
            const int seg = 12;
            for (int s = 0; s < seg; s++) {
                const float t0 = (float)s / seg, t1 = (float)(s + 1) / seg;
                const ImVec2 p0(b.x, y1 + (y0 - y1) * t0);
                const ImVec2 p1(b.x, y1 + (y0 - y1) * t1);
                ImVec4 cs = cb; cs.w = (0.10f + 0.88f * t0 * t0) * fade * alphaScale;
                dl->AddLine(p0, p1, ImGui::ColorConvertFloat4ToU32(cs), 2.0f);
            }
            // 亮头
            if (b.y < floorY) {
                ImVec4 hc = cb; hc.w = 0.90f * fade * alphaScale;
                dl->AddCircleFilled(ImVec2(b.x, b.y), 2.2f,
                                    ImGui::ColorConvertFloat4ToU32(hc), 8);
            }
        }

        // ---- 落地溅射：辉光 + 小方块颗粒 ----
        for (int i = 0; i < kSplash; i++) {
            if (!splashes[i].alive) continue;
            Splash& s = splashes[i];
            const float k = s.life / s.maxLife;
            const float age = 1.0f - k;
            for (int g = 3; g >= 1; g--) {
                const float hw = (8.0f + age * 14.0f) * (0.6f + 0.4f * g);
                const float hh = 2.6f * g;
                ImVec4 c = base; c.w = 0.32f * k / (float)g;
                dl->AddRectFilled(ImVec2(s.x - hw, s.y - hh), ImVec2(s.x + hw, s.y + hh),
                                  ImGui::ColorConvertFloat4ToU32(c), hh);
            }
            for (int q = 0; q < s.pn; q++) {
                const float pk = k * s.pk[q];
                if (pk <= 0.0f) continue;
                ImVec4 pc = base; pc.w = pk;
                const float sz = s.ps[q] * (0.5f + 0.5f * pk);
                dl->AddRectFilled(ImVec2(s.pp[q].x - sz, s.pp[q].y - sz),
                                  ImVec2(s.pp[q].x + sz, s.pp[q].y + sz),
                                  ImGui::ColorConvertFloat4ToU32(pc), 0.8f);
            }
        }
        dl->PopClipRect();
    }

private:
    struct Beam { float x = 0, y = 0, len = 0, speed = 0, life = 0, maxLife = 0; bool hit = false; };
    struct Splash {
        float x = 0, y = 0, life = 0, maxLife = 0;
        ImVec2 pv[10] = {}; ImVec2 pp[10] = {};
        float  ps[10] = {}; float  pk[10] = {};
        int    pn = 0;
        bool   alive = false;
    };
    static constexpr int kMax = 22;      // 与 WEB 预览的 26 束接近
    static constexpr int kSplash = 14;
    Beam beams[kMax] = {};
    Splash splashes[kSplash] = {};

    void Respawn(Beam& b, ImVec2 min, ImVec2 max) {
        b.x       = RndRange(min.x + 12, max.x - 12);
        b.len     = RndRange(90, 240);
        b.y       = min.y - b.len - RndRange(0, 420);
        b.speed   = RndRange(140, 320);
        b.maxLife = 1.0f;
        b.life    = 1.0f;
        b.hit     = false;
    }

    void SpawnSplash(float x, float y) {
        int idx = -1;
        for (int i = 0; i < kSplash; i++) if (!splashes[i].alive) { idx = i; break; }
        if (idx < 0) return;
        Splash& s = splashes[idx];
        s.x = x; s.y = y;
        s.maxLife = 0.8f; s.life = s.maxLife;
        s.alive = true;
        s.pn = 7 + (rand() % 3);
        for (int i = 0; i < s.pn; i++) {
            const float ang = -1.5708f + RndRange(-1.0f, 1.0f);
            const float spd = RndRange(40, 130);
            s.pv[i] = ImVec2(cosf(ang) * spd, sinf(ang) * spd);
            s.pp[i] = ImVec2(x + RndRange(-2, 2), y - RndRange(0, 4));
            s.ps[i] = RndRange(1.3f, 3.4f);
            s.pk[i] = RndRange(0.6f, 1.0f);
        }
    }

    bool inited = false;
};

// 粒子爆裂：在某点炸出一圈小方块颗粒，慢慢扩散消失
class ParticleBurst {
public:
    bool Active() const { return aliveCount > 0; }

    void Emit(ImVec2 origin, ImU32 color, int count = 40) {
        for (int n = 0; n < count && aliveCount < kMax; n++) {
            int idx = -1;
            for (int i = 0; i < kMax; i++) if (!parts[i].alive) { idx = i; break; }
            if (idx < 0) break;
            P& p = parts[idx];
            const float ang = RndRange(0.0f, 6.2831853f);
            const float spd = RndRange(60, 260);
            p.pos = origin;
            p.vel = ImVec2(cosf(ang) * spd, sinf(ang) * spd);
            p.maxLife = RndRange(0.4f, 0.9f);
            p.life = p.maxLife;
            p.size = RndRange(2.0f, 5.0f);
            p.col = color;
            p.alive = true;
            aliveCount++;
        }
    }

    void Update(float dt) {
        for (int i = 0; i < kMax; i++) {
            if (!parts[i].alive) continue;
            P& p = parts[i];
            p.vel.x *= (1.0f - 3.0f * dt);
            p.vel.y = p.vel.y * (1.0f - 3.0f * dt) + 120.0f * dt;
            p.pos.x += p.vel.x * dt;
            p.pos.y += p.vel.y * dt;
            p.life -= dt;
            if (p.life <= 0.0f) { p.alive = false; aliveCount--; }
        }
    }

    void Draw(ImDrawList* dl) {
        for (int i = 0; i < kMax; i++) {
            if (!parts[i].alive) continue;
            P& p = parts[i];
            const float k = p.life / p.maxLife;
            ImVec4 col = U32ToVec4(p.col);
            col.w = k;
            const float s = p.size * (0.5f + 0.5f * k);
            dl->AddRectFilled(ImVec2(p.pos.x - s, p.pos.y - s),
                              ImVec2(p.pos.x + s, p.pos.y + s),
                              ImGui::ColorConvertFloat4ToU32(col), 1.5f);
        }
    }

private:
    struct P { ImVec2 pos, vel; float life = 0, maxLife = 0, size = 0; ImU32 col = 0; bool alive = false; };
    static constexpr int kMax = 120;
    P parts[kMax] = {};
    int aliveCount = 0;
};

// 四角星 logo（仿UI 底部导航的“星火”图标）
inline void DrawSpark(ImDrawList* dl, ImVec2 c, float r, ImU32 col) {
    const float rin = r * 0.30f;
    ImVec2 pts[8];
    for (int i = 0; i < 8; i++) {
        const float ang = -1.5708f + i * 0.7853982f;
        const float rad = (i % 2 == 0) ? r : rin;
        pts[i] = ImVec2(c.x + cosf(ang) * rad, c.y + sinf(ang) * rad);
    }
    dl->AddConvexPolyFilled(pts, 8, col);
}

// 当前窗口的背景特效（点阵 + 光束雨）：供所有 ClientWindow 复用，
// 在 Begin() 之后、内容之前调用；beams 为每个窗口独立的 BeamRain 实例。
inline void WindowBackdrop(BeamRain& beams, const Palette& p, float dt) {
    ImDrawList* dl = ImGui::GetWindowDrawList();
    const ImVec2 wMin = ImGui::GetWindowPos();
    const ImVec2 wMax = ImVec2(wMin.x + ImGui::GetWindowWidth(), wMin.y + ImGui::GetWindowHeight());
    const float pad = ImGui::GetFontSize() * 0.45f;
    const ImVec2 min(wMin.x + pad, wMin.y + pad);
    const ImVec2 max(wMax.x - pad, wMax.y - pad);
    DrawDotGrid(dl, min, max, p.dotGrid, ImGui::GetFontSize() * 0.5f);
    beams.Update(dt);
    beams.Draw(dl, min, max, p.accent);
}

// ============================================================================
// 活动 Tab 灯条（仿UI BottomNav 的“顶灯”：弹簧跟随 + 向上光晕 + 向下光锥）
// 画在 TabBar 底边，指向当前选中的 Tab；bar 来自 imgui_internal 的 CurrentTabBar。
// 已处理 TabBar 横向滚动（FittingPolicyScroll）：目标位置 = 布局偏移 - 滚动偏移。
inline void TabGlow(ImDrawList* dl, ImGuiTabBar* bar, const Palette& p,
                    SpringState& spring, float dt) {
    if (!bar || bar->SelectedTabId == 0) return;
    ImGuiTabItem* tab = ImGui::TabBarFindTabByID(bar, bar->SelectedTabId);
    if (!tab) return;

    const float lampW = 34.0f, lampH = 6.0f;
    // 选中 Tab 的可视中心（Offset 为布局偏移，ScrollingAnim 为当前横向滚动量）
    const float targetX = bar->BarRect.Min.x + tab->Offset - bar->ScrollingAnim + tab->Width * 0.5f;
    // 夹紧到 TabBar 可视范围：选中 Tab 被滑出可视区时不至于把灯画到窗外
    const float minX = bar->BarRect.Min.x + lampW * 0.5f;
    const float maxX = bar->BarRect.Max.x - lampW * 0.5f;
    const float x = ImClamp(SpringTick(spring, targetX, dt, 260.0f, 26.0f), minX, maxX);

    const float lampY = bar->BarRect.Max.y + 2.0f;   // 灯条中心，贴在 TabBar 底边

    // 1) 向上柔和光晕：多层椭圆叠加
    for (int b = 8; b >= 1; b--) {
        const float rx = 10.0f + b * 5.0f;
        const float ry = 6.0f + b * 4.0f;
        ImVec4 bc = U32ToVec4(p.accent); bc.w = 0.045f * (9 - b) / 8.0f;
        dl->AddEllipseFilled(ImVec2(x, lampY - lampH * 0.5f), ImVec2(rx, ry),
                             ImGui::ColorConvertFloat4ToU32(bc), 0.0f, 24);
    }
    // 2) 向下光锥：重叠竖直渐变细带（上亮下淡、中亮侧淡）
    const int strips = 48;
    const float glowHalf = lampW * 0.9f;
    const float gTopY = lampY + 2.0f, gBotY = lampY + 40.0f;
    ImVec4 gBot = U32ToVec4(p.accent); gBot.w = 0.0f;
    const ImU32 gBotU = ImGui::ColorConvertFloat4ToU32(gBot);
    const float spacing = (2.0f * glowHalf) / strips;
    for (int s = 0; s < strips; s++) {
        const float fx = (s / (strips - 1.0f)) * 2.0f - 1.0f;
        const float bell = 1.0f - fx * fx;
        if (bell <= 0.0f) continue;
        const float sx = x + fx * glowHalf;
        const float sw = spacing * 5.0f;
        ImVec4 ct = U32ToVec4(p.accent); ct.w = 0.05f * bell;
        const ImU32 ctU = ImGui::ColorConvertFloat4ToU32(ct);
        dl->AddRectFilledMultiColor(ImVec2(sx - sw * 0.5f, gTopY), ImVec2(sx + sw * 0.5f, gBotY),
                                    ctU, ctU, gBotU, gBotU);
    }
    // 3) 灯条本体（亮实）
    dl->AddRectFilled(ImVec2(x - lampW * 0.5f, lampY - lampH * 0.5f),
                      ImVec2(x + lampW * 0.5f, lampY + lampH * 0.5f),
                      p.accent, lampH * 0.5f);
}

// 选中 Tab 的弹性高亮药丸（仿UI BottomNav 的 navIndic 弹簧指示器）
// 在 BeginTabBar 之后、TabItem 之前调用，画在 Tab 底层：淡紫圆角条跟随选中 Tab 弹性滑动。
// 注意：此刻 tab->Offset/Width/ScrollingAnim 还是上一帧布局值，对弹簧动画恰好合适——
// 目标位置每帧收敛到最新布局。
inline void TabPill(ImDrawList* dl, ImGuiTabBar* bar, const Palette& p,
                    SpringState& springX, SpringState& springW, float dt) {
    if (!bar || bar->SelectedTabId == 0) return;
    ImGuiTabItem* tab = ImGui::TabBarFindTabByID(bar, bar->SelectedTabId);
    if (!tab || tab->Width <= 0.0f) return;

    const float targetX = bar->BarRect.Min.x + tab->Offset - bar->ScrollingAnim + tab->Width * 0.5f;
    const float x = SpringTick(springX, targetX, dt, 300.0f, 30.0f);
    const float w = SpringTick(springW, tab->Width * 0.90f, dt, 300.0f, 30.0f);

    const float minY = bar->BarRect.Min.y + 7.0f;
    const float maxY = bar->BarRect.Max.y - 7.0f;
    const float hh = maxY - minY;
    ImVec4 col = U32ToVec4(p.accent); col.w = 0.13f;
    dl->AddRectFilled(ImVec2(x - w * 0.5f, minY), ImVec2(x + w * 0.5f, maxY),
                      ImGui::ColorConvertFloat4ToU32(col), hh * 0.5f);
    // 药丸顶部一条细亮边（玻璃反光）
    dl->AddLine(ImVec2(x - w * 0.5f + 10.0f, minY + 1.2f), ImVec2(x + w * 0.5f - 10.0f, minY + 1.2f),
                p.accent, 2.0f);
}

// 简易矢量小图标（画在底部导航单元里，照仿UI NavIcon）
inline void NavIcon(ImDrawList* dl, int kind, ImVec2 c, float s, ImU32 col, float th) {
    switch (kind % 6) {
    case 0: { // 网格四格
        const float g = s * 0.34f, o = s * 0.16f;
        for (int i = 0; i < 4; i++) {
            const ImVec2 a(c.x + (i % 2 ? o : -g - o), c.y + (i / 2 ? o : -g - o));
            dl->AddRect(a, ImVec2(a.x + g, a.y + g), col, 2.0f, 0, th);
        }
    } break;
    case 1: { // 滑块：两条线 + 旋钮
        dl->AddLine(ImVec2(c.x - s * 0.5f, c.y - s * 0.28f), ImVec2(c.x + s * 0.5f, c.y - s * 0.28f), col, th);
        dl->AddLine(ImVec2(c.x - s * 0.5f, c.y + s * 0.28f), ImVec2(c.x + s * 0.5f, c.y + s * 0.28f), col, th);
        dl->AddCircleFilled(ImVec2(c.x + s * 0.16f, c.y - s * 0.28f), th * 1.3f, col);
        dl->AddCircleFilled(ImVec2(c.x - s * 0.16f, c.y + s * 0.28f), th * 1.3f, col);
    } break;
    case 2: { // 调色：圆 + 三个小点
        dl->AddCircle(c, s * 0.46f, col, 16, th);
        dl->AddCircleFilled(ImVec2(c.x - s * 0.12f, c.y - s * 0.16f), th * 1.1f, col);
        dl->AddCircleFilled(ImVec2(c.x + s * 0.18f, c.y - s * 0.02f), th * 1.1f, col);
        dl->AddCircleFilled(ImVec2(c.x - s * 0.02f, c.y + s * 0.18f), th * 1.1f, col);
    } break;
    case 3: { // 星火（四角星）
        const float r = s * 0.5f, rin = s * 0.16f;
        ImVec2 pts[8];
        for (int i = 0; i < 8; i++) {
            const float ang = -1.5708f + i * 0.7853982f;
            const float rad = (i % 2 == 0) ? r : rin;
            pts[i] = ImVec2(c.x + cosf(ang) * rad, c.y + sinf(ang) * rad);
        }
        dl->AddConvexPolyFilled(pts, 8, col);
    } break;
    case 4: { // 地球：圆 + 横竖线
        dl->AddCircle(c, s * 0.5f, col, 16, th);
        dl->AddEllipse(c, ImVec2(s * 0.5f, s * 0.22f), col, 0.0f, 16, th);
        dl->AddLine(ImVec2(c.x, c.y - s * 0.5f), ImVec2(c.x, c.y + s * 0.5f), col, th);
    } break;
    default: { // 音符：双圆 + 符干
        dl->AddCircleFilled(ImVec2(c.x - s * 0.24f, c.y + s * 0.30f), s * 0.16f, col);
        dl->AddCircleFilled(ImVec2(c.x + s * 0.24f, c.y + s * 0.30f), s * 0.16f, col);
        dl->AddLine(ImVec2(c.x - s * 0.24f, c.y + s * 0.14f), ImVec2(c.x - s * 0.24f, c.y - s * 0.40f), col, th);
        dl->AddLine(ImVec2(c.x + s * 0.24f, c.y + s * 0.14f), ImVec2(c.x + s * 0.24f, c.y - s * 0.40f), col, th);
        dl->AddLine(ImVec2(c.x - s * 0.24f, c.y - s * 0.40f), ImVec2(c.x + s * 0.06f, c.y - s * 0.30f), col, th);
    } break;
    }
}

// ============================================================================
// 玻璃药丸 Tab 导航（仿UI BottomNav）：玻璃胶囊 + 弹性选中指示 + 可选灯条
// 用于主菜单顶部导航，也用于内容区里的嵌套子 Tab（子 Tab 关灯、字号更小）。
// 标签多放不下时自动横向滚动：手指在导航上横向滑动（后端转成 MouseWheelH）。
// 返回本帧被点击的单元索引（未点击返回 -1）；current 为当前选中索引。
// 参数：scrollX 滚动偏移（调用方持久化）；showLamp 是否画灯条；lampTop=true 灯贴顶边
//（仿UI 原版），false 灯贴底边（适合放在窗口顶部的导航）；labelScale 文字字号系数。
// ============================================================================
inline int PillTabs(ImDrawList* dl, ImVec2 min, ImVec2 max, const Palette& p,
                    const char* const* labels, int count, int current,
                    SpringState& springX, SpringState& springW, float dt,
                    float& scrollX, bool showLamp = true, bool lampTop = false,
                    float labelScale = 0.78f) {
    if (count <= 0) return -1;
    const float fs = ImGui::GetFontSize();
    const float availW = max.x - min.x;
    const float labelSz = fs * labelScale;
    const float cellPad = fs * 0.80f;

    // ---- 每个单元按自身标签取自然宽度（预览是 inline-flex：胶囊只包裹内容） ----
    const int n = count > 16 ? 16 : count;
    float cellW[16];
    float totalW = 0.0f;
    for (int i = 0; i < n; ++i) {
        const float lw = ImGui::GetFont()->CalcTextSizeA(labelSz, FLT_MAX, 0.0f, labels[i]).x;
        cellW[i] = lw + cellPad * 2.0f;
        totalW += cellW[i];
    }

    // ---- 胶囊底：仅包裹内容；放不下才铺满并横向滚动 ----
    const bool overflow = (totalW > availW);
    const float pillW = overflow ? availW : totalW;
    const ImVec2 pillMax(min.x + pillW, max.y);
    const float rounding = (max.y - min.y) * 0.5f;
    GlassPill(dl, min, pillMax, p, rounding);

    const float maxScroll = totalW - pillW;
    if (overflow && ImGui::IsMouseHoveringRect(min, pillMax, false))
        scrollX -= ImGui::GetIO().MouseWheelH * fs * 2.5f;
    scrollX = scrollX < 0.0f ? 0.0f : (scrollX > maxScroll ? maxScroll : scrollX);
    if (!overflow) scrollX = 0.0f;
    const float offsetX = min.x - scrollX;

    // ---- 选中药丸：实心渐变胶囊 + 描边（预览的活动项样式），弹簧跟随 ----
    const int cur = (current >= 0 && current < n) ? current : 0;
    float cellOffset = 0.0f;
    for (int i = 0; i < cur; ++i) cellOffset += cellW[i];
    const float targetX = offsetX + cellOffset + cellW[cur] * 0.5f;
    const float x = SpringTick(springX, targetX, dt, 300.0f, 30.0f);
    const float w = SpringTick(springW, cellW[cur] * 0.92f, dt, 300.0f, 30.0f);
    const float ix = x < min.x ? min.x : (x > pillMax.x ? pillMax.x : x);
    const float ins = fs * 0.22f;
    const float pilMinY = min.y + ins, pilMaxY = max.y - ins;
    const ImVec2 pMin(ix - w * 0.5f, pilMinY), pMax(ix + w * 0.5f, pilMaxY);
    const float pRound = (pilMaxY - pilMinY) * 0.5f;
    {
        // 注意：AddRectFilledMultiColor 没有圆角参数（会画成方角），
        // 这里必须用 AddRectFilled 才能得到预览那样的圆角药丸。
        ImVec4 cA = U32ToVec4(p.accent); cA.w = 0.55f;
        dl->AddRectFilled(pMin, pMax, ImGui::ColorConvertFloat4ToU32(cA), pRound);
        // 上半部提亮（玻璃质感）
        ImVec4 cHi = U32ToVec4(p.accentHover); cHi.w = 0.22f;
        dl->AddRectFilled(ImVec2(pMin.x + 1.0f, pMin.y + 1.0f),
                          ImVec2(pMax.x - 1.0f, (pMin.y + pMax.y) * 0.5f),
                          ImGui::ColorConvertFloat4ToU32(cHi), pRound * 0.9f);
        dl->AddRect(pMin, pMax, p.accent, pRound, 0, 1.2f);
    }

    // ---- 灯条 + 光晕 + 光锥（贴胶囊底边，向上洒入） ----
    if (showLamp) {
        const float lampY = lampTop ? min.y - 2.0f : max.y + 2.0f;
        const float lampW = fs * 0.55f, lampH = fs * 0.12f;
        for (int b = 8; b >= 1; b--) {
            const float rx = 8.0f + b * 4.5f;
            const float ry = 5.0f + b * 3.4f;
            ImVec4 bc = U32ToVec4(p.accent); bc.w = 0.05f * (9 - b) / 8.0f;
            dl->AddEllipseFilled(ImVec2(ix, lampY), ImVec2(rx, ry), ImGui::ColorConvertFloat4ToU32(bc), 0.0f, 24);
        }
        const int strips = 48;
        const float glowHalf = lampW * 0.9f;
        const float gTopY = lampTop ? min.y + 1.0f : max.y - fs * 0.5f;
        const float gBotY = lampTop ? min.y + fs * 0.5f : max.y - 1.0f;
        ImVec4 gBot = U32ToVec4(p.accent); gBot.w = 0.0f;
        const ImU32 gBotU = ImGui::ColorConvertFloat4ToU32(gBot);
        const float spacing = (2.0f * glowHalf) / strips;
        for (int s = 0; s < strips; s++) {
            const float fx = (s / (strips - 1.0f)) * 2.0f - 1.0f;
            const float bell = 1.0f - fx * fx;
            if (bell <= 0.0f) continue;
            const float sx = ix + fx * glowHalf;
            const float sw = spacing * 5.0f;
            ImVec4 ct = U32ToVec4(p.accent); ct.w = 0.06f * bell;
            const ImU32 ctU = ImGui::ColorConvertFloat4ToU32(ct);
            dl->AddRectFilledMultiColor(ImVec2(sx - sw * 0.5f, gTopY), ImVec2(sx + sw * 0.5f, gBotY),
                                        ctU, ctU, gBotU, gBotU);
        }
        dl->AddRectFilled(ImVec2(ix - lampW * 0.5f, lampY - lampH * 0.5f),
                          ImVec2(ix + lampW * 0.5f, lampY + lampH * 0.5f),
                          p.accent, lampH * 0.5f);
    }

    // ---- 各单元：文字居中 + 命中区（活动项白色加粗感，非活动项暗灰） ----
    int clicked = -1;
    dl->PushClipRect(min, pillMax, true);
    float cx0 = offsetX;
    for (int i = 0; i < n; ++i) {
        const float cw = cellW[i];
        const float cxm = cx0 + cw * 0.5f;
        const bool sel2 = (i == cur);
        const ImU32 tc = sel2 ? IM_COL32(255, 255, 255, 255) : p.textDim;

        const ImVec2 ls = ImGui::GetFont()->CalcTextSizeA(labelSz, FLT_MAX, 0.0f, labels[i]);
        const float cy = (min.y + max.y) * 0.5f;
        dl->AddText(ImGui::GetFont(), labelSz, ImVec2(cxm - ls.x * 0.5f, cy - ls.y * 0.5f), tc, labels[i]);

        if (cx0 + cw >= min.x && cx0 <= pillMax.x) {
            ImGui::SetCursorScreenPos(ImVec2(cx0, min.y));
            ImGui::PushID(i);
            if (ImGui::InvisibleButton("nav", ImVec2(cw, max.y - min.y))) clicked = i;
            ImGui::PopID();
        }
        cx0 += cw;
    }
    dl->PopClipRect();
    return clicked;
}

// ============================================================================
// 签名控件（抄自 仿UI ui.cpp）
// ============================================================================

// 自定义开关：药丸 + 白色圆钮 + 弹性滑动
inline bool ToggleSwitch(const char* id, bool* v, const Palette& p) {
    ImGui::PushID(id);
    const ImVec2 pos = ImGui::GetCursorScreenPos();
    const float h = 26.0f, w = 46.0f, r = h * 0.5f;
    ImGui::InvisibleButton("t", ImVec2(w, h));
    bool changed = false;
    if (ImGui::IsItemClicked()) { *v = !*v; changed = true; }
    ImDrawList* dl = ImGui::GetWindowDrawList();
    ImGuiStorage* st = ImGui::GetStateStorage();
    const ImGuiID kk = ImGui::GetID("k");
    float k = st->GetFloat(kk, *v ? 1.0f : 0.0f);
    k = Approach(k, *v ? 1.0f : 0.0f, ImGui::GetIO().DeltaTime, 16.0f);
    st->SetFloat(kk, k);
    const ImU32 track = ImGui::ColorConvertFloat4ToU32(ImLerp(
        U32ToVec4(p.track), U32ToVec4(p.accent), k));
    dl->AddRectFilled(pos, ImVec2(pos.x + w, pos.y + h), track, r);
    const float cx = pos.x + r + (w - h) * k;
    dl->AddCircleFilled(ImVec2(cx, pos.y + r), r - 3.0f, IM_COL32_WHITE);
    ImGui::PopID();
    return changed;
}

// 一行：左标签 + 右侧自定义开关
inline void RowToggle(const char* label, bool* v, const Palette& p) {
    ImGui::AlignTextToFramePadding();
    ImGui::TextUnformatted(label);
    ImGui::SameLine();
    const float avail = ImGui::GetContentRegionAvail().x;
    ImGui::SetCursorPosX(ImGui::GetCursorPosX() + avail - 46.0f);
    ToggleSwitch(label, v, p);
}

// 勾选按钮：圆角矩形色块 + 3D 翻转反馈（开 = 紫底白勾；关 = 灰底灰横杠）
inline bool CheckChip(const char* id, bool* v, const Palette& p) {
    ImGui::PushID(id);
    const ImVec2 pos = ImGui::GetCursorScreenPos();
    const float w = 40.0f, h = 26.0f, r = 9.0f;
    ImGui::InvisibleButton("c", ImVec2(w, h));
    bool changed = false;
    if (ImGui::IsItemClicked()) { *v = !*v; changed = true; }
    ImDrawList* dl = ImGui::GetWindowDrawList();
    const float dt = ImGui::GetIO().DeltaTime;

    ImGuiStorage* stg = ImGui::GetStateStorage();
    const ImGuiID phID   = ImGui::GetID("ph");    // 翻转相位 0..1，<0 表示静止
    const ImGuiID dispID = ImGui::GetID("disp");  // 当前显示的状态(0/1)
    const float target = *v ? 1.0f : 0.0f;
    float ph   = stg->GetFloat(phID, -1.0f);
    float disp = stg->GetFloat(dispID, target);

    if (changed) ph = 0.0f;
    else if (ph < 0.0f && disp != target) ph = 0.0f;
    if (ph >= 0.0f) {
        ph += dt / 0.20f;
        if (ph >= 0.5f) disp = target;
        if (ph >= 1.0f) { ph = -1.0f; disp = target; }
    }
    stg->SetFloat(phID, ph);
    stg->SetFloat(dispID, disp);

    float scaleY = (ph < 0.0f) ? 1.0f : fabsf(cosf(ph * 3.14159265f));
    if (scaleY < 0.04f) scaleY = 0.04f;
    const bool on = disp > 0.5f;

    const ImVec2 c((pos.x + pos.x + w) * 0.5f, (pos.y + pos.y + h) * 0.5f);
    const float hh = (h * 0.5f) * scaleY;
    const ImVec2 mn(pos.x, c.y - hh), mx(pos.x + w, c.y + hh);

    const float shade = 0.55f + 0.45f * scaleY;
    ImVec4 baseCol = on ? U32ToVec4(p.accent) : U32ToVec4(p.track);
    baseCol.x *= shade; baseCol.y *= shade; baseCol.z *= shade;
    float rr = r * scaleY; if (rr < 3.0f) rr = 3.0f;
    dl->AddRectFilled(mn, mx, ImGui::ColorConvertFloat4ToU32(baseCol), rr);
    ImVec4 hl(1, 1, 1, 0.16f * scaleY * (on ? 1.2f : 0.6f));
    dl->AddLine(ImVec2(mn.x + rr, mn.y + 1.5f), ImVec2(mx.x - rr, mn.y + 1.5f),
                ImGui::ColorConvertFloat4ToU32(hl), 1.0f);

    if (scaleY > 0.5f) {
        const float ca = (scaleY - 0.5f) * 2.0f;
        if (on) {
            ImVec4 wc(1, 1, 1, ca);
            const ImU32 wu = ImGui::ColorConvertFloat4ToU32(wc);
            dl->AddLine(ImVec2(c.x - 5, c.y + 0.5f), ImVec2(c.x - 1.5f, c.y + 4), wu, 2.0f);
            dl->AddLine(ImVec2(c.x - 1.5f, c.y + 4), ImVec2(c.x + 5.5f, c.y - 4), wu, 2.0f);
        } else {
            ImVec4 dc = U32ToVec4(p.textDim); dc.w *= ca;
            dl->AddLine(ImVec2(c.x - 5, c.y), ImVec2(c.x + 5, c.y),
                        ImGui::ColorConvertFloat4ToU32(dc), 2.0f);
        }
    }
    ImGui::PopID();
    return changed;
}

// 一行：左标签 + 右侧勾选按钮
inline void RowCheck(const char* label, bool* v, const Palette& p) {
    ImGui::AlignTextToFramePadding();
    ImGui::TextUnformatted(label);
    ImGui::SameLine();
    const float avail = ImGui::GetContentRegionAvail().x;
    ImGui::SetCursorPosX(ImGui::GetCursorPosX() + avail - 40.0f);
    CheckChip(label, v, p);
}

// 自定义滑块：细轨道（左紫右灰）+ 白色圆钮 + 跟随的数值气泡
inline bool FancySlider(const char* id, float* v, float vmin, float vmax,
                        const char* vfmt, const Palette& p, float dispScale = 1.0f) {
    ImGui::PushID(id);
    const float w = ImGui::GetContentRegionAvail().x;
    const float h = 44.0f;
    const ImVec2 pos = ImGui::GetCursorScreenPos();
    ImGui::InvisibleButton("s", ImVec2(w, h));
    const bool active = ImGui::IsItemActive();
    const float trackY = pos.y + h - 10.0f;
    const float x0 = pos.x + 10.0f, x1 = pos.x + w - 10.0f;
    float t = (*v - vmin) / (vmax - vmin);

    bool changed = false;
    if (active) {
        const float mx = ImGui::GetIO().MousePos.x;
        float nt = (mx - x0) / (x1 - x0);
        nt = nt < 0 ? 0 : (nt > 1 ? 1 : nt);
        const float nv = vmin + nt * (vmax - vmin);
        if (nv != *v) { *v = nv; changed = true; }
        t = nt;
    }
    ImDrawList* dl = ImGui::GetWindowDrawList();
    const bool hovered = ImGui::IsItemHovered();
    ImGuiStorage* stg = ImGui::GetStateStorage();
    const float dt = ImGui::GetIO().DeltaTime;

    const ImGuiID dtk = ImGui::GetID("dispT");
    float dispT = stg->GetFloat(dtk, t);
    dispT = Approach(dispT, t, dt, 16.0f);
    if (fabsf(dispT - t) < 0.0005f) dispT = t;
    stg->SetFloat(dtk, dispT);
    const float knobX = x0 + dispT * (x1 - x0);

    const ImGuiID gk = ImGui::GetID("grow");
    float grow = stg->GetFloat(gk, 0.0f);
    const float gTarget = active ? 1.0f : (hovered ? 0.55f : 0.0f);
    grow = Approach(grow, gTarget, dt, 18.0f);
    stg->SetFloat(gk, grow);
    const float knobR = 9.0f + grow * 2.0f;
    const float ringT = 1.5f + grow * 1.0f;

    dl->AddLine(ImVec2(x0, trackY), ImVec2(x1, trackY), p.track, 3.0f);
    dl->AddLine(ImVec2(x0, trackY), ImVec2(knobX, trackY), p.accent, 3.0f);
    if (grow > 0.01f) {
        ImVec4 halo = U32ToVec4(p.accent); halo.w = 0.15f * grow;
        dl->AddCircleFilled(ImVec2(knobX, trackY), knobR + 4.0f * grow,
                            ImGui::ColorConvertFloat4ToU32(halo), 24);
    }
    ImVec4 sh(0, 0, 0, 0.18f);
    dl->AddCircleFilled(ImVec2(knobX, trackY + 1.5f), knobR + 1.0f,
                        ImGui::ColorConvertFloat4ToU32(sh), 24);
    dl->AddCircleFilled(ImVec2(knobX, trackY), knobR, IM_COL32_WHITE, 24);
    dl->AddCircle(ImVec2(knobX, trackY), knobR, p.accent, 24, ringT);

    char buf[24]; snprintf(buf, sizeof(buf), vfmt, (*v) * dispScale);
    const ImVec2 ts = ImGui::CalcTextSize(buf);
    const float bw = ts.x + 18.0f, bh = ts.y + 8.0f;
    float bx = knobX - bw * 0.5f;
    if (bx < pos.x) bx = pos.x;
    if (bx + bw > pos.x + w) bx = pos.x + w - bw;
    const float by = trackY - 16.0f - bh;
    dl->AddRectFilled(ImVec2(bx, by), ImVec2(bx + bw, by + bh), p.accentSoft, bh * 0.5f);
    dl->AddText(ImVec2(bx + 9.0f, by + 4.0f), p.accent, buf);

    ImGui::PopID();
    return changed;
}

// ============================================================================
// 侧栏矢量图标（新 UI 侧栏导航用，全部用 ImDrawList 图元手绘）
// kind：0 用户 / 1 地球 / 2 准星 / 3 鱼 / 4 盒子 / 5 星火 / 6 WiFi / 7 齿轮
//      8 眼睛 / 9 搜索 / 10 日志 / 11 脚本 / 12 方块(Mod) / 13 右箭头
// ============================================================================
inline void SideIcon(ImDrawList* dl, int kind, ImVec2 c, float s, ImU32 col, float th) {
    const float r = s * 0.5f;
    switch (kind % 14) {
    case 0: { // 用户：头 + 肩
        dl->AddCircle(ImVec2(c.x, c.y - r * 0.36f), r * 0.30f, col, 14, th);
        ImVec2 pts[9];
        for (int i = 0; i < 9; ++i) {
            const float t = (float)i / 8.0f;
            const float a = 3.14159265f * t;              // 0..pi：从左到右
            pts[i] = ImVec2(c.x + (cosf(a) * 2.0f - 1.0f) * r * 0.52f,
                            c.y + sinf(a) * r * 0.32f + r * 0.12f);
        }
        dl->AddPolyline(pts, 9, col, 0, th);
    } break;
    case 1: { // 地球
        dl->AddCircle(c, r * 0.52f, col, 18, th);
        dl->AddLine(ImVec2(c.x - r * 0.52f, c.y), ImVec2(c.x + r * 0.52f, c.y), col, th);
        dl->AddEllipse(c, ImVec2(r * 0.52f, r * 0.24f), col, 0.0f, 18, th);
    } break;
    case 2: { // 准星
        dl->AddCircle(c, r * 0.50f, col, 18, th);
        dl->AddCircle(c, r * 0.30f, col, 14, th);
        dl->AddCircleFilled(c, r * 0.08f, col, 10);
    } break;
    case 3: { // 鱼
        dl->AddEllipse(ImVec2(c.x - r * 0.08f, c.y), ImVec2(r * 0.42f, r * 0.26f), col, 0.0f, 20, th);
        dl->AddTriangle(ImVec2(c.x + r * 0.20f, c.y - r * 0.16f), ImVec2(c.x + r * 0.48f, c.y),
                        ImVec2(c.x + r * 0.20f, c.y + r * 0.16f), col, th);
        dl->AddCircleFilled(ImVec2(c.x - r * 0.26f, c.y - r * 0.06f), r * 0.06f, col, 8);
    } break;
    case 4: { // 盒子
        const float hw = r * 0.55f, hh = r * 0.40f;
        dl->AddRect(ImVec2(c.x - hw, c.y - hh), ImVec2(c.x + hw, c.y + hh), col, r * 0.12f, 0, th);
        dl->AddLine(ImVec2(c.x, c.y - hh), ImVec2(c.x, c.y + hh), col, th);
        dl->AddLine(ImVec2(c.x - hw, c.y), ImVec2(c.x + hw, c.y), col, th);
    } break;
    case 5: { // 星火（复用 logo 四角星）
        DrawSpark(dl, c, s * 0.52f, col);
    } break;
    case 6: { // WiFi
        auto arc = [&](float yc, float rad) {
            ImVec2 pts[12];
            for (int i = 0; i < 12; ++i) {
                const float a = 3.14159265f * 0.25f + 3.14159265f * 0.5f * ((float)i / 11.0f);
                pts[i] = ImVec2(c.x + cosf(a) * rad, c.y + yc + sinf(a) * rad);
            }
            dl->AddPolyline(pts, 12, col, 0, th);
        };
        arc(r * 0.22f, r * 0.58f);
        arc(-r * 0.02f, r * 0.42f);
        arc(-r * 0.24f, r * 0.26f);
        dl->AddCircleFilled(ImVec2(c.x, c.y - r * 0.36f), r * 0.07f, col, 8);
    } break;
    case 7: { // 齿轮
        dl->AddCircle(c, r * 0.40f, col, 20, th);
        for (int i = 0; i < 8; ++i) {
            const float a = (float)i * 0.7853982f;
            dl->AddLine(ImVec2(c.x + cosf(a) * r * 0.15f, c.y + sinf(a) * r * 0.15f),
                        ImVec2(c.x + cosf(a) * r * 0.40f, c.y + sinf(a) * r * 0.40f), col, th);
        }
        dl->AddCircleFilled(c, r * 0.09f, col, 10);
    } break;
    case 8: { // 眼睛
        ImVec2 top[13], bot[13];
        for (int i = 0; i < 13; ++i) {
            const float a = 3.14159265f * ((float)i / 12.0f);
            top[i] = ImVec2(c.x + cosf(a) * r * 0.52f, c.y - sinf(a) * r * 0.34f + r * 0.02f);
            bot[i] = ImVec2(c.x + cosf(a) * r * 0.52f, c.y + sinf(a) * r * 0.34f + r * 0.02f);
        }
        dl->AddPolyline(top, 13, col, 0, th);
        dl->AddPolyline(bot, 13, col, 0, th);
        dl->AddCircleFilled(ImVec2(c.x, c.y + r * 0.02f), r * 0.12f, col, 10);
    } break;
    case 9: { // 搜索
        dl->AddCircle(ImVec2(c.x - r * 0.10f, c.y - r * 0.10f), r * 0.38f, col, 16, th);
        dl->AddLine(ImVec2(c.x + r * 0.18f, c.y + r * 0.18f), ImVec2(c.x + r * 0.50f, c.y + r * 0.50f), col, th);
    } break;
    case 10: { // 日志
        dl->AddRect(ImVec2(c.x - r * 0.55f, c.y - r * 0.40f), ImVec2(c.x + r * 0.55f, c.y + r * 0.40f), col, r * 0.13f, 0, th);
        dl->AddLine(ImVec2(c.x - r * 0.30f, c.y - r * 0.16f), ImVec2(c.x + r * 0.30f, c.y - r * 0.16f), col, th);
        dl->AddLine(ImVec2(c.x - r * 0.30f, c.y), ImVec2(c.x + r * 0.30f, c.y), col, th);
        dl->AddLine(ImVec2(c.x - r * 0.30f, c.y + r * 0.16f), ImVec2(c.x + r * 0.30f, c.y + r * 0.16f), col, th);
    } break;
    case 11: { // 脚本（折角）
        dl->AddRect(ImVec2(c.x - r * 0.55f, c.y - r * 0.40f), ImVec2(c.x + r * 0.55f, c.y + r * 0.40f), col, r * 0.13f, 0, th);
        dl->AddLine(ImVec2(c.x - r * 0.26f, c.y - r * 0.40f), ImVec2(c.x - r * 0.26f, c.y - r * 0.12f), col, th);
        dl->AddLine(ImVec2(c.x - r * 0.26f, c.y - r * 0.12f), ImVec2(c.x + 0.0f, c.y - r * 0.12f), col, th);
        dl->AddLine(ImVec2(c.x - r * 0.30f, c.y), ImVec2(c.x + r * 0.30f, c.y), col, th);
        dl->AddLine(ImVec2(c.x - r * 0.30f, c.y + r * 0.18f), ImVec2(c.x + r * 0.30f, c.y + r * 0.18f), col, th);
    } break;
    case 12: { // 2x2 方块（Mod）
        const float o = r * 0.12f, g = r * 0.32f;
        for (int i = 0; i < 4; ++i) {
            const float ox = (i % 2) ? o : -g - o;
            const float oy = (i / 2) ? o : -g - o;
            dl->AddRect(ImVec2(c.x + ox, c.y + oy), ImVec2(c.x + ox + g, c.y + oy + g), col, r * 0.09f, 0, th);
        }
    } break;
    default: { // 右箭头
        dl->AddLine(ImVec2(c.x - r * 0.20f, c.y - r * 0.30f), ImVec2(c.x + r * 0.20f, c.y), col, th);
        dl->AddLine(ImVec2(c.x + r * 0.20f, c.y), ImVec2(c.x - r * 0.20f, c.y + r * 0.30f), col, th);
    } break;
    }
}

// ============================================================================
// 漂浮光点（萤火虫粒子）：从底部上浮 + 左右摇摆 + 淡入淡出。背景动态增强。
// ============================================================================
class FloatParticles {
public:
    void Update(float dt) {
        t += dt;
        for (int i = 0; i < kMax; ++i) {
            P& p = parts[i];
            if (p.maxLife <= 0.0f) { Respawn(p); continue; }
            p.life += dt;
            if (p.life >= p.maxLife || p.pos.y < mMin.y - 30.0f) { Respawn(p); continue; }
            p.pos.y -= p.speed * dt;
            p.pos.x += sinf(t * p.wobble + p.phase) * 18.0f * dt;
        }
    }

    void Draw(ImDrawList* dl, ImVec2 min, ImVec2 max, float alphaScale = 1.0f) {
        mMin = min; mMax = max;
        dl->PushClipRect(min, max, true);
        for (int i = 0; i < kMax; ++i) {
            const P& p = parts[i];
            if (p.maxLife <= 0.0f || p.life >= p.maxLife) continue;
            const float k = 1.0f - p.life / p.maxLife;   // 0..1 上升进度
            float a = 1.0f;
            if (k < 0.12f) a = k / 0.12f;
            else if (k > 0.88f) a = (1.0f - k) / 0.12f;
            if (a <= 0.01f) continue;
            ImVec4 col = U32ToVec4(p.col);
            col.w = a * p.alpha * alphaScale;
            const float rad = p.size * (0.6f + 0.4f * k);
            ImVec4 halo = col; halo.w *= 0.35f;
            dl->AddCircleFilled(p.pos, rad * 2.4f, ImGui::ColorConvertFloat4ToU32(halo), 10);
            dl->AddCircleFilled(p.pos, rad, ImGui::ColorConvertFloat4ToU32(col), 12);
        }
        dl->PopClipRect();
    }

private:
    struct P {
        ImVec2 pos; float speed = 0, size = 0, phase = 0, wobble = 0;
        float life = 0, maxLife = 0, alpha = 0; ImU32 col = 0;
    };
    static constexpr int kMax = 22;
    P parts[kMax];
    ImVec2 mMin, mMax;
    float t = 0;

    void Respawn(P& p) {
        if (mMax.y <= mMin.y) { p.maxLife = 0.0f; return; }
        p.pos = ImVec2(RndRange(mMin.x, mMax.x), mMax.y + RndRange(0.0f, 60.0f));
        p.speed = RndRange(24.0f, 70.0f);
        p.size = RndRange(1.6f, 4.2f);
        p.phase = RndRange(0.0f, 6.2831853f);
        p.wobble = RndRange(0.4f, 1.4f);
        p.maxLife = (mMax.y - mMin.y + 60.0f) / p.speed + RndRange(0.0f, 2.0f);
        p.life = 0.0f;
        p.alpha = RndRange(0.45f, 0.85f);
        static const ImU32 cols[] = {
            IM_COL32(139, 124, 255, 255), IM_COL32(90, 156, 255, 255),
            IM_COL32(192, 132, 252, 255), IM_COL32(255, 255, 255, 255),
            IM_COL32(58, 214, 126, 255)
        };
        p.col = cols[rand() % 5];
    }
};

// ============================================================================
// 闪烁星点：随机散布的静止光点，各自脉动。
// ============================================================================
class TwinkleStars {
public:
    void Update(float dt) { t += dt; }

    void Draw(ImDrawList* dl, ImVec2 min, ImVec2 max) {
        if (!inited) { Init(min, max); inited = true; }
        dl->PushClipRect(min, max, true);
        for (const S& s : stars) {
            const float a = 0.10f + 0.70f * (0.5f + 0.5f * sinf(s.phase + t * s.speed));
            if (a < 0.02f) continue;
            const ImU32 u = ImGui::ColorConvertFloat4ToU32(ImVec4(1, 1, 1, a));
            dl->AddCircleFilled(s.pos, s.size, u, 8);
            dl->AddCircleFilled(s.pos, s.size * 3.0f,
                ImGui::ColorConvertFloat4ToU32(ImVec4(0.55f, 0.49f, 1.0f, a * 0.30f)), 10);
        }
        dl->PopClipRect();
    }

private:
    struct S { ImVec2 pos; float size = 0, phase = 0, speed = 0; };
    S stars[18];
    float t = 0;
    bool inited = false;

    void Init(ImVec2 min, ImVec2 max) {
        for (S& s : stars) {
            s.pos = ImVec2(RndRange(min.x, max.x), RndRange(min.y, max.y));
            s.size = RndRange(1.0f, 2.2f);
            s.phase = RndRange(0.0f, 6.2831853f);
            s.speed = RndRange(0.6f, 2.2f);
        }
    }
};

// ============================================================================
// 顶部柔光（预览的顶部光晕）：两道上亮下透明的宽渐变带，缓慢漂移 + 呼吸。
// 画在窗口背景层、内容之下；刻意保持柔和，不做抢眼的斜条。
// ============================================================================
class AuroraWave {
public:
    void Update(float dt) { t += dt; }

    void Draw(ImDrawList* dl, ImVec2 wMin, ImVec2 wMax, const Palette& p,
              float alphaScale = 1.0f, float cornerRad = 0.0f) {
        (void)p;
        dl->PushClipRect(wMin, wMax, true);
        const float W = wMax.x - wMin.x;
        const float H = wMax.y - wMin.y;

        // ---- 1) 顶部柔光：紫 + 蓝两道宽渐变带（加强版） ----
        // 覆盖策略（修复"最左/最右边缘没有极光"）：
        //   圆角线以下 → 铺满整宽，左右两侧也有极光；
        //   圆角线以上 → 只画中间段，避开左右圆角、不溢出。
        for (int b = 0; b < 2; ++b) {
            const float phase = (b == 0) ? 0.0f : 2.1f;
            const float breathe = 0.62f + 0.38f * sinf(t * 0.28f + phase);
            const float hgt = H * ((b == 0) ? 0.38f : 0.28f);
            const int al = (int)ImClamp((b == 0 ? 150.0f : 105.0f) * breathe * alphaScale, 0.0f, 255.0f);
            if (al <= 1) continue;
            const float r = ImClamp(cornerRad, 0.0f, hgt * 0.5f);
            const int cR = (b == 0) ? 139 : 90;
            const int cG = (b == 0) ? 124 : 156;
            const int cB = 255;
            // 一次画完：带圆角（与窗口同半径）+ 纵向渐变（顶部 al → 底部 0）
            // → 无接缝、不溢出圆角、左右两侧全覆盖
            AddRoundedGradientRect(dl, ImVec2(wMin.x, wMin.y), ImVec2(wMax.x, wMin.y + hgt),
                                   r, ImDrawFlags_RoundCornersTop,
                                   IM_COL32(cR, cG, cB, al), IM_COL32(cR, cG, cB, 0));
        }

        // ---- 2) 极光帘：4 条竖直柔光带（左右透明 + 向下渐隐），横向缓慢漂移（加强版）
        const int slices = 6;
        for (int i = 0; i < 4; ++i) {
            const float ph = (float)i * 2.1f;
            const float cx = wMin.x + W * (0.18f + 0.21f * (float)i)
                             + sinf(t * 0.15f + ph) * W * 0.10f;
            const float halfW = W * (0.17f + 0.04f * (float)i);
            const float curtainH = H * 0.52f;
            const int cr = (i == 1) ? 90 : 139;
            const int cg = (i == 1) ? 156 : 124;
            const int cb = 255;
            const float baseA = ((i == 1) ? 62.0f : 78.0f) * alphaScale;
            const float breathe = 0.72f + 0.28f * sinf(t * 0.30f + ph);
            const float x0 = cx - halfW, x1 = cx + halfW, xm = cx;
            for (int k = 0; k < slices; ++k) {
                const float f0 = (float)k / (float)slices;
                const float f1 = (float)(k + 1) / (float)slices;
                const float y0 = wMin.y + curtainH * f0;
                const float y1 = wMin.y + curtainH * f1;
                const int aTop = (int)ImClamp(baseA * (1.0f - f0) * breathe, 0.0f, 255.0f);
                const int aBot = (int)ImClamp(baseA * (1.0f - f1) * breathe, 0.0f, 255.0f);
                if (aTop <= 1 && aBot <= 1) continue;
                const ImU32 cT = IM_COL32(cr, cg, cb, aTop);
                const ImU32 cB = IM_COL32(cr, cg, cb, aBot);
                const ImU32 c0 = IM_COL32(cr, cg, cb, 0);
                // 左半：左透明 → 中间有色
                dl->AddRectFilledMultiColor(ImVec2(x0, y0), ImVec2(xm, y1), c0, cT, cB, c0);
                // 右半：中间有色 → 右透明
                dl->AddRectFilledMultiColor(ImVec2(xm, y0), ImVec2(x1, y1), cT, c0, c0, cB);
            }
        }
        dl->PopClipRect();
    }

private:
    float t = 0;
};

} // namespace Glass
