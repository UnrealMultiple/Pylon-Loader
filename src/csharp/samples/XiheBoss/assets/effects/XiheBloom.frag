// 羲和 Boss —— MEAC UseBloom 的移植（Effects/Screen/KawaseBlur + 合成）。
// 三种模式（uMode）：0 = 清黑（辉光层底色），1 = Kawase 模糊（MEAC 3 轮 ×2 pass，
// distance 0..5、uIntensity 1.045、分辨率按 /2.5 目标），2 = 合成（screen + bloom，
// 全屏 pass 不开混合=替换，等价 MEAC 的 SoftAdditive 叠加）。
#include <bl/screen>

uniform sampler2D uBloom;   // 模式 2：模糊完成的辉光层
uniform float uMode;
uniform float uDistance;    // 模式 1：Kawase 采样距离（texel）
uniform float uIntensity;   // 模式 1：MEAC 固定 1.045

void main() {
    if (uMode < 0.5) {
        fragColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }
    if (uMode < 1.5) {
        vec2 d = (vec2(uDistance) + vec2(0.5)) * uSourceTexel;
        vec4 o = blSource(vUV) * 4.0;
        o += blSource(vUV + d);
        o += blSource(vUV - d);
        o += blSource(vUV + vec2(-d.x, d.y));
        o += blSource(vUV - vec2(-d.x, d.y));
        fragColor = o * (uIntensity / 8.0);
        return;
    }
    fragColor = blSource(vUV) + texture(uBloom, vUV);
}
