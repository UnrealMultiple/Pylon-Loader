// 层：内核层 / mod —— SystemStages 中体桥的实现（接口见 mod/SystemBridges.h）。
#include "mod/SystemBridges.h"

#include <atomic>
#include <cmath>
#include <cstring>
#include <mutex>

#include "pylon/mod/ModApi.h"

#if defined(__aarch64__)

extern "C" {
// 桥的尾跳槽：ShadowHook 在补丁生效之前写入跳板指针。
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal0 = nullptr;
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal1 = nullptr;
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal2 = nullptr;
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal3 = nullptr;
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal4 = nullptr;
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal5 = nullptr;
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal6 = nullptr;
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal7 = nullptr;
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal8 = nullptr;
__attribute__((visibility("hidden"))) void* g_pylonSystemOriginal9 = nullptr;

__attribute__((visibility("hidden"))) void blPylonSystemDispatch(unsigned index, void* frame);
}

namespace pylon::mod {

namespace {

struct alignas(16) RegisterFrame {
    uint64_t general[31];
    uint64_t flags;
    alignas(16) uint8_t vectors[32][16];
};
static_assert(sizeof(RegisterFrame) == 768);

std::atomic<SystemStageFn> g_callbacks[kSystemStageCount]{};
std::mutex g_mx;

}  // namespace

/// 染料商阶段（9）传给托管回调的两指针：玩家与奖励池 List（见 SystemBridges.h）。
struct DyeFrame {
    void* player;
    void* pool;
};

}  // namespace pylon::mod

extern "C" __attribute__((visibility("hidden"))) void blPylonSystemDispatch(unsigned index,
                                                                            void* framePtr) {
    if (index >= static_cast<unsigned>(pylon::mod::kSystemStageCount)) return;
    const auto fn = pylon::mod::g_callbacks[index].load(std::memory_order_acquire);
    if (!fn) return;

    auto* frame = static_cast<pylon::mod::RegisterFrame*>(framePtr);
    const unsigned clock = static_cast<unsigned>(pylon::mod::kSystemStageClock);
    const unsigned dye = static_cast<unsigned>(pylon::mod::kSystemStageDyeReward);
    if (index == clock) {
        double value = 0;
        std::memcpy(&value, frame->vectors[0], sizeof(value));
        const double before = value;
        fn(static_cast<int32_t>(index), &value);
        if (value != before && std::isfinite(value) && value >= 0)
            std::memcpy(frame->vectors[0], &value, sizeof(value));
        return;
    }
    if (index == dye) {
        // 0xef0 时点 x19 = this(Player)、x21 = 奖励池 List；
        // 池改写在托管侧（Clear/Add 走生成代理），寄存器值本身不动。
        pylon::mod::DyeFrame dye{reinterpret_cast<void*>(frame->general[19]),
                                 reinterpret_cast<void*>(frame->general[21])};
        fn(static_cast<int32_t>(index), &dye);
        return;
    }
    fn(static_cast<int32_t>(index), nullptr);
}

namespace pylon::mod {

namespace {

#define PYLON_SYSTEM_SAVE_FRAME                                                     \
    "sub sp, sp, #768\n"                                                            \
    "stp x0, x1, [sp, #0]\n"                                                        \
    "stp x2, x3, [sp, #16]\n"                                                       \
    "stp x4, x5, [sp, #32]\n"                                                       \
    "stp x6, x7, [sp, #48]\n"                                                       \
    "stp x8, x9, [sp, #64]\n"                                                       \
    "stp x10, x11, [sp, #80]\n"                                                     \
    "stp x12, x13, [sp, #96]\n"                                                     \
    "stp x14, x15, [sp, #112]\n"                                                    \
    "stp x16, x17, [sp, #128]\n"                                                    \
    "stp x18, x19, [sp, #144]\n"                                                    \
    "stp x20, x21, [sp, #160]\n"                                                    \
    "stp x22, x23, [sp, #176]\n"                                                    \
    "stp x24, x25, [sp, #192]\n"                                                    \
    "stp x26, x27, [sp, #208]\n"                                                    \
    "stp x28, x29, [sp, #224]\n"                                                    \
    "str x30, [sp, #240]\n"                                                         \
    "mrs x9, nzcv\n"                                                                \
    "str x9, [sp, #248]\n"                                                          \
    "stp q0, q1, [sp, #256]\n"                                                      \
    "stp q2, q3, [sp, #288]\n"                                                      \
    "stp q4, q5, [sp, #320]\n"                                                      \
    "stp q6, q7, [sp, #352]\n"                                                      \
    "stp q8, q9, [sp, #384]\n"                                                      \
    "stp q10, q11, [sp, #416]\n"                                                    \
    "stp q12, q13, [sp, #448]\n"                                                    \
    "stp q14, q15, [sp, #480]\n"                                                    \
    "stp q16, q17, [sp, #512]\n"                                                    \
    "stp q18, q19, [sp, #544]\n"                                                    \
    "stp q20, q21, [sp, #576]\n"                                                    \
    "stp q22, q23, [sp, #608]\n"                                                    \
    "stp q24, q25, [sp, #640]\n"                                                    \
    "stp q26, q27, [sp, #672]\n"                                                    \
    "stp q28, q29, [sp, #704]\n"                                                    \
    "stp q30, q31, [sp, #736]\n"

#define PYLON_SYSTEM_RESTORE_FRAME                                                  \
    "ldp q0, q1, [sp, #256]\n"                                                      \
    "ldp q2, q3, [sp, #288]\n"                                                      \
    "ldp q4, q5, [sp, #320]\n"                                                      \
    "ldp q6, q7, [sp, #352]\n"                                                      \
    "ldp q8, q9, [sp, #384]\n"                                                      \
    "ldp q10, q11, [sp, #416]\n"                                                    \
    "ldp q12, q13, [sp, #448]\n"                                                    \
    "ldp q14, q15, [sp, #480]\n"                                                    \
    "ldp q16, q17, [sp, #512]\n"                                                    \
    "ldp q18, q19, [sp, #544]\n"                                                    \
    "ldp q20, q21, [sp, #576]\n"                                                    \
    "ldp q22, q23, [sp, #608]\n"                                                    \
    "ldp q24, q25, [sp, #640]\n"                                                    \
    "ldp q26, q27, [sp, #672]\n"                                                    \
    "ldp q28, q29, [sp, #704]\n"                                                    \
    "ldp q30, q31, [sp, #736]\n"                                                    \
    "ldr x9, [sp, #248]\n"                                                          \
    "msr nzcv, x9\n"                                                                \
    "ldp x0, x1, [sp, #0]\n"                                                        \
    "ldp x2, x3, [sp, #16]\n"                                                       \
    "ldp x4, x5, [sp, #32]\n"                                                       \
    "ldp x6, x7, [sp, #48]\n"                                                       \
    "ldp x8, x9, [sp, #64]\n"                                                       \
    "ldp x10, x11, [sp, #80]\n"                                                     \
    "ldp x12, x13, [sp, #96]\n"                                                     \
    "ldp x14, x15, [sp, #112]\n"                                                    \
    "ldp x16, x17, [sp, #128]\n"                                                    \
    "ldp x18, x19, [sp, #144]\n"                                                    \
    "ldp x20, x21, [sp, #160]\n"                                                    \
    "ldp x22, x23, [sp, #176]\n"                                                    \
    "ldp x24, x25, [sp, #192]\n"                                                    \
    "ldp x26, x27, [sp, #208]\n"                                                    \
    "ldp x28, x29, [sp, #224]\n"                                                    \
    "ldr x30, [sp, #240]\n"                                                         \
    "add sp, sp, #768\n"

#define PYLON_SYSTEM_BRIDGE(name, stage, target)                                    \
    __attribute__((naked)) void name() {                                            \
        __asm__ volatile(PYLON_SYSTEM_SAVE_FRAME                                    \
            "mov x1, sp\n" "mov w0, #" #stage "\n" "bl blPylonSystemDispatch\n"     \
            PYLON_SYSTEM_RESTORE_FRAME                                              \
            "adrp x16, " #target "\n" "ldr x16, [x16, :lo12:" #target "]\n"         \
            "br x16\n");                                                            \
    }

PYLON_SYSTEM_BRIDGE(bridge0, 0, g_pylonSystemOriginal0)
PYLON_SYSTEM_BRIDGE(bridge1, 1, g_pylonSystemOriginal1)
PYLON_SYSTEM_BRIDGE(bridge2, 2, g_pylonSystemOriginal2)
PYLON_SYSTEM_BRIDGE(bridge3, 3, g_pylonSystemOriginal3)
PYLON_SYSTEM_BRIDGE(bridge4, 4, g_pylonSystemOriginal4)
PYLON_SYSTEM_BRIDGE(bridge5, 5, g_pylonSystemOriginal5)
PYLON_SYSTEM_BRIDGE(bridge6, 6, g_pylonSystemOriginal6)
PYLON_SYSTEM_BRIDGE(bridge7, 7, g_pylonSystemOriginal7)
PYLON_SYSTEM_BRIDGE(bridge8, 8, g_pylonSystemOriginal8)
PYLON_SYSTEM_BRIDGE(bridge9, 9, g_pylonSystemOriginal9)

#undef PYLON_SYSTEM_BRIDGE
#undef PYLON_SYSTEM_RESTORE_FRAME
#undef PYLON_SYSTEM_SAVE_FRAME

/// 桥的入口按阶段排列（install 按下标取）。
void* const kBridges[kSystemStageCount] = {
    reinterpret_cast<void*>(&bridge0), reinterpret_cast<void*>(&bridge1),
    reinterpret_cast<void*>(&bridge2), reinterpret_cast<void*>(&bridge3),
    reinterpret_cast<void*>(&bridge4), reinterpret_cast<void*>(&bridge5),
    reinterpret_cast<void*>(&bridge6), reinterpret_cast<void*>(&bridge7),
    reinterpret_cast<void*>(&bridge8), reinterpret_cast<void*>(&bridge9),
};

}  // namespace

int32_t SystemBridgeInstall(int32_t stage, void* body, int32_t byte_offset,
                            const uint32_t* expected, void** trampoline) {
    if (stage < 0 || stage >= kSystemStageCount || body == nullptr || byte_offset < 0 ||
        (byte_offset & 3) != 0 || expected == nullptr || trampoline == nullptr)
        return -1;

    // 16 字节指纹不符即版本漂移，绝不挂桥。
    if (std::memcmp(static_cast<const uint8_t*>(body) + byte_offset, expected, 4 * sizeof(uint32_t)) != 0)
        return -2;

    // 尾跳槽直通 ShadowHook：跳板在补丁生效之前写入（桥永远跳得到原指令）。
    void** slot = stage == 0   ? &g_pylonSystemOriginal0
                  : stage == 1 ? &g_pylonSystemOriginal1
                  : stage == 2 ? &g_pylonSystemOriginal2
                  : stage == 3 ? &g_pylonSystemOriginal3
                  : stage == 4 ? &g_pylonSystemOriginal4
                  : stage == 5 ? &g_pylonSystemOriginal5
                  : stage == 6 ? &g_pylonSystemOriginal6
                  : stage == 7 ? &g_pylonSystemOriginal7
                  : stage == 8 ? &g_pylonSystemOriginal8
                               : &g_pylonSystemOriginal9;
    if (PylonMod_HookApply(static_cast<uint8_t*>(body) + byte_offset, kBridges[stage], slot) != 0)
        return -3;

    *trampoline = *slot;
    return 0;
}

void SystemBridgeSubscribe(int32_t stage, SystemStageFn callback) {
    if (stage < 0 || stage >= kSystemStageCount) return;
    std::lock_guard lock(g_mx);
    g_callbacks[stage].store(callback, std::memory_order_release);
}

}  // namespace pylon::mod

#else  // 非 aarch64：桥体无处安放，装与订都按不可用处理。

namespace pylon::mod {

int32_t SystemBridgeInstall(int32_t, void*, int32_t, const uint32_t*, void**) { return -1; }
void SystemBridgeSubscribe(int32_t, SystemStageFn) {}

}  // namespace pylon::mod

#endif
