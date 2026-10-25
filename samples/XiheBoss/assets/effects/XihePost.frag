// 羲和 Boss —— 后处理（屏幕层），公式逐条对照 MEAC：
//   1 = EoLWarp：Gray.fx（0.3/0.6/0.1）+ EoLWarp.fx（Perlin 旋转偏移）
//   2 = RadialGlur / RadialGlur2：9 层缩放采样（AlphaBlend / Additive）
//   3 = Brightness：加色叠屏（a = Lerp(1,brightness,progress2)，>1 再叠一次）
//   4 = EoLSky：黑幕 + TSSky1（加色）
//   5 = 闪白
//   6 = Sun.fx：SunProj / EoLSky.DrawSun 的完整逐像素公式
#include <bl/screen>
#include <bl/noise>

uniform float uMode;
uniform vec2 uPos;        // 效果中心（uv，v=0 顶部）
uniform float uStrength;
uniform float uExtra;
uniform sampler2D uSkyTex;
uniform sampler2D uNoise;

const float TAU = 6.28318530718;

vec3 sunFx(vec2 uvTop, vec2 posTop, float scale, float rayAlpha) {
    float t = uTime * 1.2;                             // Main.timeForVisualEffects * 0.02
    vec2 uv = uvTop - posTop;
    uv.x *= uAspect;
    uv /= max(dot(uv, uv), 1e-6);
    uv *= scale;

    float l = length(uv);
    vec3 col = vec3(l);
    float ang = atan(uv.y, uv.x);

    float ray = sin(ang * 7.0 + t * 1.5) + cos(ang * 10.0 - t * 2.0);
    ray *= sin(ang * 15.0 + t * 2.0) * 0.5 + 0.5;
    if (ray < 0.0) ray *= 0.8;
    ray *= 0.7;
    col += ray * 0.1 * rayAlpha;

    float i = 1.0 - (col.r + col.g + col.b) / 3.0;
    col *= mix(vec3(1.0), vec3(1.0, 0.8, 0.1), i);
    return col;
}

void main() {
    vec2 uv = vUV;
    vec2 uvTop = vec2(uv.x, 1.0 - uv.y);
    vec2 posTop = vec2(uPos.x, 1.0 - uPos.y);
    vec3 base = blScreen(uv).rgb;

    if (uMode < 1.5) {
        // ---- EoLWarp (Gray.fx + EoLWarp.fx) ----
        vec2 coords = uvTop;
        float tOffset = sin(uTime * 0.6) * 0.4;          // Main.timeForVisualEffects*0.01
        float warpColor = texture(uNoise, coords + vec2(tOffset, 0.0)).r - 0.5;

        vec2 offset = coords - posTop;
        float dis = length(offset);
        float w = 0.0;
        if (dis > 0.3) {
            w = dis < 1.2 ? (dis - 0.3) * uExtra * warpColor
                          : (1.2 - 0.3) * uExtra * warpColor;
        }
        float c = cos(w), s = sin(w);
        // HLSL: mul(float2, float2x2(cos,-sin,sin,cos)) → (x*c+y*s, -x*s+y*c)
        vec2 warped = vec2(c * offset.x + s * offset.y, -s * offset.x + c * offset.y);
        vec3 src = blScreen(vec2(posTop.x + warped.x, 1.0 - (posTop.y + warped.y))).rgb;

        float luma = dot(src, vec3(0.3, 0.6, 0.1));
        base = mix(vec3(luma), src, 1.0 - clamp(uStrength, 0.0, 1.0));
    } else if (uMode < 2.5) {
        // ---- RadialGlur / RadialGlur2 ----
        vec3 acc = base;
        float p2 = clamp(uStrength, 0.0, 1.0);
        for (int i = -4; i <= 4; i++) {
            float s = 1.0 + 0.07 * float(i) / 4.0 * p2;
            vec2 suv = uPos + (uv - uPos) / s;
            vec3 c = blScreen(clamp(suv, vec2(0.001), vec2(0.999))).rgb;
            if (uExtra > 0.5) {
                float a = 1.0 / 9.0 * (p2 * 0.5 + 1.0) * (1.0 - abs(float(i) / 4.0)) * 2.5;
                acc += c * a;
            } else {
                acc = c * 0.32 + acc * 0.68;
            }
        }
        base = acc;
    } else if (uMode < 3.5) {
        // ---- Brightness ----
        base += base * uStrength;
    } else if (uMode < 4.5) {
        // ---- EoLSky ----
        float black = clamp(uStrength, 0.0, 1.0);
        float sun = clamp(uExtra, 0.0, 1.0);
        vec3 sky = texture(uSkyTex, vec2(0.5, uv.y)).rgb;
        base += sky * black * 0.8;
        base *= (1.0 - black * 0.35);
        if (sun > 0.001) {
            base += sunFx(uvTop, posTop, 0.05 * sqrt(sun), sun);
        }
    } else if (uMode < 5.5) {
        base += vec3(1.0, 0.97, 0.86) * uStrength;
    } else if (uMode < 6.5) {
        // ---- Sun.fx ----
        base += sunFx(uvTop, posTop, max(uStrength, 0.0001), uExtra) * uIntensity;
    } else if (uMode < 7.5) {
        // ---- Cycloid：源 PreDraw 的 screenTargetSwap（黄色全屏闪光）+ Hide 圆洞 ----
        vec2 q = (uvTop - posTop) * vec2(uAspect, 1.0);
        if (length(q) > uExtra) {
            base += vec3(1.0, 1.0, 0.59) * uStrength;
        }
    }

    if (uMode > 7.5) {
        // ---- KScreen0：isWarp 特效的屏幕扭曲（不是可见的环） ----
        vec2 q = (uvTop - posTop) * vec2(uAspect, 1.0);
        float d = length(q);
        float band = smoothstep(max(uRadius - uExtra, 0.0), uRadius, d)
                   * (1.0 - smoothstep(uRadius, uRadius + uExtra, d));
        float ang = uStrength * band;
        float ca = cos(ang), sa = sin(ang);
        vec2 w = vec2(q.x * ca - q.y * sa, q.x * sa + q.y * ca);
        vec2 wuv = posTop + w / vec2(uAspect, 1.0);
        base = blScreen(vec2(wuv.x, 1.0 - wuv.y)).rgb;
    }

    fragColor = vec4(clamp(base, 0.0, 1.0), 1.0);
}


