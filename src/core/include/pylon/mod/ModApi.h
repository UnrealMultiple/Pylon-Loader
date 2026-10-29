// 层：内核层 / mod —— **给 Mod 用的导出 ABI**（P/Invoke 目标层）
// 方向：Mod → libpylon.so → IL2CPP（与 il2cpp/Call.h 的 C++ → C# 是两条不同的路）。
// 原生 Mod（C/C++/Rust…）：`#include "pylon/mod/ModApi.h"` + link libpylon.so；
// C# Mod：NativeAOT 编成 .so 后 `[DllImport("pylon")]` 调这些符号（进程里不需要再塞运行时）。
// 四条纪律（别破坏）：① 按名字解析，不传偏移（跨版本靠引用校验兜底）；② 失败返回 0/null，
// 原因在 `PylonMod_LastError()`，绝不静默；③ 调用一律走 `il2cpp_runtime_invoke`（托管异常在
// 它内部被捕获，不会有 C++ 异常穿过我们的帧）；④ 碰 IL2CPP 对象/方法必须游戏线程（见文件末"线程纪律"）。
#pragma once

#include <cstdint>

namespace pylon::mod {

/// Mod ABI 版本（不＝内核版本；这个数字只在**破坏性改动**时才动）。
constexpr int32_t kApiVersionMajor = 0;
constexpr int32_t kApiVersionMinor = 1;

/// 导出标记：内核整体是 `-fvisibility=hidden`，只有这些符号对外。
#define PYLON_MOD_EXPORT extern "C" __attribute__((visibility("default")))

}  // namespace pylon::mod

// ---------------------------------------------------------------------------
// 版本 / 日志 / 错误
// ---------------------------------------------------------------------------

/// 返回 (主 << 16) | 次。
PYLON_MOD_EXPORT int32_t PylonMod_ApiVersion();

/// 写一行日志到 logcat（tag=PylonLoader，前缀 [mod]）。Mod 的主要取证手段。
PYLON_MOD_EXPORT void PylonMod_Log(const char* utf8);

/// 最近一次失败的原因（**线程内**缓冲，下一次调用会覆盖，用完即取；成功时返回空串）。
PYLON_MOD_EXPORT const char* PylonMod_LastError();

// ---------------------------------------------------------------------------
// 按名解析
// ---------------------------------------------------------------------------

/// `"Terraria.Main"` → `Il2CppClass*`（失败返回 nullptr）；只应用来传给下面的函数，
/// 不要缓存到 Mod 的静态变量里跨版本复用。
PYLON_MOD_EXPORT void* PylonMod_FindType(const char* qualifiedName);

/// `"Terraria.Main"`+`"DrawMenu"`+argc → 该方法的**原生代码指针**（Dobby 挂钩用）。
/// 失败返回 nullptr，原因见 LastError。这是给"原生 detour 直调跳板"（对齐上游 hook 模型）
/// 用的，不是拿来直调 C#：C# 异常在 IL2CPP 里是 C++ 异常，越过托管帧会崩（见 il2cpp/Call.h 的说明）。
PYLON_MOD_EXPORT void* PylonMod_MethodPointer(const char* qualifiedName, const char* method,
                                              int32_t argc);

/// 把方法体里 `cmp #oldLimit` 改成 `cmp #newLimit`（只改后面跟着顺序条件跳转的那些）。
/// 返回改了几处，0 = 没找到，-1 = 解析/写失败（原因见 LastError）。
/// `belowToo` 同时匹配 `<` / `>=` 一侧；`shifted` 匹配 `#imm, lsl #12` 形式。
PYLON_MOD_EXPORT int32_t PylonMod_PatchCompareLimit(const char* qualifiedName, const char* method,
                                                    int32_t argc, uint32_t oldLimit,
                                                    uint32_t newLimit, int32_t belowToo,
                                                    int32_t shifted);

// ---------------------------------------------------------------------------
// 字段：读 / 写（实例）
//   成功返回 1；失败返回 0，原因见 LastError。
//   `type` 形如 "Terraria.Player"，`field` 形如 "whoAmI"。
// ---------------------------------------------------------------------------

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

/// 引用类型字段（对象/数组/字符串）：返回托管指针，失败返回 nullptr。
PYLON_MOD_EXPORT void* PylonMod_GetObjectField(void* instance, const char* type,
                                               const char* field);
PYLON_MOD_EXPORT int32_t PylonMod_SetObjectField(void* instance, const char* type,
                                                 const char* field, void* value);

// ---------------------------------------------------------------------------
// 字段：读 / 写（静态）
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT int32_t PylonMod_GetStaticInt32(const char* type, const char* field,
                                                 int32_t* out);
PYLON_MOD_EXPORT int32_t PylonMod_SetStaticInt32(const char* type, const char* field,
                                                 int32_t value);
PYLON_MOD_EXPORT int32_t PylonMod_GetStaticSingle(const char* type, const char* field,
                                                  float* out);
PYLON_MOD_EXPORT int32_t PylonMod_GetStaticBoolean(const char* type, const char* field,
                                                   int32_t* out);
PYLON_MOD_EXPORT void* PylonMod_GetStaticObject(const char* type, const char* field);

// ---------------------------------------------------------------------------
// 方法调用
//   `argc`/`argv` 与 IL2CPP 的 `il2cpp_runtime_invoke` 同构：argv[i] 指向第 i 个实参的值
//   （值类型给它的字节，引用类型给指针本身）。返回**引用类型**的返回值；
//   值类型用下面的 Int32/Single 便捷版本（内部解箱），或自己 PylonMod_HadException 检查。
// ---------------------------------------------------------------------------

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

/// 上一次方法调用是否抛了托管异常（1 = 抛了）。抛了也算"调用发生了"，
/// 返回值不可信——Mod 必须先看这个再决定用不用返回值。
PYLON_MOD_EXPORT int32_t PylonMod_HadException();
PYLON_MOD_EXPORT const char* PylonMod_ExceptionText();

// ---------------------------------------------------------------------------
// 线程纪律（**Mod 必须看这一段**）
//
//   托管方法调用、托管字符串创建、实例字段读写：**只能在游戏线程（UnityMain）上做**，
//   别的线程上做会 SIGSEGV/SIGBUS。内核会拒绝并写 LastError（不会把游戏搞崩，但功能不生效）。
//   按名解析与**静态**字段读写可以在任何已 attach 的线程上做（纯数据）。
//
//   宿主用 `UnityPlayer.queueGLThreadEvent(Runnable)` 把活投到游戏线程；Mod 从自己的线程
//   做事时用下面两个函数判断：
//     PylonMod_IsGameThread()  当前线程是不是游戏线程
//     PylonMod_GameThreadId()  已登记的游戏线程 tid（0 = 还没登记）
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT int32_t PylonMod_IsGameThread();
PYLON_MOD_EXPORT int64_t PylonMod_GameThreadId();
PYLON_MOD_EXPORT int64_t PylonMod_CurrentThreadId();

// ---------------------------------------------------------------------------
// 托管字符串
// ---------------------------------------------------------------------------

/// UTF-8 → 托管 string（失败返回 nullptr）。只能在游戏线程调用。
PYLON_MOD_EXPORT void* PylonMod_NewString(const char* utf8);

/// 托管 string → UTF-8。
/// 返回写入的字节数（不含结尾 0）；`out` 为空或缓冲不足时返回**所需字节数（含结尾 0）**且不写缓冲，
/// 调用方按它重开缓冲重试。失败（不是字符串/未 attach 等）返回 -1，原因见 LastError。
PYLON_MOD_EXPORT int32_t PylonMod_ReadString(void* managedString, char* out, int32_t outSize);

// ---------------------------------------------------------------------------
// 原生 hook（Dobby）：托管侧的 detour provider 与内核内部都用它
// ---------------------------------------------------------------------------

/// 成功时通过 trampoline 返回原函数入口（跳板为空即已撤销）。
/// @return 0 成功；-1 参数非法；-2 Dobby 不可用；-3 拿不到跳板；-4 该地址已挂别的替换函数
PYLON_MOD_EXPORT int32_t PylonMod_HookApply(void* target, void* detour, void** trampoline);

/// 卸载挂钩（还有别的持有者时只减计数）。
PYLON_MOD_EXPORT int32_t PylonMod_HookRemove(void* target);

/// Dobby 是否可用。
PYLON_MOD_EXPORT int32_t PylonMod_HookAvailable();

