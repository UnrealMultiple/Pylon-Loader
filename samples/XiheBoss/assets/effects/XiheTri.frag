// 羲和 Boss —— MEAC 三角面片批（Pylon ScreenShader / GLSL ES 3.00）。
//
// 用途：MEAC 里 direct DrawUserPrimitives 的几何（EoLLance 的 2500px 光矛、Cycloid 的摆线带、
// MagicRing_Sun 的 3D 投影环……）。C# 侧把每个顶点换成屏幕像素、局部 uv、顶点色，顶点色与
// XNA VertexPositionColorTexture 完全同一口径；这里按三角形逐像素重心插值。
//
// 一次 pass 最多 64 个三角形（192 个顶点），同一个 pass 只能用一张图集贴图；
// 颜色按调用点的 BlendState：Additive = dst + src*a，AlphaBlend = src*a + dst*(1-a)。
#include <bl/screen>

uniform sampler2D uAtlas;

uniform vec4 uTriA[192];   // xy=屏幕像素（相对屏幕中心，y 向下），zw=局部 uv（0..1）
uniform vec4 uTriB[192];   // rgba 顶点色（0..1）
uniform float uTriVerts;   // 顶点数，3 的倍数

uniform vec4 uCell;        // 图集 uv 矩形（u0,v0,du,dv）
uniform float uTexOn;      // 0 = MagicPixel/纯色顶点，1 = 采样图集
uniform float uWrapU;      // 1 = u 在 cell 内循环（PointWrap）
uniform float uAdditive;   // 1 = 加色混合
uniform float uColorize;   // 1 = 走 MEAC/Effects/Colorize.fx
uniform float uGlobalAlpha;

const float TAU = 6.28318530718;

vec2 pixOf(vec2 uv) {
    return vec2((uv.x - 0.5) * uAspect, 0.5 - uv.y) * uResolution.y;
}

vec3 meacColorize(vec3 base, vec3 col, float saturation) {
    float lum = dot(base, vec3(0.3, 0.6, 0.1));
    vec3 mid = mix(vec3(0.5), col, saturation);
    return lum < 0.5 ? mix(vec3(0.0), mid, lum * 2.0)
                     : mix(mid, vec3(1.0), (lum - 0.5) * 2.0);
}

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

void main() {
    vec2 pix = pixOf(vUV);
    vec3 acc = blScreen(vUV).rgb;

    for (int t = 0; t < 64; t++) {
        int i0 = t * 3;
        if (float(i0) >= uTriVerts) break;
        vec4 A0 = uTriA[i0 + 0];
        vec4 A1 = uTriA[i0 + 1];
        vec4 A2 = uTriA[i0 + 2];
        vec2 p0 = A0.xy, p1 = A1.xy, p2 = A2.xy;

        vec2 e1 = p1 - p0;
        vec2 e2 = p2 - p0;
        float area = e1.x * e2.y - e1.y * e2.x;
        if (abs(area) < 1e-7) continue;

        vec2 v = pix - p0;
        float w1 = (v.x * e2.y - v.y * e2.x) / area;      // 权重 p1
        float w2 = (e1.x * v.y - e1.y * v.x) / area;      // 权重 p2
        float w0 = 1.0 - w1 - w2;
        if (w0 < -0.0005 || w1 < -0.0005 || w2 < -0.0005) continue;

        vec4 C0 = uTriB[i0 + 0];
        vec4 C1 = uTriB[i0 + 1];
        vec4 C2 = uTriB[i0 + 2];
        vec4 vc = C0 * w0 + C1 * w1 + C2 * w2;

        vec2 uv = A0.zw * w0 + A1.zw * w1 + A2.zw * w2;
        vec4 src = vec4(1.0);
        if (uTexOn > 0.5) src = sampleLocal(uv);

        vec3 rgb;
        float alpha;
        if (uColorize > 0.5) {
            rgb = meacColorize(src.rgb, vc.rgb, 0.8) * vc.a;
            alpha = src.a * vc.a;
        } else {
            rgb = src.rgb * vc.rgb;
            alpha = src.a * vc.a;
        }
        rgb *= uGlobalAlpha;
        alpha *= uGlobalAlpha;

        if (uAdditive > 0.5) {
            acc += rgb * alpha;
        } else if (alpha > 0.0001) {
            acc = rgb * alpha + acc * (1.0 - alpha);
        }
    }

    fragColor = vec4(clamp(acc, 0.0, 1.0), 1.0);
}


