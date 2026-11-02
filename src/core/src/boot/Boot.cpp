#include "boot/Boot.h"

#include <unistd.h>

#include "il2cpp/Domain.h"
#include "pylon/Log.h"

namespace pylon::boot {
namespace {

std::mutex g_mutex;

constexpr int kRetryAttempts = 8;
constexpr int kRetryFirstGapMs = 50;
constexpr int kRetryMaxGapMs = 400;

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
    if (phase_ != Phase::Idle) return Status::Ok();

    const Status bound = il2cpp::Domain::Bind();
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

    const auto attached = il2cpp::Domain::Attach();
    if (!attached) {
        Fail("attach 到 IL2CPP 域失败：" + attached.reason);
        return Status::Fail(failure_reason_);
    }

    phase_ = Phase::EngineReady;
    LOGI("boot", "阶段=EngineReady（已 attach，可以按名解析）");

    const auto images = attached.value.Images();
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

    // 域就绪与镜像注册之间有个小窗口，冷启动偶发整批解析失败。
    RefReport result;
    int gap = kRetryFirstGapMs;
    for (int attempt = 1; attempt <= kRetryAttempts; ++attempt) {
        result = RefValidator::Validate(specs);
        if (result.AllOk() || attempt == kRetryAttempts) break;
        LOGW("refs", "第 %d 次校验未过（失败 %zu 条），%dms 后重试", attempt, result.FailCount(), gap);
        usleep(static_cast<useconds_t>(gap) * 1000);
        gap = gap * 2 < kRetryMaxGapMs ? gap * 2 : kRetryMaxGapMs;
    }
    if (report != nullptr) *report = result;

    if (!result.AllOk()) {
        Fail("引用校验失败 " + std::to_string(result.FailCount()) + " 条（共 " +
             std::to_string(result.items.size()) + " 条）");
        return Status::Fail(failure_reason_);
    }

    phase_ = Phase::RefsValidated;
    LOGI("boot", "阶段=RefsValidated（引用校验通过 %zu/%zu）", result.OkCount(), result.items.size());
    return Status::Ok();
}

}  // namespace pylon::boot
