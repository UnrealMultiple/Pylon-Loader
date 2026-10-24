// 层：内核层 / graphics::fx —— 离屏表面
#pragma once

#include <GLES3/gl3.h>

#include <memory>
#include <string>
#include <unordered_map>

namespace pylon::graphics::fx {

class Surface {
public:
    Surface() = default;
    ~Surface();
    Surface(const Surface&) = delete;
    Surface& operator=(const Surface&) = delete;

    /// 尺寸不变就复用；false = FBO 不完整（已写日志）。
    bool Ensure(GLsizei width, GLsizei height);
    void Release();

    GLuint texture() const { return texture_; }
    GLuint fbo() const { return fbo_; }
    GLsizei width() const { return width_; }
    GLsizei height() const { return height_; }
    bool valid() const { return fbo_ != 0; }

private:
    GLuint texture_ = 0, fbo_ = 0;
    GLsizei width_ = 0, height_ = 0;
};

/// 按名字复用（同一个名字 + 同一个尺寸不重建）。
class SurfacePool {
public:
    Surface* Get(const std::string& key, GLsizei width, GLsizei height);
    void Clear();

private:
    std::unordered_map<std::string, std::unique_ptr<Surface>> surfaces_;
};

}  // namespace pylon::graphics::fx
