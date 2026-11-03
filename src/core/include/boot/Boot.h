#pragma once

#include <atomic>
#include <mutex>
#include <string>
#include <vector>

#include "boot/RefValidator.h"
#include "pylon/Status.h"

namespace pylon::boot {

enum class Phase : int {
    Idle = 0,
    KernelLoaded = 1,
    EngineReady = 2,
    RefsValidated = 3,
    Failed = 4,
};

const char* PhaseName(Phase phase);

class Boot {
public:
    static Boot& Instance();

    Status OnKernelLoaded();
    Status OnEngineReady(const std::string& signal);
    Status ValidateRefs(const std::vector<std::string>& specs, RefReport* report);

    Phase phase() const { return phase_.load(); }
    std::string FailureReason() const;

private:
    Boot() = default;

    void Fail(const std::string& reason);

    std::atomic<Phase> phase_{Phase::Idle};
    mutable std::mutex mutex_;
    std::string failure_reason_;
};

}  // namespace pylon::boot
