// 层：内核层 / boot —— 入口时序与阶段状态机
// 对应 docs/02 §7 的 M2 时序：游戏自己的 .so 先加载（Java 侧）→ libpylon.so 的
// JNI_OnLoad（只绑定符号）→ 等 Java 侧投递"引擎已稳定" → attach 线程 → 引用校验。
// 阶段只前进不回退，重复事件幂等：Java 侧可能因为焦点变化重复触发就绪通知。
#pragma once

#include <atomic>
#include <mutex>
#include <string>
#include <vector>

#include "boot/RefValidator.h"
#include "pylon/Status.h"

namespace pylon::boot {

enum class Phase : int {
    Idle = 0,           // 内核还没开始
    KernelLoaded = 1,   // 符号已绑定（游戏 .so 已加载，Unity 还没起）
    EngineReady = 2,    // 引擎稳定，线程已 attach
    RefsValidated = 3,  // 引用校验通过
    Failed = 4,         // 停在某一步：原因见 FailureReason()
};

const char* PhaseName(Phase phase);

class Boot {
public:
    static Boot& Instance();

    /// libpylon.so 加载完成、符号绑定结果已知时调用（由 JNI nativeStart 触发）。
    Status OnKernelLoaded();

    /// Java 侧确认引擎稳定后调用。**这是唯一会第一次触碰 IL2CPP 的入口。**
    /// @param signal 就绪信号的来源描述（写进日志，便于区分主信号/回退路径）
    Status OnEngineReady(const std::string& signal);

    /// 逐条校验引用；全部通过才算通过。返回报告供 JNI 交给 Java 打日志/停用 Mod。
    Status ValidateRefs(const std::vector<std::string>& specs, RefReport* report);

    /// 阶段（可跨线程读：JNI 线程会在探测线程推进阶段的同时轮询它）。
    Phase phase() const { return phase_.load(); }

    /// 停用原因（按值返回：内部字符串可能正被探测线程改写）。
    std::string FailureReason() const;

private:
    Boot() = default;

    void Fail(const std::string& reason);

    std::atomic<Phase> phase_{Phase::Idle};
    mutable std::mutex mutex_;
    std::string failure_reason_;
};

}  // namespace pylon::boot
