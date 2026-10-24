// 层：内核层 / boot —— 探测实现
#include "boot/Probe.h"

#include <chrono>
#include <unistd.h>
#include <new>
#include <cstdio>

#include "boot/Boot.h"
#include "boot/EngineInitHook.h"
#include "il2cpp/Il2CppRuntime.h"
#include "pylon/Log.h"

namespace pylon::boot {
namespace {

/// 睡眠：用 usleep 而不是 std::this_thread（不需要把 <thread> 拉进来，也没有无异常模式的风险）。
void SleepMs(int ms) {
    if (ms > 0) usleep(static_cast<useconds_t>(ms) * 1000);
}

}  // namespace

Probe& Probe::Instance() {
    static Probe instance;
    return instance;
}

Status Probe::Start(const std::vector<std::string>& refs, const ProbeConfig& config) {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (started_) return Status::Ok();  // 幂等：Java 侧可能重复调用
    }

    // 先挂钩 il2cpp_init：装不上就拒绝启动探测（宁可没有 Mod，也不要猜时机把游戏搞崩）。
    const Status hooked = InstallEngineInitHook();
    if (!hooked) return hooked;
    LOGI("probe", "就绪探测启动：等 il2cpp_init 返回（上限 %dms），之后每 %dms 试一次，最多 %d 次",
         config.init_timeout_ms, config.gap_ms, config.attempts);

    // 用 pthread 而不是 std::thread：本库 `-fno-exceptions`，libc++ 在构造失败时走
    // `__throw_system_error` → 直接 abort。这里要的是"拒绝 + 写原因"。
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
    started_ = true;  // 只有线程真的起来了才算启动
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
    // ① 等 il2cpp_init 返回（hook 没装上就拒绝启动）。
    if (!EngineInitHookInstalled()) {
        ProbeResult refused;
        refused.failure_reason = "il2cpp_init 未被挂钩，无法确定何时可以碰 IL2CPP（拒绝启动探测）";
        Publish(refused);
        return;
    }
    for (int waited = 0; !EngineInitDone() && waited < config.init_timeout_ms; waited += 50) {
        SleepMs(50);
    }
    if (!EngineInitDone()) {
        ProbeResult timeout;
        timeout.failure_reason = "等 il2cpp_init 返回超时（" + std::to_string(config.init_timeout_ms) + "ms）";
        Publish(timeout);
        return;
    }

    auto& runtime = il2cpp::Runtime::Instance();
    ProbeResult result;

    // ② 稀疏轮询。每一步都在"已经过了静置期"之后，且间隔足够大。
    for (int attempt = 1; attempt <= config.attempts; ++attempt) {
        if (runtime.EngineLooksReady()) {
            char signal[256];
            std::snprintf(signal, sizeof(signal),
                          "探测命中 il2cpp_domain_get() != null（il2cpp_init 返回后第 %d 次尝试）",
                          attempt);
            result.signal = signal;
            LOGI("probe", "%s", result.signal.c_str());

            // 到这里才 attach（第一次真正调 IL2CPP API）。
            Status attached = Boot::Instance().OnEngineReady(result.signal);
            if (!attached) {
                result.ok = false;
                result.failure_reason = Boot::Instance().FailureReason();
                Publish(result);
                return;
            }

            RefReport report;
            Status validated = Boot::Instance().ValidateRefs(refs, &report);
            result.ok = static_cast<bool>(validated);
            result.report = report.Describe();
            result.failure_reason = validated ? std::string() : Boot::Instance().FailureReason();
            Publish(result);
            return;
        }
        if (attempt < config.attempts) {
            SleepMs(config.gap_ms);
        }
    }

    // ③ 探测失败：**不调 API、不猜**，如实报告。Mod 停用比随机崩溃好（docs/README 纪律）。
    result.ok = false;
    result.failure_reason = "就绪探测失败：静置 " + std::to_string(config.settle_ms) +
                            "ms 后尝试 " + std::to_string(config.attempts) +
                            " 次仍未拿到 il2cpp 域（Mod 停用）";
    LOGE("probe", "%s", result.failure_reason.c_str());
    Publish(result);
}

}  // namespace pylon::boot
