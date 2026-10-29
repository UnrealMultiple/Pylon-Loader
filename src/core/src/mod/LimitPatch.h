// 层：内核层 / mod —— 编译期类型上限的 ARM64 立即数补丁
// 游戏把 `type > 753` / `type >= 754` 这类比较直接编进机器码（上游 tools/disasm/scan_limits.py
// 逐条核对过）；IL2CPP 没有 IL 可 transpile，Harmony prefix 也拦不住原生内部调用，只能改立即数。
// 与上游 hook/CodePatch.cpp 的 patchCompareLimit 同语义：只改 `cmp wN, #old` 后 1..4 条指令内
// 跟着有符号/无符号"大于/小于"条件跳转的那些；相等比较、无跳转的 cmp 一律不碰。
#pragma once

#include <cstdint>

namespace pylon::il2cpp {
struct MethodInfo;
}

namespace pylon::mod {

/// 把方法体里的 `cmp #oldLimit` 改成 `cmp #newLimit`；返回改了几处（0 = 没找到）。
int PatchCompareLimit(const il2cpp::MethodInfo* method, uint32_t old_limit, uint32_t new_limit,
                      bool below_too, bool shifted);

}  // namespace pylon::mod
