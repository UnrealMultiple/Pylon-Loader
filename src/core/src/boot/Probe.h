// 层：内核层 / boot —— 就绪探测（无 hook 时的"等引擎稳定"）
// 硬约束：`il2cpp_init` **执行期间**调任何 IL2CPP API 都会 SIGSEGV。精确解法是在
// il2cpp_init 上装 hook（需 ShadowHook，离线拿不到），所以这一版走探测：先静置，再
// **稀疏**轮询 `il2cpp_domain_get() != nullptr`，命中才 attach + 校验引用；次数用完就如实失败。
// 关键纪律：**绝不紧密轮询** —— 轮询本身就是在调 API，间隔太密等于放大那个崩溃窗口。
// 参数取值依据见 docs/13。
#pragma once

#include <condition_variable>
#include <mutex>
#include <pthread.h>
#include <string>
#include <vector>

#include "boot/RefValidator.h"
#include "pylon/Status.h"

namespace pylon::boot {

struct ProbeConfig {
    /// 等 il2cpp_init 返回的上限。正常几百毫秒内返回；超时说明 hook 没生效。
    int init_timeout_ms = 120'000;
    /// 兼容旧参数（不再用于等待；时机由 il2cpp_init 决定）。
    int settle_ms = 0;
    /// 最多尝试几次。
    int attempts = 12;
    /// 两次尝试之间的间隔。
    int gap_ms = 2'500;
};

/// 探测结果（给 Java 侧一次性取走）。
struct ProbeResult {
    bool finished = false;
    bool ok = false;
    std::string signal;         // 命中的就绪信号描述
    std::string failure_reason; // ok=false 时的原因
    std::string report;         // 逐条引用校验结果（多行）
};

class Probe {
public:
    static Probe& Instance();

    /// 启动探测线程（幂等）。refs 是**数据**：要解析哪些名字由 Java 侧决定。
    Status Start(const std::vector<std::string>& refs, const ProbeConfig& config);

    /// 阻塞等待探测结束。返回 false = 超时（此时 *result 里 finished 仍为 false）。
    bool AwaitResult(int timeout_ms, ProbeResult* result);

    /// 非阻塞取当前结果。
    ProbeResult Snapshot();

private:
    Probe() = default;
    void ThreadMain(std::vector<std::string> refs, ProbeConfig config);
    void Publish(const ProbeResult& result);

    /// pthread 入口（不用 std::thread：本库 `-fno-exceptions`，构造失败会直接 abort）。
    static void* ThreadEntry(void* self);

    /// 传给 pthread 的参数（线程自己释放）。
    struct ThreadArgs {
        Probe* probe;
        std::vector<std::string> refs;
        ProbeConfig config;
    };

    std::mutex mutex_;
    std::condition_variable done_;
    ProbeResult result_;
    bool started_ = false;
};

}  // namespace pylon::boot
