// 层:内核层 / il2cpp —— AAPCS64 直调帧,与托管侧 PylonCallFrame 保持布局一致。
#pragma once

#include <cstddef>
#include <cstdint>

namespace pylon::il2cpp {

/// 返回值形状；值必须与托管侧 PylonRetKind 一致。
enum class RetKind : int32_t {
    Int = 0,      // x0（void、指针、bool、枚举都走这里）
    F32 = 1,      // s0
    F64 = 2,      // d0
    S16 = 3,      // x0/x1（<=16 字节、非 HFA 的 struct）
    H1F = 4, H2F = 5, H3F = 6, H4F = 7,    // float HFA：s0-s3
    H1D = 8, H2D = 9, H3D = 10, H4D = 11,  // double HFA：d0-d3
    Indirect = 12,                         // >16 字节：x8 指向调用方缓冲
};

constexpr int32_t kMaxIndirectReturn = 512;

/// 直调帧。托管侧 PylonCallFrame 必须逐字段同布局。
struct CallFrame {
    intptr_t x[16];      // x0-x7 + 8 个栈槽（AAPCS64 第 9..16 个整数参数）
    uint64_t d[8];       // d0-d7 原始位（float 在低 32 位）
    void* indirect;      // >16 字节返回：调用方缓冲
    int32_t ret;         // RetKind
    int32_t retSize;     // RetKind::Indirect 的有效字节数
    uint64_t out[4];     // x0/x1 或 s0-s3/d0-d3 的原始位
    void* method;        // MethodInfo*
    void* exc;           // 出参：捕获到的 Il2CppException*（无异常为 null）
};

constexpr size_t kCallFrameBytes = sizeof(CallFrame);
static_assert(sizeof(CallFrame) == 256);
static_assert(offsetof(CallFrame, out) == 208);
static_assert(offsetof(CallFrame, exc) == 248);

}  // namespace pylon::il2cpp
