// 羲和 Boss —— 图元着色器（Pylon ScreenShader / GLSL ES 3.00）。
//
// 一次 pass 画一批加色图元：orb（贴图光球）/ beam（长条光束，柱体、激光、长枪都走它）/ ring（环带）。
// 所有世界坐标在 C# 侧换成屏幕 uv（0..1，v=1 在顶部），这里按屏幕高度归一到"像素"再画，
// 所以屏幕比例变化不会把效果拉扁。贴图来自 assets/textures 的 MEAC 原素材图集（uAtlas）。
#include <bl/screen>
#include <bl/noise>

uniform sampler2D uAtlas;

uniform vec4 uOrbA[28];   // xy=屏幕uv中心, z=半径(屏幕高度分数), w=旋转
uniform vec4 uOrbB[28];   // rgba（a 再乘一遍当强度）
uniform vec4 uOrbC[28];   // uv 矩形
uniform vec4 uOrbD[28];   // x=纵向拉伸, y=核心辉光, z/w 备用
uniform float uOrbCount;

uniform vec4 uBeamA[8];   // xy=起点uv, zw=世界方向（单位向量，y 向下）
uniform vec4 uBeamB[8];   // x=长度, y=半宽（屏幕高度分数）, z/w 备用
uniform vec4 uBeamC[8];   // rgb=颜色, a=强度, 第三分量放 scroll 会丢，所以 scroll/repeat 在 D
uniform vec4 uBeamD[8];   // xy=uv 起点（图集）, zw=uv 尺寸；scroll/repeat 另用 uBeamE
uniform vec4 uBeamE[8];   // x=scroll（单位：整条纹理）, y=repeatLen（屏幕高度分数）, z=核心, w=备用
uniform float uBeamCount;

uniform vec4 uRingA[6];   // xy=中心uv, z=半径, w=宽度（都是屏幕高度分数）
uniform vec4 uRingB[6];   // rgba
uniform vec4 uRingC[6];   // uv 矩形
uniform vec4 uRingD[6];   // x=旋转(弧度), y=弧长(0=整圈), z/w 备用
uniform float uRingCount;

uniform float uGlobalAlpha;

const float TAU = 6.28318530718;

vec2 pixOf(vec2 uv) {
    return vec2((uv.x - 0.5) * uAspect, 0.5 - uv.y);
}

vec4 atlas(vec4 cell, vec2 uv) {
    vec2 t = cell.xy + clamp(uv, vec2(0.0015), vec2(0.9985)) * cell.zw;
    return texture(uAtlas, t);
}

// ---------------- orb ----------------
vec3 drawOrb(vec2 pix, vec4 a, vec4 b, vec4 c, vec4 d) {
    float R = a.z;
    if (R < 1e-5) return vec3(0.0);

    vec2 q = pix - pixOf(a.xy);
    float cr = cos(a.w), sr = sin(a.w);
    q = mat2(cr, sr, -sr, cr) * q;
    float sy = max(d.x, 0.05);
    q.y /= sy;

    vec2 uv = q / R * 0.5 + 0.5;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return vec3(0.0);

    vec4 t = atlas(c, uv);
    float m = length(q / R);
    float soft = 1.0 - smoothstep(0.80, 1.0, m);

    vec3 add = t.rgb * t.a * b.rgb * b.a * soft;
    // 核心：不吃贴图，让小球也有"过曝"的白心（MEAC 的 bloom 效果）
    add += b.rgb * b.a * exp(-m * 5.0) * max(d.y, 0.0);
    return add;
}

// ---------------- beam ----------------
vec3 drawBeam(vec2 pix, vec4 a, vec4 b, vec4 c, vec4 d, vec4 e) {
    vec2 s = pixOf(a.xy);
    vec2 dir = vec2(a.z, -a.w);
    float dl = length(dir);
    if (dl < 1e-5) return vec3(0.0);
    dir /= dl;
    vec2 perp = vec2(-dir.y, dir.x);

    vec2 rel = pix - s;
    float along = dot(rel, dir);
    float across = dot(rel, perp);

    float len = b.x;
    float w = b.y;
    if (w < 1e-6) return vec3(0.0);
    if (along < -w * 2.0 || along > len + w * 2.0) return vec3(0.0);

    // UV：u 沿光束滚动，v 横跨宽度
    float repeat = max(e.y, w * 9.0);
    float u = fract(along / repeat + e.x);
    float v = clamp(across / w * 0.5 + 0.5, 0.0, 1.0);
    vec4 t = atlas(d, vec2(u, v));

    // 光束两端柔化，避免"平头"
    float cap = smoothstep(-w * 2.0, 0.0, along) * smoothstep(len + w * 2.0, len, along);

    vec3 base = t.rgb * t.a * c.rgb * c.a * cap;
    // 中轴过曝：exp(-(cross/w*2.6)^2)
    float core = exp(-pow(abs(across) / max(w * 0.55, 1e-5), 2.0));
    base += c.rgb * c.a * core * cap * max(e.z, 0.0);
    return base;
}

// ---------------- ring ----------------
vec3 drawRing(vec2 pix, vec4 a, vec4 b, vec4 c, vec4 d) {
    vec2 q = pix - pixOf(a.xy);
    float r = length(q);
    float R = a.z;
    float W = max(a.w, 1e-5);
    float dist = abs(r - R);
    if (dist > W * 3.0) return vec3(0.0);

    float ang = atan(q.y, q.x);
    if (d.y > 0.001 && d.y < TAU) {
        float rel = mod(ang - d.x + TAU, TAU);
        if (rel > d.y) return vec3(0.0);
        ang = rel / d.y * TAU;
    }
    float u = fract(ang / TAU + d.x / TAU);
    float v = clamp((r - R) / W * 0.5 + 0.5, 0.0, 1.0);
    vec4 t = atlas(c, vec2(u, v));

    float band = 1.0 - smoothstep(0.72, 1.0, dist / (W * 0.5));
    vec3 col = t.rgb * t.a * b.rgb * b.a * band;
    // 环带两侧的一点柔光（MEAC 的 bloom）
    col += b.rgb * b.a * exp(-dist / (W * 1.2)) * 0.18;
    return col;
}

void main() {
    vec2 pix = pixOf(vUV);
    vec3 col = vec3(0.0);

    for (int i = 0; i < 28; i++) {
        if (float(i) >= uOrbCount) break;
        col += drawOrb(pix, uOrbA[i], uOrbB[i], uOrbC[i], uOrbD[i]);
    }
    for (int i = 0; i < 8; i++) {
        if (float(i) >= uBeamCount) break;
        col += drawBeam(pix, uBeamA[i], uBeamB[i], uBeamC[i], uBeamD[i], uBeamE[i]);
    }
    for (int i = 0; i < 6; i++) {
        if (float(i) >= uRingCount) break;
        col += drawRing(pix, uRingA[i], uRingB[i], uRingC[i], uRingD[i]);
    }

    // 加色混合：屏幕底色 + 效果（Pylon 侧这一步的 shader 输出直接覆盖，所以把原图加回来）
    vec3 base = blScreen(vUV).rgb;
    fragColor = vec4(clamp(base + col * uGlobalAlpha, 0.0, 1.0), 1.0);
}




