// 羲和 Boss —— MEAC/Effects/Trail.fx 的 pass 0（Trail）逐行移植。
//
// 几何照抄 MEAC：折线每段中点为骨架，半宽 = 15（EoLProj）或 node.width*3（EoLFire），
// 顶点属性 x=sqrt(factor)（0=头，1=尾）、y=0/1（横跨）、z=lerp(1.2,0,pow(factor,0.6))。
// 像素着色器：
//   c  = tex(uShapeTex, (coord.x + t, coord.y));
//   c += tex(uImage2, (1 - coord.x*5, coord.y));
//   c *= coord.z;
//   c *= tex(uColorTex, (c.r, 0.5));
// 与 MEAC 一样：uShapeTex 用 PointWrap（这里在 cell 内 fract），颜色图用 PointClamp。
#include <bl/screen>

uniform sampler2D uAtlas;

uniform vec4 uPts[32];       // xy=屏幕像素（相对屏幕中心，y 向下），z=sqrt(factor)，w=coord.z
uniform vec4 uWidth[32];     // x=半宽（屏幕像素）
uniform float uCount;

uniform vec4 uCellShape;     // u0,v0,du,dv
uniform vec4 uCellColor;
uniform vec4 uCellOverlay;

uniform float uT;
uniform float uAdditive;
uniform float uAlpha;

vec2 cellLo(vec4 c) { return c.xy + vec2(0.0008); }
vec2 cellHi(vec4 c) { return c.xy + c.zw - vec2(0.0008); }

vec4 sampleClamp(vec4 cell, vec2 uv) {
    return texture(uAtlas, clamp(uv, cellLo(cell), cellHi(cell)));
}

vec4 sampleWrap(vec4 cell, vec2 uv) {
    // 图集 v=0 在底部；Trail.fx 的 coord.y=0 是纹理顶边，所以翻 v。
    vec2 t = vec2(cell.x + fract(uv.x) * cell.z,
                  cell.y + (1.0 - clamp(uv.y, 0.0, 1.0)) * cell.w);
    return texture(uAtlas, clamp(t, cellLo(cell), cellHi(cell)));
}

void main() {
    vec2 pix = vec2((vUV.x - 0.5) * uAspect, 0.5 - vUV.y) * uResolution.y;
    vec3 acc = blScreen(vUV).rgb;

    // 取最靠前的一段命中（MEAC 的三角形条带不会在同一像素叠两次）。
    bool hit = false;
    float bestT = 0.0;
    int bestI = -1;
    float bestDist = 1e20;
    float bestCross = 0.0;

    for (int i = 0; i < 31; i++) {
        if (float(i) >= uCount - 1.0) break;
        vec2 p0 = uPts[i].xy;
        vec2 p1 = uPts[i + 1].xy;
        vec2 d = p1 - p0;
        float len2 = dot(d, d);
        if (len2 < 1e-4) continue;

        float t = clamp(dot(pix - p0, d) / len2, 0.0, 1.0);
        vec2 q = p0 + d * t;
        vec2 n = normalize(vec2(-d.y, d.x));
        float cross = dot(pix - q, n);
        float w = mix(uWidth[i].x, uWidth[i + 1].x, t);
        if (abs(cross) <= w && abs(cross) < bestDist) {
            bestDist = abs(cross);
            bestT = t;
            bestI = i;
            bestCross = cross;
            hit = true;
        }
    }

    if (hit) {
        float t = bestT;
        float uvx = mix(uPts[bestI].z, uPts[bestI + 1].z, t);
        float cw = mix(uPts[bestI].w, uPts[bestI + 1].w, t);
        float w = mix(uWidth[bestI].x, uWidth[bestI + 1].x, t);
        float v = clamp(0.5 + bestCross / max(w, 1e-5) * 0.5, 0.0, 1.0);

        vec4 c = sampleWrap(uCellShape, vec2(uvx + uT, v));
        c += sampleWrap(uCellOverlay, vec2(1.0 - uvx * 5.0, v));
        c *= cw;

        vec4 col = sampleClamp(uCellColor, vec2(clamp(c.r, 0.0, 1.0), 0.5));
        c *= col;
        c.a *= uAlpha;

        if (uAdditive > 0.5) {
            acc += c.rgb * c.a;
        } else {
            acc = c.rgb * c.a + acc * (1.0 - c.a);
        }
    }

    fragColor = vec4(clamp(acc, 0.0, 1.0), 1.0);
}



