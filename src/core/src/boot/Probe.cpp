#include "boot/Probe.h"

#include <unistd.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <new>

#include "boot/Boot.h"
#include "boot/EngineInitHook.h"
#include "il2cpp/Domain.h"
#include "pylon/Log.h"

namespace pylon::boot {
namespace {

void SleepMs(int ms) {
    if (ms > 0) usleep(static_cast<useconds_t>(ms) * 1000);
}

bool AwaitEngineInit(int timeout_ms) {
    for (int waited = 0; !EngineInitDone() && waited < timeout_ms; waited += 50) SleepMs(50);
    return EngineInitDone();
}

}  // namespace

Probe& Probe::Instance() {
    static Probe instance;
    return instance;
}

Status Probe::Start(const std::vector<std::string>& refs, const ProbeConfig& config) {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (started_) return Status::Ok();
    }

    const Status hooked = InstallEngineInitHook();
    if (!hooked) return hooked;
    LOGI("probe", "就绪探测启动：等 il2cpp_init 返回（上限 %dms），之后从 %dms 起翻倍到 %dms，最多 %d 次",
         config.init_timeout_ms, config.gap_ms, config.upper_gap_ms, config.attempts);

    auto* args = new (std::nothrow) ThreadArgs{this, refs, config};
    if (args == nullptr) return Status::Fail("分配探测参数失败");

    pthread_attr_t attr;
    if (pthread_attr_init(&attr) != 0) {
        delete args;
        return Status::Fail("pthread_attr_init 失败");
    }
    pthread_t tid;
    const int rc = pthread_create(&tid, &attr, &Probe::ThreadEntry, args);
    pthread_attr_destroy(&attr);
    if (rc != 0) {
        delete args;
        return Status::Fail("无法创建探测线程（rc=" + std::to_string(rc) + "）");
    }
    pthread_detach(tid);

    std::lock_guard<std::mutex> lock(mutex_);
    started_ = true;
    return Status::Ok();
}

void* Probe::ThreadEntry(void* self) {
    auto* args = static_cast<ThreadArgs*>(self);
    Probe* probe = args->probe;
    auto refs = std::move(args->refs);
    const ProbeConfig config = args->config;
    delete args;
    probe->ThreadMain(std::move(refs), config);
    return nullptr;
}

bool Probe::AwaitResult(int timeout_ms, ProbeResult* result) {
    std::unique_lock<std::mutex> lock(mutex_);
    const bool done = done_.wait_for(lock, std::chrono::milliseconds(timeout_ms),
                                     [this] { return result_.finished; });
    if (result != nullptr) *result = result_;
    return done;
}

ProbeResult Probe::Snapshot() {
    std::lock_guard<std::mutex> lock(mutex_);
    return result_;
}

void Probe::Publish(const ProbeResult& result) {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        result_ = result;
        result_.finished = true;
    }
    done_.notify_all();
}

void Probe::ThreadMain(std::vector<std::string> refs, ProbeConfig config) {
    if (!EngineInitHookInstalled()) {
        ProbeResult refused;
        refused.failure_reason = "il2cpp_init 未被挂钩，无法确定何时可以碰 IL2CPP（拒绝启动探测）";
        Publish(refused);
        return;
    }
    if (!AwaitEngineInit(config.init_timeout_ms)) {
        ProbeResult timeout;
        timeout.failure_reason = "等 il2cpp_init 返回超时（" + std::to_string(config.init_timeout_ms) + "ms）";
        Publish(timeout);
        return;
    }

    ProbeResult result;
    int gap = config.gap_ms;
    for (int attempt = 1; attempt <= config.attempts; ++attempt) {
        if (!il2cpp::Domain::Ready()) {
            if (attempt < config.attempts) {
                SleepMs(gap);
                gap = std::min(gap * 2, config.upper_gap_ms);
            }
            continue;
        }

        char signal[256];
        std::snprintf(signal, sizeof(signal),
                      "探测命中 il2cpp_domain_get() != null（il2cpp_init 返回后第 %d 次尝试）", attempt);
        result.signal = signal;
        LOGI("probe", "%s", result.signal.c_str());

        const Status attached = Boot::Instance().OnEngineReady(result.signal);
        if (!attached) {
            result.failure_reason = Boot::Instance().FailureReason();
            Publish(result);
            return;
        }

        RefReport report;
        const Status validated = Boot::Instance().ValidateRefs(refs, &report);
        result.ok = static_cast<bool>(validated);
        result.report = report.Describe();
        result.failure_reason = validated ? std::string() : Boot::Instance().FailureReason();
        Publish(result);
        return;
    }

    result.failure_reason = "就绪探测失败：il2cpp_init 返回后尝试 " + std::to_string(config.attempts) +
                            " 次仍未拿到 il2cpp 域（Mod 停用）";
    LOGE("probe", "%s", result.failure_reason.c_str());
    Publish(result);
}

}  // namespace pylon::boot
