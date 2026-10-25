// 羲和 Boss —— MEAC 精灵批（Pylon ScreenShader / GLSL ES 3.00）。
//
// 这不是“光球/光柱近似”，而是把 MEAC 弹幕 PreDraw 里每一次 SpriteBatch.Draw 原样搬过来：
//   * 位置/旋转/缩放/origin 用 XNA SpriteBatch 的同一套变换；
//   * 颜色按 texture * drawColor 相乘（Colorize 时走 MEAC/Effects/Colorize.fx 的公式）；
//   * 混合按调用点的 BlendState：AlphaBlend = src*a + dst*(1-a)，Additive = dst + src*a。
// 一张图集（nearest，MEAC 的 PointClamp 口径）里放原素材，每次 pass 最多 16 个精灵，
// C# 侧按屏幕包围盒把精灵分组，box 只盖住这一组，手机上不会整屏跑 16 次。
#include <bl/screen>

uniform sampler2D uAtlas;

uniform vec4 uSprA[64];   // xy=屏幕像素中心（相对屏幕中心，y 向下），zw=scale（纹理像素/屏幕像素）
uniform vec4 uSprB[64];   // xy=cos/sin(rotation)，zw=origin（纹理像素）
uniform vec4 uSprC[64];   // rgba
uniform vec4 uSprD[64];   // uv 矩形：u0,v0,du,dv
uniform vec4 uSprE[64];   // x=1/texW, y=1/texH, z=flags, w=备用
uniform vec4 uSprF[64];   // x=PointWrap 相位（源矩形 x / texW）
uniform float uSprCount;

// flags
const float F_ADD = 1.0;
const float F_COLORIZE = 2.0;

// MEAC/Effects/Colorize.fx 的逐行实现。
vec3 meacColorize(vec3 base, vec3 col, float saturation) {
    float lum = dot(base, vec3(0.3, 0.6, 0.1));
    vec3 mid = mix(vec3(0.5), col, saturation);
    return lum < 0.5 ? mix(vec3(0.0), mid, lum * 2.0)
                     : mix(mid, vec3(1.0), (lum - 0.5) * 2.0);
}

vec4 sampleCell(vec4 cell, vec2 uv) {
    // 图集是 nearest；夹在 cell 内，避免相邻格串色。
    vec2 pad = min(vec2(0.0008), cell.zw * 0.25);
    vec2 lo = cell.xy + pad;
    vec2 hi = cell.xy + cell.zw - pad;
    hi = max(hi, lo);
    return texture(uAtlas, clamp(uv, lo, hi));
}

vec4 drawSprite(vec2 pix, int i) {
    vec4 a = uSprA[i];
    vec4 b = uSprB[i];
    vec4 c = uSprC[i];
    vec4 d = uSprD[i];
    vec4 e = uSprE[i];

    vec2 p = pix - a.xy;
    float cr = b.x, sr = b.y;
    // 反向旋转（SpriteBatch 的 rotation 是屏幕空间顺时针；我们的 pix 是 y 向下）
    vec2 local = vec2(p.x * cr + p.y * sr, -p.x * sr + p.y * cr);
    local /= max(abs(a.zw), vec2(1e-6));
    local += b.zw;

    // 源矩形像素尺寸 = du / (atlas 每像素对应的 uv)；e.y 为负（图集 v 翻转）
    vec2 srcSize = vec2(d.z / max(abs(e.x), 1e-6), d.w / max(abs(e.y), 1e-6));
    if (local.x < 0.0 || local.y < 0.0 || local.x > srcSize.x || local.y > srcSize.y) {
        return vec4(0.0);
    }

    float flags = e.z;
    vec2 uv;
    if (mod(flags, 8.0) >= 4.0) {
        // PointWrap：u 在单张纹理内循环（e.w = 源矩形覆盖的纹理张数，EoLLaser1 = 5）。
        float rep = max(e.w, 1.0);
        float texW = srcSize.x / rep;
        uv = vec2(d.x + fract(local.x / texW + uSprF[i].x) * (d.z / rep), d.y + local.y * e.y);
    } else {
        uv = d.xy + local * e.xy;
    }
    vec4 t = sampleCell(d, uv);
    vec3 rgb;
    if (mod(flags, 4.0) >= 2.0) {
        // Colorize：RGB 由亮度映射到 drawColor，A = 纹理 A * drawColor.A²（见 Colorize.fx）。
        rgb = meacColorize(t.rgb, c.rgb, 0.8) * c.a;
        return vec4(rgb, t.a * c.a);
    }

    return vec4(t.rgb * c.rgb, t.a * c.a);
}

void main() {
    vec2 pix = vec2((vUV.x - 0.5) * uAspect, 0.5 - vUV.y) * uResolution.y;
    vec3 acc = blScreen(vUV).rgb;

    for (int i = 0; i < 64; i++) {
        if (float(i) >= uSprCount) break;
        vec4 s = drawSprite(pix, i);
        if (s.a <= 0.0001) continue;
        if (mod(uSprE[i].z, 2.0) >= 1.0) {
            acc += s.rgb * s.a;                 // BlendState.Additive
        } else {
            acc = s.rgb * s.a + acc * (1.0 - s.a); // BlendState.AlphaBlend
        }
    }

    fragColor = vec4(clamp(acc, 0.0, 1.0), 1.0);
}






