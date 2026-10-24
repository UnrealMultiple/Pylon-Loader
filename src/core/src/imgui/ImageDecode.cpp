// 层：内核层 / imgui —— 图片解码（供壁纸底图/扰动图与屏幕效果纹理使用）
//
// 为什么放在内核里而不是用 UnityEngine.ImageConversion：
//   1) IL2CPP 没为 Texture2D.GetRawTextureData<byte>() 生成 AOT 代码，调用必抛
//      ExecutionEngineException（"no ahead of time code was generated"）；
//   2) Texture2D.GetPixels() 等非泛型读取接口在 Il2CppInterop 代理里没有暴露；
//   3) 4K 图走 Color[] 回读会产生 130MB+ 的托管分配。
// 直接对文件字节做解码，返回 RGBA（首行为图像顶部），由托管侧一次性拷走并释放。

#define STB_IMAGE_IMPLEMENTATION
#define STBI_ONLY_JPEG
#define STBI_ONLY_PNG
#define STBI_NO_STDIO
#include "stb/stb_image.h"

#include <cstdint>

#include "pylon/Log.h"

namespace {

constexpr const char* kStage = "image";

}  // namespace

// 内核默认 -fvisibility=hidden，导出必须显式标记（与 PYLON_IMGUI_EXPORT 同义）。
#define PYLON_IMAGE_EXPORT extern "C" __attribute__((visibility("default")))

PYLON_IMAGE_EXPORT uint8_t* PylonDecodeImage(const uint8_t* data, int32_t length, int32_t* out_width, int32_t* out_height)
{
    if (out_width != nullptr) *out_width = 0;
    if (out_height != nullptr) *out_height = 0;
    if (data == nullptr || length <= 0) return nullptr;

    int width = 0;
    int height = 0;
    int channels = 0;
    stbi_uc* pixels = stbi_load_from_memory(data, length, &width, &height, &channels, 4);
    if (pixels == nullptr) {
        const char* reason = stbi_failure_reason();
        LOGE(kStage, "解码失败：%s（%d 字节）", reason != nullptr ? reason : "未知", length);
        return nullptr;
    }
    if (out_width != nullptr) *out_width = width;
    if (out_height != nullptr) *out_height = height;
    LOGI(kStage, "解码完成 %dx%d（源 %d 通道，%d 字节）", width, height, channels, length);
    return pixels;
}

PYLON_IMAGE_EXPORT void PylonFreeImage(uint8_t* pixels) { stbi_image_free(pixels); }
