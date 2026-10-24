#pragma once

#include <string>

namespace pylon::graphics {

class ShaderLibrary {
public:
    static const char* Vertex();

    // 完整片元着色器。每段带 `#line 1 <n>`：Mod 代码是 0 号源，前缀 1，include 2、3…；
    // 驱动报错（"0:12: ..."）经 Describe 换回正确文件。
    // include 不认识：返回空串，原因写进 `error`。
    static std::string BuildFragment(const std::string& source, std::string& error);

    // 编译器日志：把 "源:行" 换成每段的名字。
    static std::string Describe(const std::string& log, const std::string& name, const std::string& source);
};

}  // namespace pylon::graphics
