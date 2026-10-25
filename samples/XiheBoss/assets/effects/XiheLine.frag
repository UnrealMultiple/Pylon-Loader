// 羲和 Boss —— MEAC Cycloid.DrawCycloid 的折线（顶点条带 + MagicPixel）。
// 源把 Create(r1,100,0.15,2,70) 的 ~467 个点按 scale/rotation 变换后画成 2px 的加色条带，
// 再叠两圈半径 100/110 的圆。这里用同一个参数方程逐点求最近线段，颜色/宽度与源一致。
#include <bl/screen>

uniform vec4 uPts[64];     // xy=屏幕像素（相对屏幕中心，y 向下），zw 备用
uniform float uCount;
uniform float uWidth;
uniform vec4 uLineColor;
uniform float uAdditive;

void main() {
    vec2 pix = vec2((vUV.x - 0.5) * uAspect, 0.5 - vUV.y) * uResolution.y;
    vec3 acc = blScreen(vUV).rgb;

    float best = 1e20;
    for (int i = 0; i < 63; i++) {
        if (float(i) >= uCount - 1.0) break;
        vec2 p0 = uPts[i].xy;
        vec2 p1 = uPts[i + 1].xy;
        vec2 d = p1 - p0;
        float len2 = dot(d, d);
        if (len2 < 1e-4) continue;
        float t = clamp(dot(pix - p0, d) / len2, 0.0, 1.0);
        float dist = length(pix - (p0 + d * t));
        if (dist < best) best = dist;
    }

    if (best <= uWidth) {
        if (uAdditive > 0.5) {
            acc += uLineColor.rgb * uLineColor.a;
        } else {
            acc = uLineColor.rgb * uLineColor.a + acc * (1.0 - uLineColor.a);
        }
    }

    fragColor = vec4(clamp(acc, 0.0, 1.0), 1.0);
}



