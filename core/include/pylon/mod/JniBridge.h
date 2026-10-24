#pragma once

#include "pylon/mod/ModApi.h"   // 导入 PYLON_MOD_EXPORT 与导出纪律（声明在全局作用域，与 ModApi.h 同风格）

// ---------------------------------------------------------------------------
// Java（JNI）反射桥 —— 给 C# Mod 用的导出 ABI（P/Invoke 目标层）
// C# 直接反射 Java 类/方法（android.media.* 等），不依赖 Unity 的 AndroidJavaObject
// （这版 IL2CPP 没实例化 Call<T>，会抛 "could not execute the method ... not fully
// instantiated"）；本层只做 JNI 原语，C# 侧自己包成对象式封装。
// 约定：类名用 `a/b/C` 斜杠形式；jobject/jclass 一律全局引用（C# 跨帧持有，用完
// DeleteGlobalRef）；参数数组是 jvalue*（C# 侧 8 字节 union，实例方法第一参是 jobject）；
// 失败返回 0/null，原因见 LastError，绝不静默；调用必须在已 attach 的线程上（本层自动 attach，退出时 detach）。
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT int32_t PylonMod_Jni_EnsureAttached();
PYLON_MOD_EXPORT const char* PylonMod_Jni_LastError();

// ---- 类 / 对象句柄 ----

/// "android/media/SoundPool" → jclass（全局引用）；失败返回 nullptr。
PYLON_MOD_EXPORT void* PylonMod_Jni_FindClass(const char* name);

/// jobject 的类（全局引用）；失败返回 nullptr。
PYLON_MOD_EXPORT void* PylonMod_Jni_GetObjectClass(void* obj);

/// 本地引用 → 全局引用（跨帧持有用）；失败返回 nullptr。
PYLON_MOD_EXPORT void* PylonMod_Jni_NewGlobalRef(void* local);

/// 释放全局引用（幂等，null 安全）。
PYLON_MOD_EXPORT void PylonMod_Jni_DeleteGlobalRef(void* global);

/// 释放本地引用（幂等，null 安全）。
PYLON_MOD_EXPORT void PylonMod_Jni_DeleteLocalRef(void* local);

// ---- 方法 ID ----

/// 实例方法（"<init>" 表示构造器）；失败返回 nullptr，原因见 LastError。
PYLON_MOD_EXPORT void* PylonMod_Jni_GetMethodID(void* cls, const char* name, const char* sig);

/// 静态方法；失败返回 nullptr，原因见 LastError。
PYLON_MOD_EXPORT void* PylonMod_Jni_GetStaticMethodID(void* cls, const char* name, const char* sig);

// ---- 构造 / 调用（argv = jvalue* 数组，argc 个；返回引用类型时转全局引用） ----

PYLON_MOD_EXPORT void* PylonMod_Jni_NewObject(void* cls, void* ctorMethod, int32_t argc, void* argv);

PYLON_MOD_EXPORT int32_t PylonMod_Jni_CallInt(void* obj, void* method, int32_t argc, void* argv);
PYLON_MOD_EXPORT int32_t PylonMod_Jni_CallBoolean(void* obj, void* method, int32_t argc, void* argv);
PYLON_MOD_EXPORT int32_t PylonMod_Jni_CallLong(void* obj, void* method, int32_t argc, void* argv);
PYLON_MOD_EXPORT void PylonMod_Jni_CallVoid(void* obj, void* method, int32_t argc, void* argv);
PYLON_MOD_EXPORT void* PylonMod_Jni_CallObject(void* obj, void* method, int32_t argc, void* argv);

PYLON_MOD_EXPORT int32_t PylonMod_Jni_CallStaticInt(void* cls, void* method, int32_t argc, void* argv);
PYLON_MOD_EXPORT void* PylonMod_Jni_CallStaticObject(void* cls, void* method, int32_t argc, void* argv);
PYLON_MOD_EXPORT void PylonMod_Jni_CallStaticVoid(void* cls, void* method, int32_t argc, void* argv);

// ---- 字符串 ----

/// UTF-8 → jstring（全局引用；用完 DeleteGlobalRef）。
PYLON_MOD_EXPORT void* PylonMod_Jni_NewStringUtf(const char* utf8);

/// jstring → UTF-8 拷贝（调用方用完必须 PylonMod_Jni_ReleaseStringUtf）；失败返回 nullptr。
PYLON_MOD_EXPORT const char* PylonMod_Jni_GetStringUtf(void* jstring_);

PYLON_MOD_EXPORT void PylonMod_Jni_ReleaseStringUtf(const char* copy);

// ---- 异常 ----

/// 1 = 当前线程有未决 Java 异常（本层在调用后已清掉并记 LastError，这里供需要时再查）。
PYLON_MOD_EXPORT int32_t PylonMod_Jni_ExceptionCheck();