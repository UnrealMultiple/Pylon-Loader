#pragma once

#include "graphics/GlApi.h"

#include <cstdint>
#include <string>

namespace pylon::graphics {

// Mod 纹理 id 从这往下（-1000、-1001…），避开目标（>0）与固定源（kTexScreen/kTexHistory）。
constexpr int kFirstModTexture = -1000;

struct LoadedTexture {
    GLuint texture = 0;
    GLuint sampler = 0;
    GLint width = 0, height = 0;
};

class TextureRegistry {
public:
    // 游戏线程。同 key 同过滤返回同一个 id；0 = 参数无效（原因进日志）。
    // `rgba` 是 width*height*4 字节（v=0 在图像底部），会被拷走。
    static int Load(const std::string& key, int width, int height, const uint8_t* rgba, bool linear, bool repeat);

    // 渲染线程：上传好的纹理（首次调用时上传），没有/失败返回 nullptr。
    static const LoadedTexture* Use(int id);
};

}  // namespace pylon::graphics
