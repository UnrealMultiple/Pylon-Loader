// Standalone Android/Linux test: compile with src/mod/Arm64Patch.cpp and -Iinclude.
// Run on an ARM64 target to exercise writes; other Linux hosts exercise read/validate and deprecated decode ABI.
#include "pylon/mod/ModApi.h"
#include <cstdio>
#include <cstdlib>
#include <sys/mman.h>
#include <unistd.h>

#define REQUIRE(x) do { if (!(x)) { std::fprintf(stderr, "failed line %d: %s\n", __LINE__, #x); std::abort(); } } while (0)

int main() {
    PylonPatchDiagnostic error{};
    PylonArm64Instruction instruction{};
    // Decoding lives in managed AsmArm64 tests. These exports remain only for ABI compatibility.
    const uint32_t deprecatedWords[] = {0x14000002, 0xd503201f, 0xffffffff};
    for (const uint32_t deprecatedWord : deprecatedWords) {
        instruction = {0, PylonArm64_B, 123, 456};
        REQUIRE(PylonMod_Arm64Decode(0x4000, deprecatedWord, &instruction, &error) == PylonPatch_UnsupportedInstruction);
        REQUIRE(instruction.word == deprecatedWord && instruction.kind == PylonArm64_Unknown);
        REQUIRE(instruction.displacement == 0 && instruction.target == 0);
        REQUIRE(error.status == PylonPatch_UnsupportedInstruction && error.address == 0x4000);
        REQUIRE(error.expected == 0 && error.actual == deprecatedWord && error.systemError == 0);
        uint64_t target = 1;
        REQUIRE(PylonMod_Arm64PcRelative(0x4000, deprecatedWord, &target, &error) == PylonPatch_UnsupportedInstruction);
        REQUIRE(target == 0);
        REQUIRE(error.status == PylonPatch_UnsupportedInstruction && error.address == 0x4000);
        REQUIRE(error.expected == 0 && error.actual == deprecatedWord && error.systemError == 0);
    }
    REQUIRE(PylonMod_Arm64Decode(0, 0x17ffffff, &instruction, nullptr) == PylonPatch_UnsupportedInstruction);
    uint64_t target = 1;
    REQUIRE(PylonMod_Arm64PcRelative(0, 0x17ffffff, &target, nullptr) == PylonPatch_UnsupportedInstruction);
    REQUIRE(target == 0);
    REQUIRE(PylonMod_Arm64Decode(0x4000, 0x14000000, nullptr, &error) == PylonPatch_InvalidArgument);
    REQUIRE(error.status == PylonPatch_InvalidArgument && error.address == 0x4000);
    REQUIRE(PylonMod_Arm64Decode(3, 0x14000000, &instruction, &error) == PylonPatch_InvalidArgument);
    REQUIRE(error.status == PylonPatch_InvalidArgument && error.address == 3);
    REQUIRE(PylonMod_Arm64PcRelative(0x4000, 0x14000000, nullptr, &error) == PylonPatch_InvalidArgument);
    REQUIRE(error.status == PylonPatch_InvalidArgument && error.address == 0x4000);
    target = 1;
    REQUIRE(PylonMod_Arm64PcRelative(3, 0x14000000, &target, &error) == PylonPatch_InvalidArgument);
    REQUIRE(target == 0 && error.status == PylonPatch_InvalidArgument && error.address == 3);
    const size_t pageSize = static_cast<size_t>(sysconf(_SC_PAGESIZE));
    auto* memory = static_cast<uint32_t*>(mmap(nullptr, pageSize, PROT_READ | PROT_WRITE,
        MAP_PRIVATE | MAP_ANONYMOUS, -1, 0));
    REQUIRE(memory != MAP_FAILED);
    memory[0] = 0xd503201f;
    memory[1] = 0xd65f03c0;
    PylonCodeSite site{};
    const auto address = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(memory));
    REQUIRE(PylonMod_CodeSiteCreate(address, 8, &site, &error) == PylonPatch_NotExecutable);
    REQUIRE(mprotect(memory, pageSize, PROT_READ | PROT_EXEC) == 0);
    REQUIRE(PylonMod_CodeSiteCreate(address, 8, &site, &error) == 0);
    uint32_t word;
    REQUIRE(PylonMod_Arm64Read(&site, 0, &word, &error) == 0 && word == 0xd503201f);
    REQUIRE(PylonMod_Arm64Read(&site, 8, &word, &error) == PylonPatch_OutOfBounds);
    REQUIRE(PylonMod_Arm64Read(&site, 1, &word, &error) == PylonPatch_InvalidArgument);
    const uint32_t expected[] = {0xd503201f, 0xd65f03c0};
    REQUIRE(PylonMod_Arm64Validate(&site, 0, expected, 2, &error) == 0);
    REQUIRE(PylonMod_Arm64Validate(&site, 0, expected, 0, &error) == PylonPatch_InvalidArgument);
    REQUIRE(PylonMod_Arm64Validate(&site, 0, expected + 1, 1, &error) == PylonPatch_FingerprintMismatch);
    REQUIRE(error.expected == expected[1] && error.actual == expected[0]);
#if defined(__aarch64__)
    REQUIRE(PylonMod_Arm64Write(&site, 0, 0, expected[1], &error) == PylonPatch_Conflict);
    REQUIRE(PylonMod_Arm64Write(&site, 0, expected[0], expected[1], &error) == 0);
    REQUIRE(PylonMod_Arm64Read(&site, 0, &word, &error) == 0 && word == expected[1]);
    REQUIRE(PylonMod_Arm64Write(&site, 0, expected[1], expected[0], &error) == 0);
#else
    REQUIRE(PylonMod_Arm64Write(&site, 0, expected[0], expected[1], &error) == PylonPatch_Unsupported);
#endif
    // ReadWords must reject the entire interval before producing partial output.
    uint32_t words[] = {0xfeedface, 0xfeedface, 0xfeedface};
    REQUIRE(PylonMod_TryReadWords(&site, 0, words, 3, &error) == PylonPatch_OutOfBounds);
    REQUIRE(words[0] == 0xfeedface && words[1] == 0xfeedface);
    REQUIRE(PylonMod_TryReadWords(&site, UINT32_MAX - 3, words, UINT32_MAX, &error) == PylonPatch_OutOfBounds);
    REQUIRE(PylonMod_TryReadWords(&site, 0, words, 2, &error) == 0);
    REQUIRE(words[0] == expected[0] && words[1] == expected[1]);

    PylonPatchRequestV2 request{};
    request.structSize = sizeof(request);
    request.abiVersion = PylonPatch_AbiV2;
    request.imageBase = address - 0x1000;
    request.methodRva = 0x1000;
    request.methodByteLength = 8;
    request.byteOffset = 0;
    request.expectedBuildIdentityLow = request.authenticatedBuildIdentityLow = 0x1234;
    request.expectedBuildIdentityHigh = request.authenticatedBuildIdentityHigh = 0x5678;
    request.ownerId = 1;
    request.siteId = 2;
    request.expectedWords = reinterpret_cast<uintptr_t>(expected);
    request.expectedWordCount = 2;
    request.replacementWordCount = 1;
    request.replacement = expected[1];
    request.flags = PylonPatchV2_BuildAuthenticated | PylonPatchV2_Quiescent;
    PylonPatchDiagnosticV2 diagnostic{};
    REQUIRE(PylonMod_TryValidateCodeSiteV2(&request, &diagnostic) == 0);
    REQUIRE(diagnostic.structSize == 64 && diagnostic.abiVersion == 2);
    REQUIRE(diagnostic.methodRva == 0x1000 && diagnostic.siteRva == 0x1000);
    REQUIRE(diagnostic.writeState == PylonPatchV2_NotWritten);
    auto invalid = request;
    invalid.structSize--;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_InvalidArgument);
    invalid = request;
    invalid.authenticatedBuildIdentityHigh++;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_BuildMismatch);
    invalid = request;
    invalid.flags = PylonPatchV2_Quiescent;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_BuildMismatch);
    invalid = request;
    invalid.flags = PylonPatchV2_BuildAuthenticated;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_LifecycleFailure);
    invalid = request;
    invalid.expectedWordCount = 1;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_InvalidArgument);
    invalid = request;
    invalid.byteOffset = 8;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_OutOfBounds);
    invalid = request;
    invalid.byteOffset = 1;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_Unaligned);
    invalid = request;
    invalid.methodRva = UINT64_MAX - 3;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_OutOfBounds);
    invalid = request;
    invalid.replacementWordCount = 2;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_Unsupported);
    const uint32_t wrongFingerprint[] = {expected[0], 0};
    invalid = request;
    invalid.expectedWords = reinterpret_cast<uintptr_t>(wrongFingerprint);
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_FingerprintMismatch);
    REQUIRE(diagnostic.byteOffset == 4 && diagnostic.siteRva == 0x1004);
    REQUIRE(diagnostic.expected == 0 && diagnostic.actual == expected[1]);
    REQUIRE(diagnostic.writeState == PylonPatchV2_NotWritten && memory[0] == expected[0]);
#if defined(__aarch64__)
    REQUIRE(PylonMod_TryWritePatchV2(&request, &diagnostic) == 0);
    REQUIRE(diagnostic.writeState == PylonPatchV2_Written && memory[0] == expected[1]);
    REQUIRE(PylonMod_TryWritePatchV2(&request, &diagnostic) == PylonPatch_DuplicateInstall);
    REQUIRE(diagnostic.writeState == PylonPatchV2_NotWritten);
    REQUIRE(PylonMod_TryValidateCodeSiteV2(&request, &diagnostic) == PylonPatch_DuplicateInstall);
    invalid = request;
    invalid.ownerId++;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_Conflict);
    invalid = request;
    invalid.siteId++;
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_Conflict);
    invalid = request;
    invalid.byteOffset = 4; // Reusing an owner's site ID for a new address is a conflict.
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_Conflict);
    invalid = request;
    invalid.replacement = expected[0];
    REQUIRE(PylonMod_TryWritePatchV2(&invalid, &diagnostic) == PylonPatch_Conflict);
    auto secondSite = request;
    secondSite.byteOffset = 4;
    secondSite.siteId = 7;
    secondSite.replacement = expected[0];
    REQUIRE(PylonMod_TryWritePatchV2(&secondSite, &diagnostic) == 0);
    REQUIRE(memory[1] == expected[0]);
    // Executable test body: mov w0,#7; ret, patched to mov w0,#9; ret.
    // Use separate addresses/IDs because v2 registrations last for the process.
    REQUIRE(mprotect(memory, pageSize, PROT_READ | PROT_WRITE) == 0);
    memory[4] = 0x528000e0;
    memory[5] = 0xd65f03c0;
    __builtin___clear_cache(reinterpret_cast<char*>(memory + 4), reinterpret_cast<char*>(memory + 6));
    REQUIRE(mprotect(memory, pageSize, PROT_READ | PROT_EXEC) == 0);
    auto function = reinterpret_cast<int (*)()>(memory + 4);
    REQUIRE(function() == 7);
    const uint32_t executable[] = {memory[4], memory[5]};
    auto executableRequest = request;
    executableRequest.methodRva += 16;
    executableRequest.expectedWords = reinterpret_cast<uintptr_t>(executable);
    executableRequest.siteId = 3;
    executableRequest.replacement = 0x52800120;
    REQUIRE(PylonMod_TryWritePatchV2(&executableRequest, &diagnostic) == 0);
    REQUIRE(function() == 9);
    // A static CodeSite window may coexist with an entry hook, but every byte
    // INSIDE that window must still match the offline expected fingerprint.
    REQUIRE(mprotect(memory, pageSize, PROT_READ | PROT_WRITE) == 0);
    memory[8] = 0x14000001; memory[9] = 0xd503201f;
    memory[10] = 0xd503201f; memory[11] = 0xd65f03c0;
    REQUIRE(mprotect(memory, pageSize, PROT_READ | PROT_EXEC) == 0);
    const uint32_t windowExpected[] = {0xd503201f, 0xd503201f, 0xd503201f, 0xd65f03c0};
    auto window = request;
    window.methodRva += 32; window.methodByteLength = 16; window.expectedWordCount = 4;
    window.byteOffset = 4; window.fingerprintByteOffset = 4; window.fingerprintWordCount = 3;
    window.expectedWords = reinterpret_cast<uintptr_t>(windowExpected); window.siteId = 8;
    window.replacement = 0xd65f03c0;
    REQUIRE(PylonMod_TryValidateCodeSiteV2(&window, &diagnostic) == 0);
    auto badWindow = window; badWindow.fingerprintWordCount = 4;
    REQUIRE(PylonMod_TryWritePatchV2(&badWindow, &diagnostic) == PylonPatch_OutOfBounds);
    const uint32_t wrongWindow[] = {0xd503201f, 0xd503201f, 0, 0xd65f03c0};
    badWindow = window; badWindow.expectedWords = reinterpret_cast<uintptr_t>(wrongWindow);
    REQUIRE(PylonMod_TryWritePatchV2(&badWindow, &diagnostic) == PylonPatch_FingerprintMismatch);
    REQUIRE(diagnostic.byteOffset == 8 && diagnostic.actual == 0xd503201f && diagnostic.expected == 0);
    REQUIRE(PylonMod_TryWritePatchV2(&window, &diagnostic) == 0);
#else
    REQUIRE(PylonMod_TryWritePatchV2(&request, &diagnostic) == PylonPatch_Unsupported);
#endif
    REQUIRE(munmap(memory, pageSize) == 0);
    REQUIRE(PylonMod_TryValidateCodeSiteV2(&request, &diagnostic) == PylonPatch_Unmapped);
    REQUIRE(PylonMod_Arm64Read(&site, 0, &word, &error) == PylonPatch_Unmapped);
    std::puts("ARM64 patch tests passed");
}
