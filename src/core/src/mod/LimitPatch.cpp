#include "mod/LimitPatch.h"

#include <sys/mman.h>
#include <unistd.h>

#include <cstdint>
#include <cstring>

#include "il2cpp/Domain.h"
#include "il2cpp/Invoke.h"

namespace pylon::mod {

void* MethodPointerOf(const il2cpp::MethodInfo* method) {
    void* pointer = nullptr;
    std::memcpy(&pointer, method, sizeof(pointer));
    return pointer;
}

bool WriteInstruction(uint32_t* at, uint32_t insn) {
    const long page_size = sysconf(_SC_PAGESIZE);
    if (page_size <= 0) return false;
    const auto page = reinterpret_cast<uintptr_t>(at) & ~static_cast<uintptr_t>(page_size - 1);
    if (mprotect(reinterpret_cast<void*>(page), static_cast<size_t>(page_size),
                 PROT_READ | PROT_WRITE | PROT_EXEC) != 0) {
        return false;
    }
    *at = insn;
    mprotect(reinterpret_cast<void*>(page), static_cast<size_t>(page_size), PROT_READ | PROT_EXEC);
    __builtin___clear_cache(reinterpret_cast<char*>(at), reinterpret_cast<char*>(at + 1));
    return true;
}

namespace {

constexpr uintptr_t kMaxMethodBytes = 512 * 1024;

uintptr_t MethodEnd(const il2cpp::MethodInfo* method, uintptr_t start) {
    const il2cpp::Symbols& api = il2cpp::Domain::symbols();
    uintptr_t end = start + kMaxMethodBytes;
    if (api.method_get_class == nullptr || api.class_get_methods == nullptr) return end;

    auto* klass = api.method_get_class(method);
    if (klass == nullptr) return end;

    void* iterator = nullptr;
    while (const il2cpp::MethodInfo* other = api.class_get_methods(klass, &iterator)) {
        const auto pointer = reinterpret_cast<uintptr_t>(MethodPointerOf(other));
        if (pointer > start && pointer < end) end = pointer;
    }
    return end;
}

bool IsCompareImmediate(uint32_t insn, uint32_t imm, bool shifted) {
    const uint32_t op = shifted ? 0x7140001Fu : 0x7100001Fu;
    return (insn & 0xFFC0001Fu) == op && ((insn >> 10) & 0xFFFu) == imm;
}

bool IsLimitBranch(uint32_t insn, bool below_too) {
    if ((insn & 0xFF000010u) != 0x54000000u) return false;
    const uint32_t cond = insn & 0xFu;
    if (cond == 0x8 || cond == 0x9 || cond == 0xC || cond == 0xD) return true;
    return below_too && (cond == 0x2 || cond == 0x3 || cond == 0xA || cond == 0xB);
}

bool KeepsFlags(uint32_t insn) {
    if ((insn & 0x0A000000u) == 0x08000000u) return true;
    if ((insn & 0x1F800000u) == 0x13000000u) return true;
    return (insn & 0x7F2003E0u) == 0x2A0003E0u;
}

bool LimitBranchAfter(const uint32_t* at, uintptr_t end, bool below_too) {
    for (int k = 1; k <= 4 && reinterpret_cast<uintptr_t>(at + k) < end; ++k) {
        if (IsLimitBranch(at[k], below_too)) return true;
        if (!KeepsFlags(at[k])) return false;
    }
    return false;
}

}

int PatchCompareLimit(const il2cpp::MethodInfo* method, uint32_t old_limit, uint32_t new_limit,
                      bool below_too, bool shifted) {
    if (method == nullptr || new_limit > 0xFFF || old_limit > 0xFFF) return 0;

    const auto start = reinterpret_cast<uintptr_t>(MethodPointerOf(method));
    if (start == 0) return 0;
    const uintptr_t end = MethodEnd(method, start);

    int patched = 0;
    for (auto* at = reinterpret_cast<uint32_t*>(start);
         reinterpret_cast<uintptr_t>(at + 1) < end; ++at) {
        if (!IsCompareImmediate(*at, old_limit, shifted)) continue;
        if (!LimitBranchAfter(at, end, below_too)) continue;
        const uint32_t insn = (*at & ~(0xFFFu << 10)) | (new_limit << 10);
        if (WriteInstruction(at, insn)) ++patched;
    }
    return patched;
}

}
