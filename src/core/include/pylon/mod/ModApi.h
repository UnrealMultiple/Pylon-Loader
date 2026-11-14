#pragma once

#include <cstdint>

namespace pylon::mod {

constexpr int32_t kApiVersionMajor = 0;
constexpr int32_t kApiVersionMinor = 1;

#define PYLON_MOD_EXPORT extern "C" __attribute__((visibility("default")))

}

PYLON_MOD_EXPORT int32_t PylonMod_ApiVersion();

PYLON_MOD_EXPORT void PylonMod_Log(const char* utf8);

PYLON_MOD_EXPORT const char* PylonMod_LastError();

PYLON_MOD_EXPORT void* PylonMod_FindType(const char* qualifiedName);

PYLON_MOD_EXPORT void* PylonMod_MethodPointer(const char* qualifiedName, const char* method,
                                              int32_t argc);

PYLON_MOD_EXPORT int32_t PylonMod_PatchCompareLimit(const char* qualifiedName, const char* method,
                                                    int32_t argc, uint32_t oldLimit,
                                                    uint32_t newLimit, int32_t belowToo,
                                                    int32_t shifted);

PYLON_MOD_EXPORT int32_t PylonMod_GetInt32Field(void* instance, const char* type,
                                                const char* field, int32_t* out);
PYLON_MOD_EXPORT int32_t PylonMod_SetInt32Field(void* instance, const char* type,
                                                const char* field, int32_t value);
PYLON_MOD_EXPORT int32_t PylonMod_GetSingleField(void* instance, const char* type,
                                                 const char* field, float* out);
PYLON_MOD_EXPORT int32_t PylonMod_SetSingleField(void* instance, const char* type,
                                                 const char* field, float value);
PYLON_MOD_EXPORT int32_t PylonMod_GetBooleanField(void* instance, const char* type,
                                                  const char* field, int32_t* out);
PYLON_MOD_EXPORT int32_t PylonMod_SetBooleanField(void* instance, const char* type,
                                                  const char* field, int32_t value);

PYLON_MOD_EXPORT void* PylonMod_GetObjectField(void* instance, const char* type,
                                               const char* field);
PYLON_MOD_EXPORT int32_t PylonMod_SetObjectField(void* instance, const char* type,
                                                 const char* field, void* value);

PYLON_MOD_EXPORT int32_t PylonMod_GetStaticInt32(const char* type, const char* field,
                                                 int32_t* out);
PYLON_MOD_EXPORT int32_t PylonMod_SetStaticInt32(const char* type, const char* field,
                                                 int32_t value);
PYLON_MOD_EXPORT int32_t PylonMod_GetStaticSingle(const char* type, const char* field,
                                                  float* out);
PYLON_MOD_EXPORT int32_t PylonMod_GetStaticBoolean(const char* type, const char* field,
                                                   int32_t* out);
PYLON_MOD_EXPORT void* PylonMod_GetStaticObject(const char* type, const char* field);

PYLON_MOD_EXPORT void* PylonMod_InvokeStatic(const char* type, const char* method,
                                             int32_t argc, void** argv);
PYLON_MOD_EXPORT void* PylonMod_InvokeInstance(void* instance, const char* type,
                                               const char* method, int32_t argc, void** argv);

PYLON_MOD_EXPORT int32_t PylonMod_InvokeStaticInt32(const char* type, const char* method,
                                                    int32_t argc, void** argv, int32_t* out);
PYLON_MOD_EXPORT int32_t PylonMod_InvokeInstanceInt32(void* instance, const char* type,
                                                      const char* method, int32_t argc,
                                                      void** argv, int32_t* out);
PYLON_MOD_EXPORT int32_t PylonMod_InvokeStaticSingle(const char* type, const char* method,
                                                     int32_t argc, void** argv, float* out);
PYLON_MOD_EXPORT int32_t PylonMod_InvokeInstanceSingle(void* instance, const char* type,
                                                       const char* method, int32_t argc,
                                                       void** argv, float* out);

PYLON_MOD_EXPORT int32_t PylonMod_HadException();
PYLON_MOD_EXPORT const char* PylonMod_ExceptionText();

PYLON_MOD_EXPORT int32_t PylonMod_IsGameThread();
PYLON_MOD_EXPORT int64_t PylonMod_GameThreadId();
PYLON_MOD_EXPORT int64_t PylonMod_CurrentThreadId();

PYLON_MOD_EXPORT void* PylonMod_NewString(const char* utf8);

PYLON_MOD_EXPORT int32_t PylonMod_ReadString(void* managedString, char* out, int32_t outSize);

PYLON_MOD_EXPORT int32_t PylonMod_HookApply(void* target, void* detour, void** trampoline);
/// 同 PylonMod_HookApply，但把 ShadowHook 返回的原始跳板直接发布到持久槽，供中体桥无窗口尾跳。
PYLON_MOD_EXPORT int32_t PylonMod_HookApplyPublished(void* target, void* detour,
                                                     void** originSlot, void** trampoline);

PYLON_MOD_EXPORT int32_t PylonMod_HookRemove(void* target);

PYLON_MOD_EXPORT int32_t PylonMod_HookAvailable();

PYLON_MOD_EXPORT int32_t PylonMod_PatchInstruction(void* body, int32_t byteOffset,
                                                   uint32_t expected, uint32_t replacement);

// ARM64 patch ABI v1. All functions return a PylonPatchStatus (0 = success).
// CodeSite bounds are supplied by the caller, not inferred method boundaries.
// Callers must keep mappings alive and quiesce executing threads while patching.
enum PylonPatchStatus : int32_t {
    PylonPatch_Ok = 0, PylonPatch_InvalidArgument = 1, PylonPatch_OutOfBounds = 2,
    PylonPatch_Unmapped = 3, PylonPatch_NotExecutable = 4, PylonPatch_FingerprintMismatch = 5,
    PylonPatch_Unsupported = 6, PylonPatch_ProtectionFailed = 7,
    PylonPatch_ProtectionRestoreFailed = 8,
    // Validate fingerprints describe a build; write fingerprints detect competing patches.
    PylonPatch_BuildMismatch = 9, PylonPatch_Conflict = 10,
    // Reserved for future hook backends; this API reports OS failures via Protection* + errno.
    PylonPatch_BackendFailure = 11, PylonPatch_Unaligned = 12,
    PylonPatch_DuplicateInstall = 13, PylonPatch_LifecycleFailure = 14,
    PylonPatch_CallbackFailure = 15, PylonPatch_UnsupportedInstruction = 16
};
struct PylonCodeSite { uint64_t address; uint32_t byteLength; uint32_t reserved; };
struct PylonPatchDiagnostic {
    int32_t status;
    int32_t systemError;
    uint64_t address;
    uint32_t expected;
    uint32_t actual;
};
enum PylonArm64Kind : uint32_t {
    PylonArm64_Unknown = 0, PylonArm64_B = 1, PylonArm64_Bl = 2,
    PylonArm64_BCond = 3, PylonArm64_CompareBranch = 4, PylonArm64_TestBranch = 5,
    PylonArm64_Adr = 6, PylonArm64_Adrp = 7, PylonArm64_Literal = 8,
    PylonArm64_Nop = 9, PylonArm64_Ret = 10
};
struct PylonArm64Instruction {
    uint32_t word;
    uint32_t kind;
    int64_t displacement;
    uint64_t target;
};
PYLON_MOD_EXPORT int32_t PylonMod_CodeSiteCreate(uint64_t address, uint32_t byteLength,
    PylonCodeSite* out, PylonPatchDiagnostic* diagnostic);
PYLON_MOD_EXPORT int32_t PylonMod_Arm64Read(const PylonCodeSite* site, uint32_t byteOffset,
    uint32_t* out, PylonPatchDiagnostic* diagnostic);
// Deprecated v1 decode: retained for ABI compatibility; strategy/decoding belongs in managed code.
// Deprecated compatibility exports. ARM64 decode/PC-relative analysis is owned by AsmArm64 in C#;
// these exports intentionally return UnsupportedInstruction and perform no native mask decoding.
PYLON_MOD_EXPORT int32_t PylonMod_Arm64Decode(uint64_t pc, uint32_t word,
    PylonArm64Instruction* out, PylonPatchDiagnostic* diagnostic);
PYLON_MOD_EXPORT int32_t PylonMod_Arm64PcRelative(uint64_t pc, uint32_t word,
    uint64_t* target, PylonPatchDiagnostic* diagnostic);
PYLON_MOD_EXPORT int32_t PylonMod_Arm64Validate(const PylonCodeSite* site, uint32_t byteOffset,
    const uint32_t* expected, uint32_t wordCount, PylonPatchDiagnostic* diagnostic);
// One aligned instruction only; no multi-instruction transaction or thread suspension.
// ProtectionRestoreFailed means the instruction WAS written; actual reports replacement.
PYLON_MOD_EXPORT int32_t PylonMod_Arm64Write(const PylonCodeSite* site, uint32_t byteOffset,
    uint32_t expected, uint32_t replacement, PylonPatchDiagnostic* diagnostic);
// Stable v1 names used by the managed strategy layer.
PYLON_MOD_EXPORT int32_t PylonMod_TryValidateCodeSite(const PylonCodeSite* site,
    uint32_t byteOffset, const uint32_t* expected, uint32_t wordCount, PylonPatchDiagnostic* diagnostic);
PYLON_MOD_EXPORT int32_t PylonMod_TryReadInstruction32(const PylonCodeSite* site,
    uint32_t byteOffset, uint32_t* out, PylonPatchDiagnostic* diagnostic);
PYLON_MOD_EXPORT int32_t PylonMod_TryReadWords(const PylonCodeSite* site,
    uint32_t byteOffset, uint32_t* out, uint32_t wordCount, PylonPatchDiagnostic* diagnostic);
PYLON_MOD_EXPORT int32_t PylonMod_TryWriteInstruction32(const PylonCodeSite* site,
    uint32_t byteOffset, uint32_t expected, uint32_t replacement, PylonPatchDiagnostic* diagnostic);
PYLON_MOD_EXPORT int32_t PylonMod_TryWritePatch(const PylonCodeSite* site,
    uint32_t byteOffset, uint32_t expected, uint32_t replacement, PylonPatchDiagnostic* diagnostic);

// Strict ARM64 patch ABI v2, independent of the legacy single-word ABI above.
// All sizes/offsets are bytes; expectedWords stores the immutable ENTIRE offline method.
// Default validation compares that full method. An explicit fingerprint window compares
// EVERY byte of the declared static CodeSite window (never a mask/partial match).
// The caller authenticates the loaded image against a trusted build identity and supplies
// both the planned and authenticated 128-bit identities. Native only compares these
// opaque values; it does NOT authenticate a binary, discover method bounds, suspend
// threads, keep mappings alive, or implement a general intercept/relocation backend.
// Caller must retain all request buffers/mappings for the call and exclude competing
// writes/execution throughout validation+write+cache maintenance (Quiescent asserted).
// A process-lifetime registry reserves written addresses and (ownerId, siteId) pairs;
// there is deliberately no uninstall/reset API. IDs must be nonzero and process-unique.
// Only replacementWordCount == 1 is supported: no multiword transaction is promised.
// Status always uses PylonPatchStatus; diagnostics distinguish whether a write occurred.
// ProtectionRestoreFailed can occur with either write state; inspect writeState.
constexpr uint32_t PylonPatch_AbiV2 = 2;
enum PylonPatchFlagsV2 : uint32_t {
    PylonPatchV2_BuildAuthenticated = 1, PylonPatchV2_Quiescent = 2
};
enum PylonPatchWriteStateV2 : uint32_t {
    PylonPatchV2_NotWritten = 0, PylonPatchV2_Written = 1
};
struct PylonPatchRequestV2 {
    uint32_t structSize;
    uint32_t abiVersion;
    uint64_t imageBase;
    uint64_t methodRva;
    uint32_t methodByteLength;
    uint32_t byteOffset;
    uint64_t expectedBuildIdentityLow;
    uint64_t expectedBuildIdentityHigh;
    uint64_t authenticatedBuildIdentityLow;
    uint64_t authenticatedBuildIdentityHigh;
    uint64_t ownerId;
    uint64_t siteId;
    uint64_t expectedWords; // uint32_t[expectedWordCount], encoded address for stable ABI
    uint32_t expectedWordCount;
    uint32_t replacementWordCount;
    uint32_t replacement;
    uint32_t flags;
    uint32_t fingerprintByteOffset; // zero/zero count = complete method; otherwise explicit static site window
    uint32_t fingerprintWordCount;
    uint64_t reserved; // must be zero
};
struct PylonPatchDiagnosticV2 {
    uint32_t structSize;
    uint32_t abiVersion;
    int32_t status;
    int32_t systemError;
    uint64_t methodRva;
    uint64_t siteRva;
    uint64_t address; // failure address, or patch address on success
    uint32_t byteOffset; // failure offset relative to method start
    uint32_t expected;
    uint32_t actual;
    uint32_t writeState;
    uint64_t reserved;
};
// Identify the loaded ELF containing address. Reports its GNU BuildID; no symbol/name scan.
struct PylonCodeImage { uint64_t imageBase; uint32_t buildIdLength; uint32_t reserved; uint8_t buildId[64]; };
PYLON_MOD_EXPORT int32_t PylonMod_TryGetCodeImage(uint64_t address, PylonCodeImage* image);
PYLON_MOD_EXPORT int32_t PylonMod_TryValidateCodeSiteV2(const PylonPatchRequestV2* request,
    PylonPatchDiagnosticV2* diagnostic);
PYLON_MOD_EXPORT int32_t PylonMod_TryWritePatchV2(const PylonPatchRequestV2* request,
    PylonPatchDiagnosticV2* diagnostic);

PYLON_MOD_EXPORT int32_t PylonMod_CallWithStatics(void* fn, void* statics, void* arg0, void* arg1);

PYLON_MOD_EXPORT int32_t PylonMod_SystemBridgeInstall(int32_t stage, void* body,
                                                      int32_t byteOffset,
                                                      const uint32_t* expected,
                                                      void** trampoline);

PYLON_MOD_EXPORT void PylonMod_SystemBridgeSubscribe(int32_t stage,
                                                     void (*callback)(int32_t stage,
                                                                      void* value));
