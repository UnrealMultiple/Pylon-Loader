// 层：内核层 / boot —— C# 方法绑定自检
// "能解析到方法"与"能真的调起来并拿回返回值"是两件事，这个自检把后者钉死：
// 覆盖 值类型返回 / 字符串往返（传托管对象 + 读托管字符串）/ 故意抛异常 三条路径。
// 每一步都**先打日志再执行**，崩在哪一步日志里看得见（见 docs/13）。
#pragma once

#include <string>

namespace pylon::boot {

/// 跑一遍调用自检，返回多行文本（直接进日志）。只在 attach 之后可调用。
std::string RunBindSelfTest();

}  // namespace pylon::boot
