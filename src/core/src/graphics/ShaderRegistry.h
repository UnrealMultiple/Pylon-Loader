#pragma once

#include "graphics/GlApi.h"

#include <string>
#include <unordered_map>

namespace pylon::graphics {

enum class ShaderState : int { Pending = 0, Ready = 1, Failed = -1 };

/// 前缀（ShaderLibrary）里的标准 uniforms，位置已查好。
struct BuiltinUniforms {
    GLint box = -1, screen = -1, source = -1, screenTexel = -1, sourceTexel = -1, view = -1, resolution = -1;
    GLint aspect = -1, time = -1, age = -1, progress = -1, intensity = -1, center = -1, radius = -1, color = -1;
};

struct CompiledShader {
    GLuint program = 0;
    BuiltinUniforms builtin;
    std::unordered_map<std::string, GLint> custom;
};

class ShaderRegistry {
public:
    // 游戏线程。同名字同源码返回同一个 id。
    static int Register(const std::string& name, const std::string& source);
    // 硬件几何路径的片元着色器（用 mesh 顶点着色器与前缀）。
    static int RegisterMesh(const std::string& name, const std::string& source);
    static ShaderState Status(int id, std::string* error = nullptr);

    // 渲染线程：编译好的程序（首次编译），失败/未就绪返回 nullptr。
    static CompiledShader* Use(int id);
    static GLint Uniform(CompiledShader& shader, const std::string& name);

private:
    static int registerImpl(const std::string& name, const std::string& source, bool mesh);
};

}  // namespace pylon::graphics
