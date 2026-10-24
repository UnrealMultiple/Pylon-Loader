#include "pylon/mod/ModApi.h"

#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <sys/mman.h>
#include <unistd.h>

namespace {

std::mutex code_page_mutex;

// The kernel is built with -fno-exceptions (see src/core/build.ps1). Never use
// try/catch or throwing containers here: every failure path returns a status and
// every allocation is checked.
constexpr size_t kMaxCodePages = 1024;
constexpr size_t kMaxMappings = 65536;

struct CodePageRecord {
    uint64_t address;
    size_t bytes;
    bool sealed;
};

CodePageRecord code_pages[kMaxCodePages];
size_t code_page_count = 0;

size_t HostPageSize() {
    static const size_t size = [] {
        const long value = sysconf(_SC_PAGESIZE);
        return value > 0 ? static_cast<size_t>(value) : 4096;
    }();
    return size;
}

uint64_t AlignUp(uint64_t value, size_t alignment) {
    const uint64_t rest = value % alignment;
    return rest == 0 ? value : value + (alignment - rest);
}

struct MapInterval { uint64_t start; uint64_t end; };

struct MapList {
    MapInterval* entries = nullptr;
    size_t count = 0;
    size_t capacity = 0;

    ~MapList() { std::free(entries); }
};

bool CollectMappings(MapList& out) {
    out.entries = static_cast<MapInterval*>(std::malloc(sizeof(MapInterval) * kMaxMappings));
    if (out.entries == nullptr) return false;
    out.capacity = kMaxMappings;
    std::FILE* file = std::fopen("/proc/self/maps", "r");
    if (file == nullptr) return false;
    char line[512];
    while (std::fgets(line, sizeof(line), file) != nullptr) {
        uint64_t start = 0;
        uint64_t end = 0;
        if (std::sscanf(line, "%lx-%lx", &start, &end) != 2) continue;
        if (out.count == out.capacity) {
            std::fclose(file);
            return false;
        }
        out.entries[out.count++] = {start, end};
    }
    std::fclose(file);
    return true;
}

void* TryMapAt(uint64_t candidate, size_t bytes) {
    const auto wanted = reinterpret_cast<void*>(candidate);
    void* mapped = mmap(wanted, bytes, PROT_READ | PROT_WRITE,
                        MAP_PRIVATE | MAP_ANONYMOUS | MAP_FIXED_NOREPLACE, -1, 0);
    if (mapped != MAP_FAILED) {
        if (mapped == wanted) return mapped;
        munmap(mapped, bytes);
        return nullptr;
    }
    if (errno != EINVAL) return nullptr;
    // Older kernels (e.g. Android 9 / Linux 4.9) do not know
    // MAP_FIXED_NOREPLACE. Try the address as a plain hint instead of falling back to
    // MAP_FIXED: a concurrent mmap between the /proc/self/maps snapshot and this call
    // must never be silently replaced.
    mapped = mmap(wanted, bytes, PROT_READ | PROT_WRITE,
                  MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (mapped == MAP_FAILED) return nullptr;
    if (mapped != wanted) {
        munmap(mapped, bytes);
        return nullptr;
    }
    return mapped;
}

class CandidateSearch {
public:
    CandidateSearch(uint64_t near, size_t bytes, const MapList& used)
        : near_(near), bytes_(bytes), used_(used) {}

    uint64_t Next(bool& progress) {
        progress = true;
        while (steps_ < kMaxSteps) {
            const unsigned long long offset =
                static_cast<unsigned long long>(steps_) * static_cast<unsigned long long>(step());
            for (; direction_ < 2; ++direction_) {
                const bool viable = direction_ == 0 ? near_ <= UINT64_MAX - offset
                                                    : near_ >= offset;
                if (viable) {
                    const uint64_t candidate = direction_ == 0 ? near_ + offset : near_ - offset;
                    if (InWindow(candidate) && !Occupied(candidate)) {
                        ++direction_;
                        return candidate;
                    }
                }
            }
            direction_ = 0;
            ++steps_;
        }
        progress = false;
        return 0;
    }

private:
    static constexpr uint32_t kMaxSteps = 30720;

    size_t step() const { return HostPageSize(); }
    bool InWindow(uint64_t candidate) const {
        const long long delta = static_cast<long long>(candidate) - static_cast<long long>(near_);
        const long long reach = PylonCodePage_ReachBytes;
        return delta >= -reach && delta <= reach;
    }
    bool Occupied(uint64_t candidate) const {
        for (size_t i = 0; i < used_.count; ++i) {
            const auto& interval = used_.entries[i];
            if (candidate < interval.end && candidate + bytes_ > interval.start) return true;
        }
        return false;
    }

    uint64_t near_;
    size_t bytes_;
    const MapList& used_;
    uint32_t steps_ = 0;
    uint32_t direction_ = 0;
};

CodePageRecord* FindRecord(uint64_t pageAddress) {
    for (size_t i = 0; i < code_page_count; ++i) {
        if (code_pages[i].address == pageAddress) return &code_pages[i];
    }
    return nullptr;
}

bool AddRecord(uint64_t address, size_t bytes) {
    if (code_page_count == kMaxCodePages) return false;
    code_pages[code_page_count++] = {address, bytes, false};
    return true;
}

}

PYLON_MOD_EXPORT int32_t PylonMod_ReserveCodePage(uint64_t nearAddress, uint32_t byteSize,
                                                  uint64_t* outPageAddress) {
    if (outPageAddress == nullptr || byteSize == 0 || byteSize > PylonCodePage_MaxBytes ||
        nearAddress == 0) {
        return PylonPatch_InvalidArgument;
    }
    std::lock_guard<std::mutex> lock(code_page_mutex);
    const size_t pageSize = HostPageSize();
    const size_t bytes = static_cast<size_t>(AlignUp(byteSize, pageSize));
    MapList used;
    if (!CollectMappings(used)) return PylonPatch_BackendFailure;

    const uint64_t anchor = AlignUp(nearAddress, pageSize);
    CandidateSearch search(anchor, bytes, used);
    for (;;) {
        bool progress = false;
        const uint64_t candidate = search.Next(progress);
        if (!progress) return PylonPatch_BackendFailure;
        if (TryMapAt(candidate, bytes) != nullptr) {
            if (!AddRecord(candidate, bytes)) {
                munmap(reinterpret_cast<void*>(candidate), bytes);
                return PylonPatch_BackendFailure;
            }
            *outPageAddress = candidate;
            return PylonPatch_Ok;
        }
    }
}

PYLON_MOD_EXPORT int32_t PylonMod_ReleaseCodePage(uint64_t pageAddress) {
    if (pageAddress == 0) return PylonPatch_InvalidArgument;
    std::lock_guard<std::mutex> lock(code_page_mutex);
    for (size_t i = 0; i < code_page_count; ++i) {
        if (code_pages[i].address != pageAddress) continue;
        // The caller must guarantee that no site or thread can branch into this page.
        if (munmap(reinterpret_cast<void*>(pageAddress), code_pages[i].bytes) != 0) {
            return PylonPatch_BackendFailure;
        }
        code_pages[i] = code_pages[--code_page_count];
        return PylonPatch_Ok;
    }
    return PylonPatch_Conflict;
}

PYLON_MOD_EXPORT int32_t PylonMod_SealCodePage(uint64_t pageAddress, const uint8_t* bytes,
                                               uint32_t byteLength) {
    if (bytes == nullptr || byteLength == 0 || byteLength > PylonCodePage_MaxBytes ||
        pageAddress == 0) {
        return PylonPatch_InvalidArgument;
    }
    std::lock_guard<std::mutex> lock(code_page_mutex);
    CodePageRecord* record = FindRecord(pageAddress);
    if (record == nullptr || record->sealed) return PylonPatch_Conflict;
    if (byteLength > record->bytes) return PylonPatch_InvalidArgument;
    std::memcpy(reinterpret_cast<void*>(pageAddress), bytes, byteLength);
    if (mprotect(reinterpret_cast<void*>(pageAddress), record->bytes,
                 PROT_READ | PROT_EXEC) != 0) {
        return PylonPatch_ProtectionFailed;
    }
    __builtin___clear_cache(reinterpret_cast<char*>(pageAddress),
                            reinterpret_cast<char*>(pageAddress + byteLength));
    record->sealed = true;
    return PylonPatch_Ok;
}
