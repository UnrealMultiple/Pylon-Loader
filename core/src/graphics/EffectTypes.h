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

/// 一步效果：游戏线程构造，渲染线程执行。坐标是 viewport 的分数（0..1，v 从下往上）；
/// 半径是高度的分数。
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
