#pragma once

#include <cstdint>

namespace pylon::graphics {

/// 导出标记：内核整体 -fvisibility=hidden，只有这些符号对外。
#define PYLON_FX_EXPORT extern "C" __attribute__((visibility("default")))

/// 状态值（与 ShaderState 对应）：0 待编译 / 1 就绪 / -1 失败。
constexpr int32_t kPylonFxShaderPending = 0;
constexpr int32_t kPylonFxShaderReady = 1;
constexpr int32_t kPylonFxShaderFailed = -1;

/// 固定纹理 id（与 EffectTypes.h 的 kTex* 对应）。
constexpr int32_t kPylonFxTexNone = 0;
constexpr int32_t kPylonFxTexScreen = -1;   // uScreen：已画好的画面拷贝
constexpr int32_t kPylonFxTexHistory = -2;  // 上一帧世界（已含效果）

/// 统计（布局与 RenderStats 一致；顺序字段，C# 直接 Sequential 封送）。
struct PylonFxStats {
    // 相机计数 / 帧计数 / 累计时间
    int64_t worldPostRender;
    int64_t uiPostRender;
    int64_t otherPostRender;
    int64_t draws;
    int64_t frames;
    int64_t markersIssued;
    int64_t markerHits;
    int64_t screenFrames;
    int64_t renderCpuNs;
    int64_t markerCpuNs;
    int64_t gpuNs;
    int64_t gpuSamples;
    int64_t gpuFrameNs;
    int64_t gpuFrameSamples;
    int64_t shadowChecks;
    int64_t shadowMismatches;
    int64_t boxPixels;
    // GL 现场
    int32_t drawFbo;
    int32_t readFbo;
    int32_t viewport[4];
    int32_t samples;
    int32_t blitError;
    int32_t copyFallbacks;
    // 标志
    int32_t hookInstalled;
    int32_t hasContext;
    int32_t markerReady;
    int32_t gpuTimer;
};

}  // namespace pylon::graphics

// ---------------------------------------------------------------------------
// 生命周期
// ---------------------------------------------------------------------------

/// 装渲染 hooks（GL hooks + Camera.FireOnPostRender）。幂等。游戏线程、引擎稳定后调。
/// 1 成功 / 0 失败（类没找齐或 hook 失败；日志有原因）。失败后效果不画，Mod 照常。
PYLON_FX_EXPORT int32_t PylonFx_Install();

/// GLES 是否已加载并且 hook 装上了（1/0）。失败排查第一步。
PYLON_FX_EXPORT int32_t PylonFx_Available();

// ---------------------------------------------------------------------------
// 着色器
// ---------------------------------------------------------------------------

/// 注册片元着色器（GLSL ES 3.00，可带 `#include <bl/...>`）。同名字同源码返回同一 id。
/// 返回 id（>0）；0 = 参数无效。
PYLON_FX_EXPORT int32_t PylonFx_ShaderRegister(const char* name, const char* source);

/// 查询编译状态。`state` 输出 kPylonFxShader*；`error` 可空，失败时给错误文本（带 Mod 文件行号）。
PYLON_FX_EXPORT int32_t PylonFx_ShaderStatus(int32_t id, int32_t* state, char* error, int32_t errorCap);

// ---------------------------------------------------------------------------
// 纹理（RGBA 由 C# 解码后传入；行序 v=0 在图像底部）
// ---------------------------------------------------------------------------

/// 登记一张 RGBA 纹理。同 key 同过滤返回同一 id；0 = 参数无效。上传发生在渲染线程首次使用。
/// outWidth/outHeight 可空。
PYLON_FX_EXPORT int32_t PylonFx_TextureLoad(const char* key, int32_t width, int32_t height,
                                            const uint8_t* rgba, int32_t linear, int32_t repeat,
                                            int32_t* outWidth, int32_t* outHeight);

// ---------------------------------------------------------------------------
// 提交一帧（builder：Begin → AddTarget/AddDraw(+Uniform/Texture) → End）
// ---------------------------------------------------------------------------

/// 开始一帧。history=1 时效果能读上一帧世界（kPylonFxTexHistory）。1 ok。
PYLON_FX_EXPORT int32_t PylonFx_SubmitBegin(int32_t history);

/// 登记一个离屏目标（id>0 供 AddDraw 的 dest/source 用）。scale 0<..<=1（0.5=半分辨率）。
/// persistent 目前只用于文档语义（临时目标按名复用）。1 ok。
PYLON_FX_EXPORT int32_t PylonFx_SubmitAddTarget(int32_t id, const char* name, float scale, int32_t persistent);

/// 加一步绘制，返回 draw 句柄（>0，供 AddUniform/AddTexture 用）；0 = 参数无效（shader<=0）。
/// layer：0=世界（界面之下）/ 1=屏幕（盖一切）。dest：0=屏幕、>0=目标 id。source：0/固定/目标。
/// box/center/color 可空（取默认）。半径与中心都是 viewport 分数，半径按高度。
PYLON_FX_EXPORT int32_t PylonFx_SubmitAddDraw(int32_t shader, int32_t layer, int32_t dest, int32_t source,
                                              const float* box, const float* center, float radius,
                                              float intensity, float progress, float age, const float* color);

/// 给一步加 uniform：`components` 1-4 个 float 一组，`values` 里 `count` 组（数组 uniform）。
PYLON_FX_EXPORT int32_t PylonFx_SubmitAddUniform(int32_t draw, const char* name, int32_t components,
                                                 const float* values, int32_t count);

/// 给一步加 sampler2D 绑定：`texture` = PylonFx_TextureLoad 的 id / kPylonFxTex* / 目标 id。
PYLON_FX_EXPORT int32_t PylonFx_SubmitAddTexture(int32_t draw, const char* name, int32_t texture);

/// 结束并提交本帧（交给渲染 hooks；没 AddDraw 的帧会被忽略）。
PYLON_FX_EXPORT void PylonFx_SubmitEnd();

// ---------------------------------------------------------------------------
// 诊断
// ---------------------------------------------------------------------------

/// 统计快照（任意线程）。
PYLON_FX_EXPORT void PylonFx_Stats(pylon::graphics::PylonFxStats* out);

/// 请下一整帧 GL 调用 trace 进日志（排帧点问题用）。
PYLON_FX_EXPORT void PylonFx_RequestTrace();
