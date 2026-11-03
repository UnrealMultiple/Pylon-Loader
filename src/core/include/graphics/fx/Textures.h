// 层：内核层 / graphics::fx —— Mod 纹理
#pragma once

#include <GLES3/gl3.h>

#include <cstdint>
#include <string>

namespace pylon::graphics::fx {

struct Texture {
    GLuint texture = 0;
    GLuint sampler = 0;
    GLint width = 0, height = 0;
};

class TextureStore {
public:
    /// 游戏线程。同 key 同过滤返回同一个 id；0 = 参数无效（已写日志）。
    static int Load(const std::string& key, int width, int height, const uint8_t* rgba, bool linear, bool repeat);

    /// 渲染线程：首次调用时上传。没有或上传失败返回 nullptr。
    static const Texture* Use(int id);
};

}  // namespace pylon::graphics::fx
