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
uniform float uHead;

vec4 sampleCell(vec4 cell, vec2 uv, float wrapU) {
    ivec2 atlasSize = textureSize(uAtlas, 0);
    ivec2 origin = ivec2(round(cell.xy * vec2(atlasSize)));
    ivec2 size = max(ivec2(1), ivec2(round(cell.zw * vec2(atlasSize))));
    vec2 q = wrapU > 0.5 ? fract(uv) : clamp(uv, vec2(0.0), vec2(1.0));
    // PointWrap wraps BOTH axes (side=1 returns the top edge); PointClamp
    // selects the last texel at 1. Avoid the old arbitrary atlas padding,
    // which removed up to 3 texels from thin texture/color strips.
    ivec2 local = clamp(ivec2(floor(q * vec2(size))), ivec2(0), size - 1);
    local.y = size.y - 1 - local.y;
    return texelFetch(uAtlas, origin + local, 0);
}

void main() {
    if (uHead > 0.5) {
        // XNA AlphaBlend uses ONE, ONE_MINUS_SRC_ALPHA. These sprites
        // deliberately have alpha=0 and add their RGB without masking the scene.
        fragColor = sampleCell(uCellShape, vUV, 0.0) * vColor;
        return;
    }
    vec3 coord = vec3(vUV, vAux.x);
    vec4 c = sampleCell(uCellShape, vec2(coord.x + vAux.y, coord.y), 1.0);
    c += sampleCell(uCellImage, vec2(1.0 - coord.x * 5.0, coord.y), 0.0);
    c *= coord.z;
    c *= sampleCell(uCellColor, vec2(clamp(c.r, 0.0, 1.0), 0.5), 0.0);

    float a = clamp(vColor.a, 0.0, 1.0);
    fragColor = vec4(c.rgb * a, c.a * a);
}
