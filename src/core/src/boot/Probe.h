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
    int init_timeout_ms = 120'000;
    int attempts = 16;
    int gap_ms = 100;
    int upper_gap_ms = 2'500;
};

struct ProbeResult {
    bool finished = false;
    bool ok = false;
    std::string signal;
    std::string failure_reason;
    std::string report;
};

class Probe {
public:
    static Probe& Instance();

    Status Start(const std::vector<std::string>& refs, const ProbeConfig& config);
    bool AwaitResult(int timeout_ms, ProbeResult* result);
    ProbeResult Snapshot();

private:
    Probe() = default;
    void ThreadMain(std::vector<std::string> refs, ProbeConfig config);
    void Publish(const ProbeResult& result);

    static void* ThreadEntry(void* self);

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
