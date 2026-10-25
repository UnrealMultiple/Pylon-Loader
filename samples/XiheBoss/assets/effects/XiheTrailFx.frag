// 羲和 Boss —— 硬件几何的 MEAC Trail.fx pass 0（Pylon mesh 着色器 / GLSL ES 3.00）。
//
// 逐行照抄 MEAC/Effects/Trail.fx 的 PixelShaderFunction（不乘 input.Color，和源一致）：
//   coord = (u, side, w)
//   c  = tex(uShapeTex, (coord.x + t, coord.y));   // PointWrap
//   c += tex(uImage2,  (1 - coord.x * 5, coord.y)); // Projectile_0（本图集里用全透明 2x2）
//   c *= coord.z;
//   c *= tex(uColorTex, (c.r, 0.5));               // PointClamp
// 每个顶点的 t 由 C# 烘进 vAux.y，所以同一批 trail（不同弹幕的 timeLeft）可以合成一次画。
#include <bl/screen>

uniform sampler2D uAtlas;

uniform vec4 uCellShape;
uniform vec4 uCellImage;
uniform vec4 uCellColor;

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

void main() {
    vec3 coord = vec3(vUV, vAux.x);
    vec4 c = sampleCell(uCellShape, vec2(coord.x + vAux.y, coord.y), 1.0);
    c += sampleCell(uCellImage, vec2(1.0 - coord.x * 5.0, coord.y), 0.0);
    c *= coord.z;
    c *= sampleCell(uCellColor, vec2(clamp(c.r, 0.0, 1.0), 0.5), 0.0);

    float a = clamp(vColor.a, 0.0, 1.0);
    fragColor = vec4(c.rgb * a, c.a * a);
}
