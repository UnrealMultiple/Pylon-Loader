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

PYLON_MOD_EXPORT int32_t PylonMod_HookRemove(void* target);

PYLON_MOD_EXPORT int32_t PylonMod_HookAvailable();

PYLON_MOD_EXPORT int32_t PylonMod_PatchInstruction(void* body, int32_t byteOffset,
                                                   uint32_t expected, uint32_t replacement);

PYLON_MOD_EXPORT int32_t PylonMod_CallWithStatics(void* fn, void* statics, void* arg0, void* arg1);

PYLON_MOD_EXPORT int32_t PylonMod_SystemBridgeInstall(int32_t stage, void* body,
                                                      int32_t byteOffset,
                                                      const uint32_t* expected,
                                                      void** trampoline);

PYLON_MOD_EXPORT void PylonMod_SystemBridgeSubscribe(int32_t stage,
                                                     void (*callback)(int32_t stage,
                                                                      void* value));
