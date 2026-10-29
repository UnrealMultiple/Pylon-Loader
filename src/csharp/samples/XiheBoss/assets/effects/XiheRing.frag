// 羲和 Boss —— MEAC 的环带绘制（DrawArea / Circle）。
//
// MEAC 用 200 段三角条带画一个环：内半径 R、外半径 R+d，顶点 UV.x = i/counts（角度），
// UV.y = 0（内）/ 1（外），贴 Wave，顶点色 = 颜色（AlphaBlend 或 Additive）。
// 这里逐像素解析同一个环带，贴图取样与混合方程与顶点条带一致。
#include <bl/screen>

uniform sampler2D uAtlas;

uniform vec4 uRingA[8];   // xy=屏幕像素中心（相对屏幕中心，y 向下），z=内半径，w=宽度
uniform vec4 uRingB[8];   // rgba
uniform vec4 uRingC[8];   // uv 矩形
uniform vec4 uRingD[8];   // x=flags(1=additive)，y=arc(0=整圈)，z/w 备用
uniform float uRingCount;

const float TAU = 6.28318530718;

vec4 sampleCell(vec4 cell, vec2 uv) {
    vec2 pad = min(vec2(0.0008), cell.zw * 0.25);
    vec2 lo = cell.xy + pad;
    vec2 hi = cell.xy + cell.zw - pad;
    hi = max(hi, lo);
    return texture(uAtlas, clamp(uv, lo, hi));
}

void main() {
    vec2 pix = vec2((vUV.x - 0.5) * uAspect, 0.5 - vUV.y) * uResolution.y;
    vec3 acc = blScreen(vUV).rgb;

    for (int i = 0; i < 8; i++) {
        if (float(i) >= uRingCount) break;
        vec4 a = uRingA[i];
        vec2 d = pix - a.xy;
        float r = length(d);
        float dist = abs(r - a.z);
        if (dist > a.w || a.w <= 1e-5) continue;

        float ang = atan(d.y, d.x);
        float u = fract(ang / TAU);
        float v = clamp((r - a.z) / a.w, 0.0, 1.0);

        // 图集 v=0 在图像底部，MEAC 顶点 v=0 在顶部：取样时翻一下。
        vec4 c = sampleCell(uRingC[i], vec2(u, 1.0 - v));
        vec4 color = uRingB[i];
        vec4 src = vec4(c.rgb * color.rgb, c.a * color.a);

        if (mod(uRingD[i].x, 2.0) >= 1.0) {
            acc += src.rgb * src.a;
        } else {
            acc = src.rgb * src.a + acc * (1.0 - src.a);
        }
    }

    fragColor = vec4(clamp(acc, 0.0, 1.0), 1.0);
}





