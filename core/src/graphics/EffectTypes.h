#pragma once

#include <string>
#include <vector>

namespace pylon::graphics {

/// 效果画在哪一层：World = 世界相机之后（界面盖在上面）、Screen = 帧末（盖一切）。
enum class EffectLayer : int { World = 0, Screen = 1 };

/// 一步效果读/写哪种纹理。> 0 = 帧内离屏目标（TargetSpec.id）。
constexpr int kTexNone = 0;
constexpr int kTexScreen = -1;    // 已画好的画面拷贝（uScreen）
constexpr int kTexHistory = -2;   // 上一帧的世界（已含效果）

/// 一个 uniform：`components` 个 float 一组；`v` 里多于一组就是数组（uniform vec4 uAlgo[8]）。
struct UniformValue {
    std::string name;
    int components = 1;
    std::vector<float> v;
};

/// 一个 sampler2D 绑定：连到固定纹理（kTex*）或一个离屏目标。
struct TextureBinding {
    std::string name;
    int texture = kTexNone;
};

/// 离屏目标：盖满 viewport（scale = 0.5 就是半分辨率）。临时目标按名字跨帧复用，
/// 可能带上次画剩的旧内容；持久目标保留内容。
struct TargetSpec {
    int id = 0;
    std::string name;
    float scale = 1.f;
    bool persistent = false;
};

/// 网格混合：0 = 直接写（不混合）、1 = AlphaBlend（SrcAlpha, 1-SrcAlpha）、
/// 2 = Additive（SrcAlpha, One）。与 XNA 的 BlendState.NonPremultiplied / Additive 同口径。
enum class MeshBlend : int { None = 0, Alpha = 1, Additive = 2 };

/// 硬件光栅化用的顶点：12 个 float，和 shader 的 attribute 一一对应。
/// x,y = 屏幕分数（0..1，y 向下）；u,v = 纹理坐标（图集坐标）；
/// w,t = Trail.fx 的 coord.z / 每顶点滚动量；其余是顶点色。
struct MeshVertex {
    float x = 0, y = 0;
    float u = 0, v = 0;
    float r = 1, g = 1, b = 1, a = 1;
    float w = 1, t = 0;
    float aux0 = 0, aux1 = 0;
};

/// 一步效果：游戏线程构造，渲染线程执行。坐标是 viewport 的分数（0..1，v 从下往上）；
/// 半径是高度的分数。
///
/// mesh=true 时走硬件几何路径：不读屏幕、不做屏幕拷贝，直接把 verts 作为三角形列表画到
/// 当前目标（默认屏幕），混合由 blend 决定。这是 MEAC 的 DrawUserPrimitives / 精灵批的等价物。
struct EffectDraw {
    int shader = 0;
    EffectLayer layer = EffectLayer::World;
    int dest = 0;                   // 0 = 屏幕；> 0 = 一个目标
    int source = kTexNone;          // shader 的 uSource
    float box[4] = {0, 0, 1, 1};    // x0, y0, x1, y1：画在哪（也从这拷贝屏幕）
    float center[2] = {0.5f, 0.5f};
    float radius = 0.f;
    float intensity = 1.f;
    float progress = 0.f;
    float age = 0.f;                // 效果活了多久（秒）
    float color[4] = {1, 1, 1, 1};
    bool mesh = false;              // 走硬件三角形列表（不读屏幕）
    MeshBlend blend = MeshBlend::None;
    std::vector<MeshVertex> verts;
    std::vector<UniformValue> uniforms;
    std::vector<TextureBinding> textures;
};

/// 一个帧交给一层的内容：按顺序的步骤、用到的目标、要不要上一帧（History）。
struct EffectFrame {
    std::vector<EffectDraw> draws;
    std::vector<TargetSpec> targets;
    bool history = false;

    bool empty() const { return draws.empty(); }
};

}  // namespace pylon::graphics
