// 层：内核层 / graphics::fx —— 着色器注册与编译
#pragma once

#include <GLES3/gl3.h>

#include <string>
#include <unordered_map>

namespace pylon::graphics::fx {

enum class ShaderState : int { Pending = 0, Ready = 1, Failed = -1 };

/// 前缀里的标准 uniform，链接后查一次。
struct BuiltinUniforms {
    GLint box = -1, screen = -1, source = -1, screenTexel = -1, sourceTexel = -1;
    GLint view = -1, resolution = -1, aspect = -1, time = -1, age = -1;
    GLint progress = -1, intensity = -1, center = -1, radius = -1, color = -1;
};

struct Program {
    GLuint id = 0;
    BuiltinUniforms builtin;
    std::unordered_map<std::string, GLint> custom;
};

class ShaderStore {
public:
    /// 游戏线程。同名字同源码同类型返回同一个 id。
    static int Register(const std::string& name, const std::string& source, bool mesh);
    static ShaderState State(int id, std::string* error = nullptr);

    /// 渲染线程：首次调用时链接，失败返回 nullptr。
    static Program* Use(int id);
    static GLint Uniform(Program& program, const std::string& name);
};

}  // namespace pylon::graphics::fx
