using System;

namespace Pylon.ImGui;

/// <summary>ImDrawList C# 包装（只在窗口体/渲染帧内绘制有效）。</summary>
public readonly struct DrawList
{
    private readonly IntPtr _dl;

    private DrawList(IntPtr dl) => _dl = dl;

    /// <summary>当前窗口的绘制列表。</summary>
    public static DrawList Window => new(ImGuiNative.igGetWindowDrawList());

    /// <summary>前景绘制列表（盖在窗口内容之上；悬浮球/覆盖效果用）。</summary>
    public static DrawList Foreground => new(ImGuiNative.igGetForegroundDrawList());

    /// <summary>是否有效（原生上下文未就绪时可能拿不到）。</summary>
    public bool Valid => _dl != IntPtr.Zero;

    public void AddRectFilled(ImVec2 min, ImVec2 max, uint col, float rounding = 0)
        => ImGuiNative.igDL_AddRectFilled(_dl, min.X, min.Y, max.X, max.Y, col, rounding);

    public void AddRectFilledMultiColor(ImVec2 min, ImVec2 max,
        uint colUL, uint colUR, uint colLR, uint colLL)
        => ImGuiNative.igDL_AddRectFilledMultiColor(_dl, min.X, min.Y, max.X, max.Y, colUL, colUR, colLR, colLL);

    public void AddRect(ImVec2 min, ImVec2 max, uint col, float rounding = 0, float thickness = 1)
        => ImGuiNative.igDL_AddRect(_dl, min.X, min.Y, max.X, max.Y, col, rounding, thickness);

    public void AddLine(ImVec2 a, ImVec2 b, uint col, float thickness = 1)
        => ImGuiNative.igDL_AddLine(_dl, a.X, a.Y, b.X, b.Y, col, thickness);

    public void AddCircleFilled(ImVec2 center, float r, uint col, int segments = 0)
        => ImGuiNative.igDL_AddCircleFilled(_dl, center.X, center.Y, r, col, segments);

    public void AddCircle(ImVec2 center, float r, uint col, int segments = 0, float thickness = 1)
        => ImGuiNative.igDL_AddCircle(_dl, center.X, center.Y, r, col, segments, thickness);

    public void AddEllipseFilled(ImVec2 center, ImVec2 radius, uint col, float rotation = 0, int segments = 0)
        => ImGuiNative.igDL_AddEllipseFilled(_dl, center.X, center.Y, radius.X, radius.Y, col, rotation, segments);

    public void AddText(ImVec2 pos, uint col, string text)
        => ImGuiNative.igDL_AddText(_dl, pos.X, pos.Y, col, text);

    public void AddTriangleFilled(ImVec2 a, ImVec2 b, ImVec2 c, uint col)
        => ImGuiNative.igDL_AddTriangleFilled(_dl, a.X, a.Y, b.X, b.Y, c.X, c.Y, col);

    public void AddPolyline(ImVec2[] pts, uint col, float thickness)
    {
        if (pts == null || pts.Length == 0) return;
        var xy = new float[pts.Length * 2];
        for (int i = 0; i < pts.Length; i++) { xy[i * 2] = pts[i].X; xy[i * 2 + 1] = pts[i].Y; }
        ImGuiNative.igDL_AddPolyline(_dl, xy, pts.Length, col, thickness);
    }

    /// <summary>顶部圆角 + 垂直渐变矩形（极光顶部光带用）。</summary>
    public void AddRoundedGradientRectTop(ImVec2 min, ImVec2 max, float rounding,
                                          uint colTop, uint colBot)
        => ImGuiNative.igDL_AddRoundedGradientRectTop(_dl, min.X, min.Y, max.X, max.Y, rounding, colTop, colBot);

    /// <summary>直接画上传的纹理到屏幕矩形（tint 调色，0xFFFFFFFF = 原样）。</summary>
    public void AddImage(ulong id, ImVec2 min, ImVec2 max, uint tint = 0xFFFFFFFF)
        => ImGuiNative.igDL_AddImage(_dl, id, min.X, min.Y, max.X, max.Y, tint);

    /// <summary>按指定字号绘制文本（Chrome：标题/副标题/标签按比例字号）。</summary>
    public void AddTextFont(ImVec2 pos, uint col, string text, float fontSize)
        => ImGuiNative.igDL_AddTextFont(_dl, pos.X, pos.Y, col, text, fontSize);

    /// <summary>带圆角 flags 的实心矩形（侧栏 RoundCornersBottom 等）。</summary>
    public void AddRectFilledEx(ImVec2 min, ImVec2 max, uint col, float rounding, int flags = 0)
        => ImGuiNative.igDL_AddRectFilledEx(_dl, min.X, min.Y, max.X, max.Y, col, rounding, flags);

    /// <summary>带圆角 flags 的描边矩形。</summary>
    public void AddRectEx(ImVec2 min, ImVec2 max, uint col, float rounding, int flags = 0, float thickness = 1)
        => ImGuiNative.igDL_AddRectEx(_dl, min.X, min.Y, max.X, max.Y, col, rounding, flags, thickness);

    /// <summary>实心四边形（品牌菱形 logo 用）。</summary>
    public void AddQuadFilled(ImVec2 a, ImVec2 b, ImVec2 c, ImVec2 d, uint col)
        => ImGuiNative.igDL_AddQuadFilled(_dl, a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y, col);

    /// <summary>实心凸多边形（四角星 logo 用）。</summary>
    public void AddConvexPolyFilled(ImVec2[] pts, uint col)
    {
        if (pts == null || pts.Length == 0) return;
        var xy = new float[pts.Length * 2];
        for (int i = 0; i < pts.Length; i++) { xy[i * 2] = pts[i].X; xy[i * 2 + 1] = pts[i].Y; }
        ImGuiNative.igDL_AddConvexPolyFilled(_dl, xy, pts.Length, col);
    }

    public void PushClipRect(ImVec2 min, ImVec2 max, bool intersect = true)
        => ImGuiNative.igDL_PushClipRect(_dl, min.X, min.Y, max.X, max.Y, intersect ? 1 : 0);

    public void PopClipRect() => ImGuiNative.igDL_PopClipRect(_dl);
}

/// <summary>动画工具（GlassKit 的 SpringTick / Approach 移植；帧率无关的缓动）。</summary>
public static class Anim
{
    /// <summary>全局时钟（秒），驱动缓动。</summary>
    public static float Time => ImGuiNative.igGetTime();

    /// <summary>指数平滑：cur 朝 target 以 speed 收敛（帧率无关，默认 10 = 较快跟手）。</summary>
    public static float Approach(float cur, float target, float dt, float speed = 10f)
    {
        if (dt <= 0) return cur;
        float t = 1f - (float)Math.Exp(-speed * dt);
        return cur + (target - cur) * t;
    }

    /// <summary>弹性缓动状态（仿UI Spring：stiff 刚度 / damp 阻尼）。</summary>
    public struct Spring
    {
        public float Value;
        public float Velocity;

        public Spring(float value) { Value = value; Velocity = 0; }

        /// <summary>弹性追 target（dt 秒；stiff 高 = 更硬更快，damp 高 = 更早衰减）。</summary>
        public void Tick(float target, float dt, float stiff = 120f, float damp = 12f)
        {
            if (dt <= 0) return;
            float acc = (target - Value) * stiff - Velocity * damp;
            Velocity += acc * dt;
            Value += Velocity * dt;
        }
    }
}