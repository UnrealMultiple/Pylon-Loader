// 羲和 Boss —— MEAC Trail.fx pass 0 的精确几何版（Pylon ScreenShader / GLSL ES 3.00）。
//
// MEAC 的拖尾不是“中心线 + 半宽”，而是逐段两个顶点（左/右 + coord.x=sqrt(factor) + coord.z=w）的
// triangle strip。这里直接收这些顶点对，逐段用两个三角形求重心插值，像素着色器照抄 Trail.fx：
//   c  = tex(uShapeTex, (coord.x + t, coord.y));
//   c += tex(uImage2, (1 - coord.x*5, coord.y));
//   c *= coord.z;
//   c *= tex(uColorTex, (c.r, 0.5));
// shape 用 PointWrap，overlay/color 用 PointClamp；Additive/AlphaBlend 由 uAdditive 决定。
#include <bl/screen>

uniform sampler2D uAtlas;

uniform vec4 uQ0[64];   // xy=左0 屏幕像素, zw=右0
uniform vec4 uQ1[64];   // xy=左1 屏幕像素, zw=右1
uniform vec4 uQ2[64];   // x=ux0, y=w0, z=ux1, w=w1
uniform vec4 uQ3[64];   // x=t, y=alpha
uniform float uQCount;

uniform vec4 uCellShape;
uniform vec4 uCellColor;
uniform vec4 uCellOverlay;

uniform float uAdditive;
uniform float uGlobalAlpha;

vec2 pixOf(vec2 uv) {
    return vec2((uv.x - 0.5) * uAspect, 0.5 - uv.y) * uResolution.y;
}

vec4 sampleCell(vec4 cell, vec2 uv, float wrapU) {
    vec2 q = uv;
    if (wrapU > 0.5) q.x = fract(q.x);
    q = clamp(q, vec2(0.0), vec2(1.0));
    // 图集 v=0 在底部；MEAC 的 coord.y=0 是纹理顶边。
    vec2 atlasUv = cell.xy + vec2(q.x, 1.0 - q.y) * cell.zw;
    vec2 pad = min(vec2(0.0008), cell.zw * 0.25);
    vec2 lo = cell.xy + pad;
    vec2 hi = cell.xy + cell.zw - pad;
    hi = max(hi, lo);
    return texture(uAtlas, clamp(atlasUv, lo, hi));
}

// 返回是否命中，并给出 ux/w/side
bool quadHit(vec2 pos, vec2 L0, vec2 R0, vec2 L1, vec2 R1,
             float ux0, float w0, float ux1, float w1,
             out float ux, out float w, out float side) {
    // 三角形 A: L0, R0, L1
    vec2 p0 = L0, p1 = R0, p2 = L1;
    vec2 e1 = p1 - p0;
    vec2 e2 = p2 - p0;
    float area = e1.x * e2.y - e1.y * e2.x;
    if (abs(area) > 1e-7) {
        vec2 v = pos - p0;
        float a1 = (v.x * e2.y - v.y * e2.x) / area;
        float a2 = (e1.x * v.y - e1.y * v.x) / area;
        float a0 = 1.0 - a1 - a2;
        if (a0 >= -0.0005 && a1 >= -0.0005 && a2 >= -0.0005) {
            // L0(side=1), R0(side=0), L1(side=1)
            side = a0 * 1.0 + a1 * 0.0 + a2 * 1.0;
            ux = a0 * ux0 + a1 * ux0 + a2 * ux1;
            w = a0 * w0 + a1 * w0 + a2 * w1;
            return true;
        }
    }
    // 三角形 B: R0, L1, R1
    p0 = R0; p1 = L1; p2 = R1;
    e1 = p1 - p0;
    e2 = p2 - p0;
    area = e1.x * e2.y - e1.y * e2.x;
    if (abs(area) <= 1e-7) return false;
    vec2 v = pos - p0;
    float b1 = (v.x * e2.y - v.y * e2.x) / area;
    float b2 = (e1.x * v.y - e1.y * v.x) / area;
    float b0 = 1.0 - b1 - b2;
    if (b0 < -0.0005 || b1 < -0.0005 || b2 < -0.0005) return false;
    // R0(side=0), L1(side=1), R1(side=0)
    side = b0 * 0.0 + b1 * 1.0 + b2 * 0.0;
    ux = b0 * ux0 + b1 * ux1 + b2 * ux1;
    w = b0 * w0 + b1 * w1 + b2 * w1;
    return true;
}

void main() {
    vec2 pix = pixOf(vUV);
    vec3 acc = blScreen(vUV).rgb;

    bool hit = false;
    float ux = 0.0, w = 0.0, side = 0.0, t = 0.0, alpha = 1.0;

    for (int i = 0; i < 64; i++) {
        if (float(i) >= uQCount) break;
        vec4 a = uQ0[i];
        vec4 b = uQ1[i];
        vec4 c = uQ2[i];
        float hu, hw, hs;
        if (quadHit(pix, a.xy, a.zw, b.xy, b.zw, c.x, c.y, c.z, c.w, hu, hw, hs)) {
            ux = hu; w = hw; side = hs;
            t = uQ3[i].x;
            alpha = uQ3[i].y;
            hit = true;
            break;
        }
    }

    if (hit) {
        vec4 c = sampleCell(uCellShape, vec2(ux + t, side), 1.0);
        c += sampleCell(uCellOverlay, vec2(1.0 - ux * 5.0, side), 0.0);
        c *= w;
        c *= sampleCell(uCellColor, vec2(clamp(c.r, 0.0, 1.0), 0.5), 0.0);
        c.a *= alpha * uGlobalAlpha;
        c.rgb *= uGlobalAlpha;

        if (uAdditive > 0.5) {
            acc += c.rgb * c.a;
        } else {
            acc = c.rgb * c.a + acc * (1.0 - c.a);
        }
    }

    fragColor = vec4(clamp(acc, 0.0, 1.0), 1.0);
}





