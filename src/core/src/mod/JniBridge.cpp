// 层：内核层 / mod —— Java（JNI）反射桥的实现（声明见 include/pylon/mod/JniBridge.h）
// 用反射而非逐类写 JNI 粘合：C# 要"能直接反射 Java"这一通用原语，任何系统类都能从 C# 调。
// 线程/引用纪律：未 attach 的线程自动 attach（JavaVM 从 libart/libnativehelper 找，与
// JniRuntime.cpp 同源）并登记退出 detach（否则 ART 在线程结束时 abort 进程）；返回给 C# 的
// jclass/jobject/jstring 一律**全局引用**，本地引用用完即删；每次调用后清未决异常并写
// LastError（带异常继续调 JNI 是未定义行为）。
#include "pylon/mod/JniBridge.h"

#include <jni.h>
#include <dlfcn.h>
#include <pthread.h>
#include <string.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>

#include "imgui/backends/JniRuntime.h"

namespace {

thread_local char g_error[512] = {0};
thread_local pthread_key_t g_detach_key;
thread_local bool g_detach_key_created = false;

void SetError(const char* text) {
    if (text == nullptr) text = "未知 JNI 错误";
    std::snprintf(g_error, sizeof(g_error), "%s", text);
}

void DetachOnExit(void*) {
    JavaVM* vm = AndroidPlatform::GetJavaVM();
    if (vm) vm->DetachCurrentThread();
}

/// 当前线程的 JNIEnv：已 attach 直接用，否则 attach（并登记退出 detach）。
JNIEnv* EnsureEnv() {
    JavaVM* vm = AndroidPlatform::GetJavaVM();
    if (vm == nullptr) {
        SetError("拿不到 JavaVM");
        return nullptr;
    }
    JNIEnv* env = nullptr;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_OK) return env;
    if (vm->AttachCurrentThread(&env, nullptr) != JNI_OK) {
        SetError("AttachCurrentThread 失败");
        return nullptr;
    }
    if (!g_detach_key_created) {
        if (pthread_key_create(&g_detach_key, DetachOnExit) == 0) g_detach_key_created = true;
    }
    if (g_detach_key_created) pthread_setspecific(g_detach_key, env);
    return env;
}

/// 调用后统一收尾：有未决异常就清掉并写 LastError（返回 false 表示调用结果不可信）。
bool AfterCall(JNIEnv* env, const char* what) {
    if (env->ExceptionCheck()) {
        env->ExceptionDescribe();  // 写 logcat
        env->ExceptionClear();
        SetError(what);
        return false;
    }
    return true;
}

}  // namespace

extern "C" {

int32_t PylonMod_Jni_EnsureAttached() {
    return EnsureEnv() != nullptr ? 1 : 0;
}

const char* PylonMod_Jni_LastError() {
    return g_error;
}

// ---- 类 / 对象句柄 ----

void* PylonMod_Jni_FindClass(const char* name) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr) return nullptr;
    if (name == nullptr || *name == '\0') { SetError("类名为空"); return nullptr; }

    jclass local = env->FindClass(name);
    if (local == nullptr) {
        AfterCall(env, "FindClass 失败");
        std::snprintf(g_error, sizeof(g_error), "FindClass(%s) 失败", name);
        return nullptr;
    }
    jclass global = static_cast<jclass>(env->NewGlobalRef(local));
    env->DeleteLocalRef(local);
    if (global == nullptr) SetError("NewGlobalRef 失败");
    return global;
}

void* PylonMod_Jni_GetObjectClass(void* obj) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || obj == nullptr) { if (env == nullptr) return nullptr; SetError("对象为空"); return nullptr; }
    jclass local = env->GetObjectClass(static_cast<jobject>(obj));
    if (local == nullptr) { AfterCall(env, "GetObjectClass 失败"); return nullptr; }
    jclass global = static_cast<jclass>(env->NewGlobalRef(local));
    env->DeleteLocalRef(local);
    return global;
}

void* PylonMod_Jni_NewGlobalRef(void* local) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr) return nullptr;
    if (local == nullptr) { SetError("本地引用为空"); return nullptr; }
    return env->NewGlobalRef(static_cast<jobject>(local));
}

void PylonMod_Jni_DeleteGlobalRef(void* global) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || global == nullptr) return;
    env->DeleteGlobalRef(static_cast<jobject>(global));
}

void PylonMod_Jni_DeleteLocalRef(void* local) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || local == nullptr) return;
    env->DeleteLocalRef(static_cast<jobject>(local));
}

// ---- 方法 ID ----

void* PylonMod_Jni_GetMethodID(void* cls, const char* name, const char* sig) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr) return nullptr;
    if (cls == nullptr || name == nullptr || sig == nullptr) { SetError("GetMethodID 参数为空"); return nullptr; }
    jmethodID mid = env->GetMethodID(static_cast<jclass>(cls), name, sig);
    if (mid == nullptr) {
        AfterCall(env, "GetMethodID 失败");
        std::snprintf(g_error, sizeof(g_error), "GetMethodID(%s%s%s) 失败", name, sig == nullptr ? "" : sig, "");
        return nullptr;
    }
    return mid;
}

void* PylonMod_Jni_GetStaticMethodID(void* cls, const char* name, const char* sig) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr) return nullptr;
    if (cls == nullptr || name == nullptr || sig == nullptr) { SetError("GetStaticMethodID 参数为空"); return nullptr; }
    jmethodID mid = env->GetStaticMethodID(static_cast<jclass>(cls), name, sig);
    if (mid == nullptr) {
        AfterCall(env, "GetStaticMethodID 失败");
        std::snprintf(g_error, sizeof(g_error), "GetStaticMethodID(%s%s) 失败", name, sig == nullptr ? "" : sig);
        return nullptr;
    }
    return mid;
}

// ---- 构造 / 调用 ----

void* PylonMod_Jni_NewObject(void* cls, void* ctorMethod, int32_t argc, void* argv) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr) return nullptr;
    if (cls == nullptr || ctorMethod == nullptr) { SetError("NewObject 参数为空"); return nullptr; }
    jobject local = env->NewObjectA(static_cast<jclass>(cls), reinterpret_cast<jmethodID>(ctorMethod),
                                    argc > 0 ? static_cast<const jvalue*>(argv) : nullptr);
    if (local == nullptr) { AfterCall(env, "NewObject 失败"); return nullptr; }
    jobject global = env->NewGlobalRef(local);
    env->DeleteLocalRef(local);
    if (global == nullptr) SetError("NewObject 转全局引用失败");
    return global;
}

int32_t PylonMod_Jni_CallInt(void* obj, void* method, int32_t argc, void* argv) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || obj == nullptr || method == nullptr) { SetError("CallInt 参数为空"); return 0; }
    jint value = env->CallIntMethodA(static_cast<jobject>(obj), reinterpret_cast<jmethodID>(method),
                                     argc > 0 ? static_cast<const jvalue*>(argv) : nullptr);
    if (!AfterCall(env, "CallIntMethod 抛异常")) return 0;
    return value;
}

int32_t PylonMod_Jni_CallBoolean(void* obj, void* method, int32_t argc, void* argv) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || obj == nullptr || method == nullptr) { SetError("CallBoolean 参数为空"); return 0; }
    jboolean value = env->CallBooleanMethodA(static_cast<jobject>(obj), reinterpret_cast<jmethodID>(method),
                                             argc > 0 ? static_cast<const jvalue*>(argv) : nullptr);
    if (!AfterCall(env, "CallBooleanMethod 抛异常")) return 0;
    return value ? 1 : 0;
}

int32_t PylonMod_Jni_CallLong(void* obj, void* method, int32_t argc, void* argv) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || obj == nullptr || method == nullptr) { SetError("CallLong 参数为空"); return 0; }
    jlong value = env->CallLongMethodA(static_cast<jobject>(obj), reinterpret_cast<jmethodID>(method),
                                       argc > 0 ? static_cast<const jvalue*>(argv) : nullptr);
    if (!AfterCall(env, "CallLongMethod 抛异常")) return 0;
    return static_cast<int32_t>(value);
}

void PylonMod_Jni_CallVoid(void* obj, void* method, int32_t argc, void* argv) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || obj == nullptr || method == nullptr) { SetError("CallVoid 参数为空"); return; }
    env->CallVoidMethodA(static_cast<jobject>(obj), reinterpret_cast<jmethodID>(method),
                         argc > 0 ? static_cast<const jvalue*>(argv) : nullptr);
    AfterCall(env, "CallVoidMethod 抛异常");
}

void* PylonMod_Jni_CallObject(void* obj, void* method, int32_t argc, void* argv) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || obj == nullptr || method == nullptr) { SetError("CallObject 参数为空"); return nullptr; }
    jobject local = env->CallObjectMethodA(static_cast<jobject>(obj), reinterpret_cast<jmethodID>(method),
                                           argc > 0 ? static_cast<const jvalue*>(argv) : nullptr);
    if (!AfterCall(env, "CallObjectMethod 抛异常")) return nullptr;
    if (local == nullptr) return nullptr;   // Java null 对象：合法，返回 nullptr
    jobject global = env->NewGlobalRef(local);
    env->DeleteLocalRef(local);
    return global;
}

int32_t PylonMod_Jni_CallStaticInt(void* cls, void* method, int32_t argc, void* argv) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || cls == nullptr || method == nullptr) { SetError("CallStaticInt 参数为空"); return 0; }
    jint value = env->CallStaticIntMethodA(static_cast<jclass>(cls), reinterpret_cast<jmethodID>(method),
                                           argc > 0 ? static_cast<const jvalue*>(argv) : nullptr);
    if (!AfterCall(env, "CallStaticIntMethod 抛异常")) return 0;
    return value;
}

void* PylonMod_Jni_CallStaticObject(void* cls, void* method, int32_t argc, void* argv) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || cls == nullptr || method == nullptr) { SetError("CallStaticObject 参数为空"); return nullptr; }
    jobject local = env->CallStaticObjectMethodA(static_cast<jclass>(cls), reinterpret_cast<jmethodID>(method),
                                                 argc > 0 ? static_cast<const jvalue*>(argv) : nullptr);
    if (!AfterCall(env, "CallStaticObjectMethod 抛异常")) return nullptr;
    if (local == nullptr) return nullptr;
    jobject global = env->NewGlobalRef(local);
    env->DeleteLocalRef(local);
    return global;
}

void PylonMod_Jni_CallStaticVoid(void* cls, void* method, int32_t argc, void* argv) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr || cls == nullptr || method == nullptr) { SetError("CallStaticVoid 参数为空"); return; }
    env->CallStaticVoidMethodA(static_cast<jclass>(cls), reinterpret_cast<jmethodID>(method),
                               argc > 0 ? static_cast<const jvalue*>(argv) : nullptr);
    AfterCall(env, "CallStaticVoidMethod 抛异常");
}

// ---- 字符串 ----

void* PylonMod_Jni_NewStringUtf(const char* utf8) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr) return nullptr;
    if (utf8 == nullptr) { SetError("字符串为空"); return nullptr; }
    jstring local = env->NewStringUTF(utf8);
    if (local == nullptr) { AfterCall(env, "NewStringUTF 失败"); return nullptr; }
    jstring global = static_cast<jstring>(env->NewGlobalRef(local));
    env->DeleteLocalRef(local);
    return global;
}

const char* PylonMod_Jni_GetStringUtf(void* jstring_) {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr) return nullptr;
    if (jstring_ == nullptr) { SetError("jstring 为空"); return nullptr; }
    const char* chars = env->GetStringUTFChars(static_cast<jstring>(jstring_), nullptr);
    if (chars == nullptr) { AfterCall(env, "GetStringUTFChars 失败"); return nullptr; }
    // 拷贝成独立缓冲：ReleaseStringUTFChars 只释放 Java 侧引用的拷贝，这里 strdup 给 C#。
    char* copy = strdup(chars);
    env->ReleaseStringUTFChars(static_cast<jstring>(jstring_), chars);
    return copy;
}

void PylonMod_Jni_ReleaseStringUtf(const char* copy) {
    free(const_cast<char*>(copy));
}

// ---- 异常 ----

int32_t PylonMod_Jni_ExceptionCheck() {
    JNIEnv* env = EnsureEnv();
    if (env == nullptr) return 0;
    return env->ExceptionCheck() ? 1 : 0;
}

}  // extern "C"