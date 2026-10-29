// 层：内核层 / boot —— 实现
#include "boot/Boot.h"

#include <mutex>
#include <unistd.h>

#include "il2cpp/Il2CppRuntime.h"
#include "pylon/Log.h"

namespace pylon::boot {
namespace {
std::mutex g_mutex;
}  // namespace

const char* PhaseName(Phase phase) {
    switch (phase) {
        case Phase::Idle: return "Idle";
        case Phase::KernelLoaded: return "KernelLoaded";
        case Phase::EngineReady: return "EngineReady";
        case Phase::RefsValidated: return "RefsValidated";
        case Phase::Failed: return "Failed";
    }
    return "Unknown";
}

Boot& Boot::Instance() {
    static Boot instance;
    return instance;
}

/// 失败原因可能在探测线程上被改写，所以按值返回并在同一把锁下拷贝。
std::string Boot::FailureReason() const {
    std::lock_guard<std::mutex> lock(g_mutex);
    return failure_reason_;
}

void Boot::Fail(const std::string& reason) {
    phase_ = Phase::Failed;
    failure_reason_ = reason;
    LOGE("boot", "阶段=Failed：%s", reason.c_str());
}

Status Boot::OnKernelLoaded() {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (phase_ == Phase::Failed) return Status::Fail(failure_reason_);
    if (phase_ != Phase::Idle) return Status::Ok();  // 幂等

    Status bound = il2cpp::Runtime::Instance().Bind();
    if (!bound) {
        Fail("il2cpp 符号绑定失败：" + bound.reason + "（停用 Mod，不继续解析）");
        return Status::Fail(failure_reason_);
    }

    phase_ = Phase::KernelLoaded;
    LOGI("boot", "阶段=KernelLoaded（符号就绪，尚未触碰任何 IL2CPP API）");
    return Status::Ok();
}

Status Boot::OnEngineReady(const std::string& signal) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (phase_ == Phase::Failed) return Status::Fail(failure_reason_);
    if (phase_ == Phase::EngineReady || phase_ == Phase::RefsValidated) return Status::Ok();

    if (phase_ != Phase::KernelLoaded) {
        return Status::Fail("阶段不符：引擎就绪事件早于内核加载完成（当前 " +
                            std::string(PhaseName(phase_)) + "）");
    }

    LOGI("boot", "引擎就绪信号：%s", signal.c_str());

    // 到这里才允许第一次调 IL2CPP API（docs/02 §6：il2cpp_init 期间调用会 SIGSEGV）。
    Status attached = il2cpp::Runtime::Instance().Attach();
    if (!attached) {
        Fail("attach 到 IL2CPP 域失败：" + attached.reason);
        return Status::Fail(failure_reason_);
    }

    phase_ = Phase::EngineReady;
    LOGI("boot", "阶段=EngineReady（已 attach，可以按名解析）");

    auto images = il2cpp::Runtime::Instance().Images();
    LOGI("core", "已加载程序集 %zu 个", images.size());
    for (size_t i = 0; i < images.size() && i < 8; ++i) {
        LOGI("core", "  image[%zu] %s", i, images[i].c_str());
    }
    return Status::Ok();
}

Status Boot::ValidateRefs(const std::vector<std::string>& specs, RefReport* report) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (phase_ == Phase::Failed) return Status::Fail(failure_reason_);
    if (phase_ != Phase::EngineReady && phase_ != Phase::RefsValidated) {
        return Status::Fail("阶段不符：引擎未就绪就要求校验引用（当前 " +
                            std::string(PhaseName(phase_)) + "）");
    }

    // 镜像注册与 il2cpp_init 返回之间有个小窗口：域已就绪但 Assembly-CSharp 还没注册，
    // 一次校验会整批失败、把 Mod 全停（实测：装完 APK 的第一次冷启动偶发）。重试到镜像就绪。
    constexpr int kAttempts = 10;
    constexpr int kGapMs = 200;
    RefReport result;
    for (int attempt = 1; attempt <= kAttempts; ++attempt) {
        result = RefValidator::Validate(specs);
        if (result.AllOk()) break;
        if (attempt < kAttempts) {
            LOGW("refs", "第 %d 次校验未过（失败 %zu 条）：镜像可能还没就绪，%dms 后重试",
                 attempt, result.FailCount(), kGapMs);
            usleep(static_cast<useconds_t>(kGapMs) * 1000);
        }
    }
    if (report != nullptr) *report = result;

    for (const auto& item : result.items) {
        if (item.ok) {
            LOGI("refs", "✓ %s —— %s", item.spec.c_str(), item.detail.c_str());
        } else {
            LOGE("refs", "✗ %s —— %s", item.spec.c_str(), item.detail.c_str());
        }
    }

    if (!result.AllOk()) {
        // 逐条原因已经在上面的 ✗ 日志里了，这里只留一句汇总，避免日志重复一大段。
        Fail("引用校验失败 " + std::to_string(result.FailCount()) + " 条（共 " +
             std::to_string(result.items.size()) + " 条）");
        return Status::Fail(failure_reason_);
    }

    phase_ = Phase::RefsValidated;
    LOGI("boot", "阶段=RefsValidated（引用校验通过 %zu/%zu）", result.OkCount(), result.items.size());
    return Status::Ok();
}

}  // namespace pylon::boot
