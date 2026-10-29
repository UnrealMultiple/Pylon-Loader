// 羲和 Boss —— EoLBigLaser 大光柱（直接复用 samples/XiheMagic 的 XiheBeam.frag）。
//
// C# 侧 Fx/XiheBigLaserFx.cs 只填 uBeam/uBeamX/uImpact/uRings 等；uCharge/uFlash/uSky/uShake 恒 0，
// 所以蓄力翅膀、天空、闪白、Shader 内震屏这几段不会执行（震屏由相机钩子做）。
// 贴图：uBeamTex=tex3、uCoreTex=tex0、uArcTex/uStarTex=Extra_89、uRingStrip=MagicRing。
//
// 下面保留原 XiheMagic 的 3D MagicRing 投影反解与 tex3/tex0 滚动采样（逐行未改）。
#include <bl/screen>
#include <bl/noise>

uniform sampler2D uBeamTex;
uniform sampler2D uCoreTex;
uniform sampler2D uArcTex;
uniform sampler2D uStarTex;
uniform sampler2D uRingStrip;

uniform float uTexOn;
uniform float uCharge;
uniform float uBeam;
uniform float uFlash;
uniform float uSky;
uniform float uShake;
uniform float uImpact;
uniform float uBeamWidth;
uniform float uBeamX;

// 环：每个 vec4 = (屏幕 uv 中心 x, 屏幕 uv 中心 y, R, 半宽)，都以屏幕高度为单位。
uniform vec4 uRings[8];
uniform float uRingCount;
uniform float uRingViewZ;   // 1000 / screenHeight（MEAC 的 viewZ = 1000 世界 px）
uniform float uRingHalo;    // bloom 半径（屏幕高度分数）

const float TAU = 6.28318530718;

// MEAC Colorize.fx 的同款。
vec3 colorize(vec3 base, vec3 col, float saturation, float offset) {
    float lum = dot(base, vec3(0.3, 0.6, 0.1)) + offset;
    vec3 mid = mix(vec3(0.5), col, saturation);
    return lum < 0.5 ? mix(vec3(0.0), mid, lum * 2.0)
                     : mix(mid, vec3(1.0), (lum - 0.5) * 2.0);
}

// MEAC.Entities.MagicRing.Draw 的投影反解。pix = (x, y)，单位屏幕高度，y 向下，原点屏幕中心。
vec3 magicRing(vec2 pix, vec4 ring) {
    if (uTexOn < 0.5) return vec3(0.0);

    float Dx = (ring.x - 0.5) * uAspect;
    float Dy = 0.5 - ring.y;
    float R = ring.z;
    float w = ring.w;
    float vz = uRingViewZ;

    if (R < 1e-5 || w < 1e-7 || R > vz * 0.98) return vec3(0.0);

    float kMin = vz / (vz + R);      // sinθ = -1（远侧）
    float kMax = vz / (vz - R);      // sinθ = +1（近侧）
    float margin = uRingHalo * 4.0;

    // 包围盒剔除：环带的 x/y 一定落在这里面。
    float px0 = min(Dx - R, Dx + R);
    float px1 = max(Dx - R, Dx + R);
    float py0 = min(Dy - w, Dy + w);
    float py1 = max(Dy - w, Dy + w);
    if (pix.x < min(px0 * kMin, px0 * kMax) - margin ||
        pix.x > max(px1 * kMin, px1 * kMax) + margin) return vec3(0.0);
    if (pix.y < min(py0 * kMin, py0 * kMax) - margin ||
        pix.y > max(py1 * kMin, py1 * kMax) + margin) return vec3(0.0);

    // R cosθ + (X R / vz) sinθ = X - Dx   →   φ ± acos(c/m)
    float b = pix.x * R / vz;
    float c = pix.x - Dx;
    float m = sqrt(R * R + b * b);
    if (m < 1e-6 || abs(c) > m) return vec3(0.0);

    float phi = atan(b, R);
    float dth = acos(clamp(c / m, -1.0, 1.0));

    vec3 acc = vec3(0.0);
    for (int s = 0; s < 2; s++) {
        float th = phi + (s == 0 ? dth : -dth);
        float k = vz / (vz - R * sin(th));       // 透视缩放（MEAC 的 Projection 的 k）
        float y0 = k * (Dy - w);                 // 上边（V=0）
        float y1 = k * (Dy + w);                 // 下边（V=1）
        float lo = min(y0, y1);
        float hi = max(y0, y1);

        float v;        // 0 = 上边，1 = 下边
        float halo;     // bloom 权重
        bool inside = (pix.y >= lo && pix.y <= hi);
        float sigma = max(uRingHalo, 1e-4);
        if (inside) {
            v = (pix.y - y0) / max(y1 - y0, 1e-6);
            halo = 0.05;                     // 环带内只留一点暖光，黑底保持透明
        } else {
            float dist = (pix.y < lo) ? (lo - pix.y) : (pix.y - hi);
            // 环带外只做一小圈金色辉光（MEAC 的 bloom 是 1/3 分辨率的小模糊）。
            halo = 0.12 * exp(-(dist * dist) / (2.0 * sigma * sigma));
            if (halo < 0.02) continue;
            v = (pix.y < lo) ? 0.0 : 1.0;
        }

        v = clamp(v, 0.0, 1.0);
        // 顶点色 = 白 * k（上边第一个顶点原版再 +0.05）；XNA 的 Color 会截到 1。
        float kc = clamp(mix(k + 0.05, k, v), 0.0, 1.0);
        // sX = 3，t = -timeForVisualEffects * 0.01（每 tick 0.01 → 每秒 0.6）。
        float u = fract(3.0 * th / TAU - uTime * 0.6);
        // Pylon 的纹理 v=0 在图像底部，原版 DX 的 v=0 在顶部，所以这里翻一下。
        vec4 t = texture(uRingStrip, vec2(u, 1.0 - v));

        // 原版是加色混合：src.rgb * src.a = tex.rgb * tex.a * kc²。
        vec3 base = t.rgb * t.a * kc * kc;
        // MagicRing.useBloom = true，bloomColor = HsvToRgb(50) = (1, 0.833, 0)。
        vec3 gold = vec3(1.0, 0.833, 0.0);
        if (inside) {
            acc += base;                 // 环带本体：纹理的 4px 白描边 + 符文
            acc += base * gold * halo;   // 环带内一点点暖光
        } else {
            acc += base * gold * halo;   // 环带外只有金色辉光，不能再刷一遍白色描边
        }
    }
    return acc;
}

void main() {
    // 震屏：MEAC 的 EffectPlayer 每 tick 给 Main.screenPosition 加一个「随机方向 * screenShake」，
    // 也就是整个画面（世界 + 光柱 + 环）一起抖，而且每 tick 换一次方向。
    float tick = floor(uTime * 60.0);
    float ang = blHash(vec2(tick, 3.7)) * TAU;
    vec2 shake = vec2(cos(ang) / uAspect, sin(ang)) * uShake;
    vec2 uv = vUV + shake;
    float sh = uResolution.y;

    vec3 col = blScreen(clamp(uv, vec2(0.002), vec2(0.998))).rgb;

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
        float beamMask = abs(dx) > w * 2.2 ? 0.0 : exp(-pow(abs(dx) / (w * 2.2), 2.0));

        if (uTexOn > 0.5) {
            float v = clamp(dx / w * 0.5 + 0.5, 0.0, 1.0);
            float u3 = uv.y * (sh / 2000.0) + uTime * 0.9375
                     + sin(uv.y * 37.0 + uTime * 3.0) * 0.004;   // 轻微扰动，避免死贴图感
            float u0 = uv.y * (sh / 5000.0) + uTime * 1.875;

            vec4 t3 = texture(uBeamTex, vec2(u3, v));
            vec4 t0 = texture(uCoreTex, vec2(u0, v));

            vec3 c3 = colorize(t3.rgb, vec3(0.5, 0.35, 0.1), 0.8, 0.0);
            vec3 c0 = colorize(t0.rgb, vec3(0.5, 0.35, 0.1), 0.8, 0.0);

            // v 在柱体外会被 clamp 到贴图边缘；没有这个 mask 时，边缘颜色会铺满整个 box，
            // 整屏发白。柱体 2.2w 以外不再叠贴图。
            col += c3 * 0.78 * uBeam * beamMask;
            col += c0 * 0.90 * uBeam * beamMask;
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
        col += vec3(1.0, 0.86, 0.50) * (exp(-ir * 0.010) * 0.40 + exp(-ir * 0.0035) * 0.18) * uBeam * beamMask;
    }

    // ---------------- MagicRing（在柱之后；真 3D 投影，逐像素反解）----------------
    if (uTexOn > 0.5 && uRingCount > 0.5) {
        vec2 pix = vec2((uv.x - 0.5) * uAspect, 0.5 - uv.y);
        for (int i = 0; i < 8; i++) {
            if (float(i) >= uRingCount) break;
            col += magicRing(pix, uRings[i]);
        }
    }

    col += vec3(1.0, 0.96, 0.86) * uFlash * 1.15;
    fragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}




