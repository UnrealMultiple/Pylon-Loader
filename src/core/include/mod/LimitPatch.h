#pragma once

#include <cstdint>

namespace pylon::il2cpp {
struct MethodInfo;
}

namespace pylon::mod {

int PatchCompareLimit(const il2cpp::MethodInfo* method, uint32_t old_limit, uint32_t new_limit,
                      bool below_too, bool shifted);

bool WriteInstruction(uint32_t* at, uint32_t insn);

void* MethodPointerOf(const il2cpp::MethodInfo* method);

}
