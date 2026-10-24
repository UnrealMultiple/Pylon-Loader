// 羲和魔杖 —— 激光柱屏幕效果（Pylon ScreenShader，GLSL ES 3.00）。
//
// 素材全部来自本机 MEAC 文件夹：
//   uBeamTex      = MEAC/Images/tex3.png             EoLBigLaser 外层柱
//   uCoreTex      = MEAC/Images/tex0.png             内层柱
//   uArcTex       = MEAC/Items/ModWeapons/Arc        XiheMagicControler.PreDraw 的耀斑
//   uStarTex      = MEAC/Images/Extra_89.png         十字辉光
//   uRingTex      = MEAC/Projectiles/Circle          落点柔光
//   uRingStripTex = MEAC/Images/MagicCircle/tex1     MagicRing 的文字环带
//
// 圆环：按 MEAC.Entities.MagicRing.Draw 的 3D 顶点公式逐像素反解：
//   顶点 = Projection(center + (R cosθ, ±width, R sinθ), screenCenter, viewZ=1000)
//   R = 110*scale*ai0, width = 10*scale*ai0；投影是 k=1000/(1000-z) 的透视缩放。
//   → 近侧环带窄、远侧宽，真正有立体感，而不是平面椭圆。
// 彩虹：按 XiheMagicControler.PreDraw 的 Arc.png 上下各 3 片（±90+j*3，红/绿/蓝）+
//   彩虹色按左右铺在 Arc 的翅膀形状里（不再是几何圆弧）。
#include <bl/screen>
#include <bl/noise>
#include <bl/color>
#include <bl/shapes>

uniform sampler2D uBeamTex;
uniform sampler2D uCoreTex;
uniform sampler2D uArcTex;
uniform sampler2D uStarTex;
uniform sampler2D uRingAnnulus;

uniform float uTexOn;
uniform float uCharge;
uniform float uBeam;
uniform float uFlash;
uniform float uSky;
uniform float uShake;
uniform float uImpact;
uniform float uHalfWidth;
uniform float uBeamWidth;
uniform float uBeamX;
uniform float uBeamCenterV;
uniform float uBeamAge;
uniform float uRingEarlyX;
uniform float uRingEarlyV;
uniform float uRingLateX;
uniform float uRingLateV;

const float TAU = 6.28318530718;
const float VIEW_Z = 1000.0;

// MEAC Colorize.fx 的同款。
vec3 colorize(vec3 base, vec3 col, float saturation, float offset) {
    float lum = dot(base, vec3(0.3, 0.6, 0.1)) + offset;
    vec3 mid = mix(vec3(0.5), col, saturation);
    return lum < 0.5 ? mix(vec3(0.0), mid, lum * 2.0)
                     : mix(mid, vec3(1.0), (lum - 0.5) * 2.0);
}

// MEAC MagicRing 的环带已按 tex1（sX=3，V 0..1）卷成环形图，这里只做透视压扁后采样。
vec3 magicRing(vec2 uv, float ringX, float ringV, float R, float halfW, float alpha, float spin) {
    if (uTexOn < 0.5) return vec3(0.0);

    vec2 p = (uv - vec2(ringX, ringV)) * uResolution;
    float squash = clamp(abs(ringV - 0.5) * 1.7 + 0.10, 0.10, 0.55);
    p.y /= squash;

    // MEAC MagicRing 的 t（-timeForVisualEffects*0.01）让环上的文字绕圈转。
    float a = uTime * 0.35 + spin;
    float ca = cos(a), sa = sin(a);
    p = vec2(p.x * ca - p.y * sa, p.x * sa + p.y * ca);

    vec2 tuv = p / (2.0 * max(R, 1.0)) + 0.5;
    if (tuv.x < 0.0 || tuv.x > 1.0 || tuv.y < 0.0 || tuv.y > 1.0) return vec3(0.0);

    vec4 t = texture(uRingAnnulus, tuv);
    vec3 c = t.rgb * vec3(1.0, 0.92, 0.78);   // 贴近光柱的金白
    return c * alpha * 2.2;
}
void main() {
    vec2 uv = vUV;
    float sh = uResolution.y;

    // 震屏
    vec2 shake = vec2(blNoise(vec2(uTime * 13.0, 3.1)) - 0.5,
                      blNoise(vec2(uTime * 11.0, 7.7)) - 0.5) * uShake * 2.0;
    vec3 col = blScreen(clamp(uv + shake, vec2(0.002), vec2(0.998))).rgb;

    // 天幕（紫金，无星点）
    if (uSky > 0.001) {
        float top = smoothstep(0.42, 1.02, uv.y);
        float aur = blFbm(vec2(uv.x * 2.0 + uTime * 0.02, uv.y * 2.6 - uTime * 0.035), 3);
        vec3 skyCol = mix(vec3(0.55, 0.10, 0.55), vec3(0.95, 0.55, 0.25), aur);
        col += skyCol * top * uSky * 0.30;
    }

    vec2 centerPix = vec2((uv.x - uCenter.x) * uAspect * sh, (uv.y - uCenter.y) * sh);
    vec2 beamRel = vec2((uv.x - uBeamX) * uAspect, uv.y - uImpact) * vec2(sh, sh);

    // ---------------- 蓄力：Arc.png 的彩虹翅膀（MEAC XiheMagicControler.PreDraw）----------------
    if (uCharge > 0.001) {
        vec2 d = vec2((uv.x - uCenter.x) * uAspect, uv.y - uCenter.y);
        float flareAlpha = (uCharge + sin(uTime * 0.8) * 0.1) * 0.4;

        // 原版：Arc 144x255，scale=1，中心 ±90；只做红/绿/蓝三色描边，不再自己铺彩虹。
        if (uTexOn > 0.5) {
            for (int j = 0; j < 3; j++) {
                vec3 tint = j == 0 ? vec3(1.0, 0.0, 0.0)
                          : (j == 1 ? vec3(0.0, 0.8, 0.0) : vec3(0.0, 0.0, 0.6));
                float off = 90.0 + float(j) * 3.0;

                vec2 rp = centerPix - vec2(0.0, off); vec2 upUv = vec2(rp.y, -rp.x) / vec2(144.0, 255.0) + 0.5;
                vec2 rn = centerPix + vec2(0.0, off); vec2 dnUv = vec2(-rn.y, rn.x) / vec2(144.0, 255.0) + 0.5;
                if (abs(upUv.x - 0.5) < 0.5 && abs(upUv.y - 0.5) < 0.5)
                    col += tint * max(max(texture(uArcTex, upUv).r, texture(uArcTex, upUv).g), texture(uArcTex, upUv).b) * flareAlpha * 0.32;
                if (abs(dnUv.x - 0.5) < 0.5 && abs(dnUv.y - 0.5) < 0.5)
                    col += tint * max(max(texture(uArcTex, dnUv).r, texture(uArcTex, dnUv).g), texture(uArcTex, dnUv).b) * flareAlpha * 0.32;
            }

            // Extra_89 十字辉光（图二中间那道亮白十字）
            vec2 starUv = centerPix / 144.0;
            if (abs(starUv.x) < 0.5 && abs(starUv.y) < 0.5) {
                vec4 s1 = texture(uStarTex, starUv + 0.5);
                vec4 s2 = texture(uStarTex, vec2(starUv.y, -starUv.x) + 0.5);
                col += vec3(1.0, 0.96, 0.82) * (s1.a + s2.a) * uCharge * 0.14;
            }

            // 着色器自己的光晕/旋转光线（不是贴图）：让手里那团光有 breathing 的 bloom。
            float gr = length(d);
            float ga = atan(d.y, d.x);
            float rays = pow(max(0.0, cos(ga * 5.0 + uTime * 1.3)), 26.0) * exp(-gr * 13.0);
            col += vec3(1.0, 0.80, 0.42) *
                   (exp(-gr * 20.0) * 0.22 + exp(-gr * 8.0) * 0.06 + rays * 0.08) * uCharge;
        } else {
            float r = length(d);
            col += vec3(1.0, 0.93, 0.72) * exp(-r * 8.0) * uCharge * 0.5;
        }
    }

    // ---------------- 激光柱 ----------------
    if (uBeam > 0.001) {
        float w = max(uBeamWidth, 0.0015);
        float dx = (uv.x - uBeamX) * uAspect;

        if (uTexOn > 0.5) {
            float v = clamp(dx / w * 0.5 + 0.5, 0.0, 1.0);
            float u3 = uv.y * (sh / 2000.0) + uTime * 0.9375
                     + sin(uv.y * 37.0 + uTime * 3.0) * 0.004;   // 轻微扰动，避免死贴图感
            float u0 = uv.y * (sh / 5000.0) + uTime * 1.875;

            vec4 t3 = texture(uBeamTex, vec2(u3, v));
            vec4 t0 = texture(uCoreTex, vec2(u0, v));

            vec3 c3 = colorize(t3.rgb, vec3(0.5, 0.35, 0.1), 0.8, 0.0);
            vec3 c0 = colorize(t0.rgb, vec3(0.5, 0.35, 0.1), 0.8, 0.0);

            col += c3 * 0.78 * uBeam;
            col += c0 * 0.90 * uBeam;
            col += vec3(1.0, 0.87, 0.55) * t0.r * exp(-pow(abs(dx) / (w * 0.16), 2.0)) * 0.25 * uBeam;
        } else {
            float core = exp(-pow(abs(dx) / (w * 0.24), 2.0));
            float inner = exp(-pow(abs(dx) / (w * 0.62), 2.0));
            col += vec3(1.0, 0.97, 0.87) * core * 1.1 * uBeam;
            col += vec3(1.0, 0.72, 0.28) * inner * 0.7 * uBeam;
        }

        // 着色器泛光：柱体外围的金色光晕（不是贴图本身）
        float haloA = exp(-pow(abs(dx) / (w * 1.9), 2.0));
        float haloB = exp(-pow(abs(dx) / (w * 3.4), 2.0));
        col += vec3(1.0, 0.68, 0.24) * haloA * 0.42 * uBeam;
        col += vec3(1.0, 0.45, 0.12) * haloB * 0.18 * uBeam;

        float cap = smoothstep(0.80, 1.02, uv.y);
        col += vec3(1.0, 0.95, 0.75) * cap * exp(-pow(abs(dx) / (w * 0.30), 2.0)) * 0.45 * uBeam;

        float ir = length(beamRel * vec2(1.0, 2.4));
        col += vec3(1.0, 0.86, 0.50) * (exp(-ir * 0.010) * 0.40 + exp(-ir * 0.0035) * 0.18) * uBeam;
    }

    // ---------------- 环（在柱之后，带真 3D 投影）----------------
    if (uBeamAge > 0.0) {
        // 柱生成时三环：依次展开（timeLeft 360/340/320），ai0 = 0.7 / 0.4 / 0.9。
        for (int i = 0; i < 3; i++) {
            float start = float(i) * 20.0;
            float t = (uBeamAge - start) / 90.0;
            if (t <= 0.0 || t >= 1.0) continue;

            float expand = smoothstep(0.0, 0.22, t);
            float rs = expand * 4.0;                       // MagicRing.scale 0 → 4
            float ai0 = i == 0 ? 0.70 : (i == 1 ? 0.40 : 0.90);
            float R = 110.0 * rs * ai0;
            float hw = max(10.0 * rs * ai0, 8.0);
            float alpha = (1.0 - smoothstep(0.55, 1.0, t)) * 0.75;
            float centerV = uBeamCenterV - (80.0 + float(i) * 260.0) / sh;
            col += magicRing(uv, uBeamX, centerV, R, hw, alpha, float(i) * 0.2);
        }

        // 点火后三环：常驻，ai0 = 0.85 / 0.6 / 1.1，scale 4 → 5。
        float lit = clamp((uBeamAge - 110.0) / 25.0, 0.0, 1.0);
        if (lit > 0.0) {
            float persist = smoothstep(0.0, 0.35, uBeam);
            for (int i = 0; i < 3; i++) {
                float rs = 4.0 + 0.5 * smoothstep(0.8, 1.0, lit);
                float ai0 = i == 0 ? 0.85 : (i == 1 ? 0.60 : 1.10);
                float R = 110.0 * rs * ai0;
                float hw = max(10.0 * rs * ai0, 10.0);
                float centerV = uBeamCenterV - (80.0 + float(i) * 260.0) / sh;
                col += magicRing(uv, uBeamX, centerV, R, hw, 0.95 * persist, 0.4 + float(i) * 0.2);
            }
        }
    }

    col += vec3(1.0, 0.96, 0.86) * uFlash * 1.15;
    fragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}











