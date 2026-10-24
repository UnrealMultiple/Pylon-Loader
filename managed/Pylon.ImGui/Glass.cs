using System;
using System.Collections.Generic;

namespace Pylon.ImGui;

/// <summary>暗色玻璃拟态配色（对应 GlassKit.h 的 Palette）。</summary>
public struct GlassPalette
{
    public uint Bg, Card, CardBorder, DotGrid;
    public uint Text, TextDim;
    public uint Accent, AccentHover, AccentSoft, Track;
    public uint Danger, DangerSoft, Success, NavBar, Shadow;

    public static GlassPalette Dark()
    {
        return new GlassPalette
        {
            Bg = ImGuiNative.igColor4(0x0A / 255f, 0x0A / 255f, 0x0C / 255f, 1f),
            Card = ImGuiNative.igColor4(0x15 / 255f, 0x15 / 255f, 0x1B / 255f, 1f),
            CardBorder = ImGuiNative.igColor4(1f, 1f, 1f, 0x14 / 255f),
            DotGrid = ImGuiNative.igColor4(1f, 1f, 1f, 0x12 / 255f),
            Text = ImGuiNative.igColor4(0xF4 / 255f, 0xF4 / 255f, 0xF6 / 255f, 1f),
            TextDim = ImGuiNative.igColor4(0xF4 / 255f, 0xF4 / 255f, 0xF6 / 255f, 0x72 / 255f),
            Accent = ImGuiNative.igColor4(0x7C / 255f, 0x74 / 255f, 1f, 1f),
            AccentHover = ImGuiNative.igColor4(0x8F / 255f, 0x88 / 255f, 1f, 1f),
            AccentSoft = ImGuiNative.igColor4(0x7C / 255f, 0x74 / 255f, 1f, 0x2E / 255f),
            Track = ImGuiNative.igColor4(0x7C / 255f, 0x74 / 255f, 1f, 0x33 / 255f),
            Danger = ImGuiNative.igColor4(1f, 0x5A / 255f, 0x5A / 255f, 1f),
            DangerSoft = ImGuiNative.igColor4(1f, 0x5A / 255f, 0x5A / 255f, 0x1E / 255f),
            Success = ImGuiNative.igColor4(0x3A / 255f, 0xD6 / 255f, 0x7E / 255f, 1f),
            NavBar = ImGuiNative.igColor4(0x2A / 255f, 0x2A / 255f, 0x38 / 255f, 0xB4 / 255f),
            Shadow = ImGuiNative.igColor4(0f, 0f, 0f, 0x66 / 255f),
        };
    }
}

/// <summary>GlassKit 视觉组件（静态；每个控件 id 持自己的动画状态）。</summary>
public static class Glass
{
    // 控件动画状态（key = 控件 id 字符串）：值随帧缓动
    private static readonly Dictionary<string, float> AnimState = new();

    private static float GetState(string key, float def)
        => AnimState.TryGetValue(key, out var v) ? v : def;

    private static void SetState(string key, float v) => AnimState[key] = v;

    private static float _lastTime = -1;
    private static float _frameDt;
    private static float _dtFrameStamp = -1;   // 已算过的帧时间戳（帧内缓存 dt）

    /// <summary>本帧 dt（秒），帧率无关；帧内缓存——同一帧重复调用返回同值，否则各控件各自算一次、后面的会得 0。</summary>
    public static float Dt()
    {
        float now = Anim.Time;
        if (_dtFrameStamp == now) return _frameDt;   // 本帧已算过
        _dtFrameStamp = now;
        float dt = _lastTime < 0 ? 0 : now - _lastTime;
        _lastTime = now;
        if (dt < 0) dt = 0;
        if (dt > 0.1f) dt = 0.1f;   // 掉帧保护
        _frameDt = dt;
        return dt;
    }

    // ===================================================================
    // 卡片 / 胶囊
    // ===================================================================

    /// <summary>圆角卡片 + 多层软阴影（对照 PanelWithShadow）。</summary>
    public static void PanelWithShadow(DrawList dl, ImVec2 min, ImVec2 max,
                                       uint fill, uint border, uint shadow,
                                       float rounding = 16f)
    {
        for (int i = 6; i >= 1; i--)
        {
            float o = i * 2f;
            uint s = WithAlpha(shadow, 0.16f * (7 - i) / 6f);
            dl.AddRectFilled(new ImVec2(min.X - o, min.Y - o + 4), new ImVec2(max.X + o, max.Y + o + 6),
                             s, rounding + o);
        }
        dl.AddRectFilled(min, max, fill, rounding);
        dl.AddRect(min, max, border, rounding, 1.5f);
    }

    /// <summary>半透明液态玻璃胶囊（叠色 + 顶边高光模拟，对照 GlassPill）。</summary>
    public static void GlassPill(DrawList dl, ImVec2 min, ImVec2 max,
                                 in GlassPalette p, float rounding)
    {
        // 1) 外柔和阴影
        for (int i = 7; i >= 1; i--)
        {
            float o = i * 1.8f;
            uint s = WithAlpha(p.Shadow, 0.10f * (8 - i) / 7f);
            dl.AddRectFilled(new ImVec2(min.X - o, min.Y - o + 3), new ImVec2(max.X + o, max.Y + o + 5),
                             s, rounding + o);
        }
        // 2) 半透明磨砂底
        dl.AddRectFilled(min, max, p.NavBar, rounding);
        // 3) 顶边淡淡高光（玻璃反光）
        dl.AddLine(new ImVec2(min.X + rounding, min.Y + 1f), new ImVec2(max.X - rounding, min.Y + 1f),
                   ImGuiNative.igColor4(1f, 1f, 1f, 0.10f), 1f);
        // 4) 细描边
        dl.AddRect(min, max, p.CardBorder, rounding, 1f);
    }

    /// <summary>把颜色的 alpha 换成给定值（0..1），RGB 不变。</summary>
    private static uint WithAlpha(uint col, float a)
    {
        uint r = (col >> 0) & 0xFF, g = (col >> 8) & 0xFF, b = (col >> 16) & 0xFF, oldA = (col >> 24) & 0xFF;
        // ImU32 = 0xRRGGBBAA（原生打包顺序）；改字节末位为 alpha
        uint a8 = (uint)Math.Clamp(a * 255f, 0f, 255f);
        return (col & 0xFFFFFF00u) | (a8 & 0xFF);
    }

    // ===================================================================
    // 签名控件
    // ===================================================================

    /// <summary>药丸开关（对照 ToggleSwitch）：返回 true = 本帧切换。</summary>
    public static bool ToggleSwitch(string id, ref bool v, in GlassPalette p)
    {
        ImGui.PushID(id);
        try
        {
            var pos = ImGui.GetCursorScreenPos();   // 用傀儡占位替代 CursorScreenPos（无直接 API 时）
            float h = 26f, w = 46f, r = h * 0.5f;
            ImGui.InvisibleButton("t", w, h);
            bool changed = false;
            if (ImGui.IsItemClicked()) { v = !v; changed = true; }

            string kk = id + ".k";
            float k = GetState(kk, v ? 1f : 0f);
            k = Anim.Approach(k, v ? 1f : 0f, Dt(), 16f);
            SetState(kk, k);

            uint track = LerpColor(p.Track, p.Accent, k);
            var dl = DrawList.Window;
            dl.AddRectFilled(pos, new ImVec2(pos.X + w, pos.Y + h), track, r);
            float cx = pos.X + r + (w - h) * k;
            dl.AddCircleFilled(new ImVec2(cx, pos.Y + r), r - 3f, ImGuiNative.igColor4(1f, 1f, 1f, 1f));
            return changed;
        }
        finally { ImGui.PopID(); }
    }

    /// <summary>翻转勾选（对照 CheckChip）：自带 0.2s 翻转收缩动画。</summary>
    public static bool CheckChip(string id, ref bool v, in GlassPalette p)
    {
        ImGui.PushID(id);
        try
        {
            var pos = ImGui.GetCursorScreenPos();
            float w = 40f, h = 26f, r = 9f;
            ImGui.InvisibleButton("c", w, h);
            bool changed = false;
            if (ImGui.IsItemClicked()) { v = !v; changed = true; }

            var dl = DrawList.Window;
            float dt = Dt();
            string phId = id + ".ph", dispId = id + ".disp";
            float target = v ? 1f : 0f;
            float ph = GetState(phId, -1f);
            float disp = GetState(dispId, target);

            if (changed) ph = 0f;
            else if (ph < 0f && disp != target) ph = 0f;
            if (ph >= 0f)
            {
                ph += dt / 0.20f;
                if (ph >= 0.5f) disp = target;
                if (ph >= 1f) { ph = -1f; disp = target; }
            }
            SetState(phId, ph);
            SetState(dispId, disp);

            float scaleY = ph < 0f ? 1f : (float)Math.Abs(Math.Cos(ph * Math.PI));
            if (scaleY < 0.04f) scaleY = 0.04f;
            bool on = disp > 0.5f;
            var c = new ImVec2((pos.X + pos.X + w) * 0.5f, (pos.Y + pos.Y + h) * 0.5f);
            float hh = (h * 0.5f) * scaleY;
            var mn = new ImVec2(pos.X, c.Y - hh);
            var mx = new ImVec2(pos.X + w, c.Y + hh);

            float shade = 0.55f + 0.45f * scaleY;
            uint baseCol = on ? p.Accent : p.Track;
            baseCol = ShadeRgb(baseCol, shade);
            float rr = r * scaleY; if (rr < 3f) rr = 3f;
            dl.AddRectFilled(mn, mx, baseCol, rr);
            float hl = 0.16f * scaleY * (on ? 1.2f : 0.6f);
            dl.AddLine(new ImVec2(mn.X + rr, mn.Y + 1.5f), new ImVec2(mx.X - rr, mn.Y + 1.5f),
                       ImGuiNative.igColor4(1f, 1f, 1f, hl), 1f);
            return changed;
        }
        finally { ImGui.PopID(); }
    }

    /// <summary>气泡滑块（对照 FancySlider）：轨道 + 弹性圆钮 + 悬停/按下生长。</summary>
    public static bool FancySlider(string id, ref float v, float vmin, float vmax, in GlassPalette p)
    {
        ImGui.PushID(id);
        try
        {
            float w = ImGui.GetContentRegionAvail().X;
            float h = 44f;
            var pos = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("s", w, h);
            bool active = ImGui.IsItemActive();
            bool hovered = ImGui.IsItemHovered();
            float trackY = pos.Y + h - 10f;
            float x0 = pos.X + 10f, x1 = pos.X + w - 10f;
            float t = (v - vmin) / (vmax - vmin);

            bool changed = false;
            if (active)
            {
                var mouse = ImGui.MousePos;
                float nt = (mouse.X - x0) / (x1 - x0);
                nt = nt < 0 ? 0 : (nt > 1 ? 1 : nt);
                float nv = vmin + nt * (vmax - vmin);
                if (nv != v) { v = nv; changed = true; }
                t = nt;
            }

            var dl = DrawList.Window;
            float dt = Dt();
            string dtk = id + ".dispT", gk = id + ".grow";
            float dispT = GetState(dtk, t);
            dispT = Anim.Approach(dispT, t, dt, 16f);
            if (Math.Abs(dispT - t) < 0.0005f) dispT = t;
            SetState(dtk, dispT);

            float grow = GetState(gk, 0f);
            float gTarget = active ? 1f : (hovered ? 0.55f : 0f);
            grow = Anim.Approach(grow, gTarget, dt, 18f);
            SetState(gk, grow);

            float knobX = x0 + dispT * (x1 - x0);
            float knobR = 9f + grow * 2f;
            float ringT = 1.5f + grow * 1f;

            dl.AddLine(new ImVec2(x0, trackY), new ImVec2(x1, trackY), p.Track, 3f);
            dl.AddLine(new ImVec2(x0, trackY), new ImVec2(knobX, trackY), p.Accent, 3f);
            dl.AddCircleFilled(new ImVec2(knobX, trackY), knobR, p.Accent, 0);
            dl.AddCircle(new ImVec2(knobX, trackY), knobR + 4f + grow * 2f,
                         ImGuiNative.igColor4(1f, 1f, 1f, 0.15f * (grow + 0.3f)), 0, ringT);
            return changed;
        }
        finally { ImGui.PopID(); }
    }

    /// <summary>颜色插值（a 到 b，t 0..1）。</summary>
    private static uint LerpColor(uint a, uint b, float t)
    {
        float rA = (a >> 0) & 0xFF, gA = (a >> 8) & 0xFF, bA = (a >> 16) & 0xFF;
        float rB = (b >> 0) & 0xFF, gB = (b >> 8) & 0xFF, bB = (b >> 16) & 0xFF;
        return ImGuiNative.igColor4(
            Lerp(rA, rB, t) / 255f, Lerp(gA, gB, t) / 255f, Lerp(bA, bB, t) / 255f, 1f);
    }

    /// <summary>RGB 乘亮度（alpha 不变）。</summary>
    private static uint ShadeRgb(uint col, float shade)
    {
        uint r = (uint)Math.Clamp(((col >> 0) & 0xFF) * shade, 0, 255);
        uint g = (uint)Math.Clamp(((col >> 8) & 0xFF) * shade, 0, 255);
        uint b = (uint)Math.Clamp(((col >> 16) & 0xFF) * shade, 0, 255);
        uint a = (col >> 24) & 0xFF;
        return (a << 24) | (b << 16) | (g << 8) | r;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}