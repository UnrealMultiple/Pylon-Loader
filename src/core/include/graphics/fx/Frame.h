// 层：内核层 / graphics::fx —— 一帧效果的数据模型
//
// 游戏线程构造、渲染线程执行；这里只有数据，没有 GL。
#pragma once

#include <string>
#include <vector>

namespace pylon::graphics::fx {

/// 画在哪一层：World = 世界相机之后（界面盖在上面），Screen = 帧末（盖住一切）。
enum class Layer : int { World = 0, Screen = 1 };

enum class Blend : int { None = 0, Alpha = 1, Additive = 2, Premultiplied = 3 };

/// 纹理 id：0 无、负数固定源、正数本帧目标、<= kFirstTexture 是 Mod 纹理。
constexpr int kTexNone = 0;
constexpr int kTexScreen = -1;
constexpr int kTexHistory = -2;
constexpr int kFirstTexture = -1000;

/// 纹理单元从尾部往回排，Unity 用前面那些。
constexpr int kUnitScreen = 15;
constexpr int kUnitSource = 14;
constexpr int kUnitMod = 13;
constexpr int kUnitModCount = 6;

struct Uniform {
    std::string name;
    int components = 1;
    std::vector<float> values;
};

struct TextureBinding {
    std::string name;
    int texture = kTexNone;
};

/// 离屏目标：按名字跨帧复用，尺寸 = viewport * scale。
struct TargetSpec {
    int id = 0;
    std::string name;
    float scale = 1.f;
    bool persistent = false;
};

/// 硬件几何顶点：屏幕分数位置、图集坐标、顶点色、Trail 辅助量。
struct MeshVertex {
    float x = 0, y = 0;
    float u = 0, v = 0;
    float r = 1, g = 1, b = 1, a = 1;
    float w = 1, t = 0;
    float aux0 = 0, aux1 = 0;
};

struct Draw {
    int shader = 0;
    Layer layer = Layer::World;
    int dest = 0;                 // 0 = 屏幕，> 0 = 目标
    int source = kTexNone;
    Blend blend = Blend::None;
    float box[4] = {0, 0, 1, 1};
    float center[2] = {0.5f, 0.5f};
    float radius = 0.f;
    float intensity = 1.f;
    float progress = 0.f;
    float age = 0.f;
    float color[4] = {1, 1, 1, 1};
    bool mesh = false;            // 三角形列表：不读屏幕、不拷贝
    std::vector<MeshVertex> verts;
    std::vector<Uniform> uniforms;
    std::vector<TextureBinding> textures;
};

struct Frame {
    std::vector<Draw> draws;
    std::vector<TargetSpec> targets;
    bool history = false;

    bool empty() const { return draws.empty() && !history; }
};

/// 画到哪：Unity 的 framebuffer 和它的 viewport。
struct RenderTarget {
    int fbo = 0;
    int viewport[4] = {0, 0, 0, 0};
};

}  // namespace pylon::graphics::fx
