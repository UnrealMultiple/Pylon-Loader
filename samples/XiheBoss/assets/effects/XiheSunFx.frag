// 羲和 Boss —— 太阳（MEAC EoLSky.DrawSun / Sun.fx）的硬件几何版。
//
// 与 MEAC 完全同口径：全屏四边形 + 加色混合，像素着色器逐行照抄 Sun.fx：
//   uv -= uPos; uv.x *= aspect; uv /= dot(uv, uv); uv *= uScale;
//   l = length(uv); col = vec3(l); ang = atan2(uv.y, uv.x);
//   ray = ...; col += ray * 0.1 * uRayAlpha;
//   i = 1 - (r+g+b)/3; col *= lerp(1, uColor(1,0.8,0.1), i);
//   finalColor = col * drawColor
// drawColor = Color.White * alpha，alpha 由 uIntensity 给。源在 Additive 下还会再乘一次
// 输出 alpha，所以这里 rgb 与 a 都乘 alpha（与 MEAC 相同）。
uniform float uSunTime;
uniform float uExtra;   // uRayAlpha

void main() {
    vec2 uv = vUV - uCenter;
    uv.x *= uAspect;
    uv /= max(dot(uv, uv), 1e-6);
    uv *= uRadius;

    float l = length(uv);
    vec3 col = vec3(l);
    float ang = atan(uv.y, uv.x);

    float t = uSunTime;
    float ray = sin(ang * 7.0 + t * 1.5) + cos(ang * 10.0 - t * 2.0);
    ray *= sin(ang * 15.0 + t * 2.0) * 0.5 + 0.5;
    if (ray < 0.0) ray *= 0.8;
    ray *= 0.7;
    col += ray * 0.1 * uExtra;

    float i = 1.0 - (col.r + col.g + col.b) / 3.0;
    col *= mix(vec3(1.0), vec3(1.0, 0.8, 0.1), i);

    float a = clamp(uIntensity, 0.0, 2.0);
    fragColor = vec4(col * a, a);
}
