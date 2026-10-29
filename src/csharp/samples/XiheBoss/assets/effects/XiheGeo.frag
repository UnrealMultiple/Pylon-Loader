// 羲和 Boss —— 硬件几何的片元着色器（Pylon mesh 着色器 / GLSL ES 3.00）。
//
// 和 XiheTri.frag 的数学完全一样（局部 uv → 图集 cell、MEAC Colorize.fx、顶点色相乘），
// 但三角形由 GPU 光栅化：C# 只提交顶点，不再让每个屏幕像素循环测试 64 个三角形。
// 这是本 Mod 掉帧问题的根因修复，绘制结果（位置/uv/颜色/混合）与原 Tri 通道逐像素一致。
#include <bl/screen>

uniform sampler2D uAtlas;

uniform vec4 uCell;       // 图集 uv 矩形（u0,v0,du,dv）
uniform float uTexOn;     // 0 = MagicPixel/纯色顶点，1 = 采样图集
uniform float uWrapU;     // 1 = u 在 cell 内循环（PointWrap）
uniform float uColorize;  // 1 = 走 MEAC/Effects/Colorize.fx

vec4 sampleLocal(vec2 uv) {
    vec2 q = uv;
    if (uWrapU > 0.5) q.x = fract(q.x);
    q = clamp(q, vec2(0.0), vec2(1.0));
    // 图集 v=0 在底部；局部 uv 的 v=0 是纹理顶边。
    vec2 atlasUv = uCell.xy + vec2(q.x, 1.0 - q.y) * uCell.zw;
    vec2 lo = uCell.xy + vec2(0.0008);
    vec2 hi = uCell.xy + uCell.zw - vec2(0.0008);
    return texture(uAtlas, clamp(atlasUv, lo, hi));
}

vec3 meacColorize(vec3 base, vec3 col, float saturation) {
    float lum = dot(base, vec3(0.3, 0.6, 0.1));
    vec3 mid = mix(vec3(0.5), col, saturation);
    return lum < 0.5 ? mix(vec3(0.0), mid, lum * 2.0)
                     : mix(mid, vec3(1.0), (lum - 0.5) * 2.0);
}

void main() {
    vec4 src = vec4(1.0);
    if (uTexOn > 0.5) src = sampleLocal(vUV);

    vec3 rgb;
    float alpha;
    if (uColorize > 0.5) {
        // MEAC Colorize.fx：rgb = colorize(tex.rgb, drawColor.rgb, 0.8)，a = tex.a，再乘 drawColor.a。
        rgb = meacColorize(src.rgb, vColor.rgb, 0.8) * vColor.a;
        alpha = src.a * vColor.a;
    } else {
        rgb = src.rgb * vColor.rgb;
        alpha = src.a * vColor.a;
    }
    fragColor = vec4(rgb, alpha);
}
