#include <jni.h>
#include <dlfcn.h>

#include <string>
#include <vector>

#include "boot/Boot.h"
#include "boot/Probe.h"
#include "dotnet/CoreClrHost.h"
#include "il2cpp/Domain.h"
#include "pylon/Log.h"

namespace {

constexpr int kVersionMajor = 0;
constexpr int kVersionMinor = 2;  // M2

jlong EncodeVersion() {
    return (static_cast<jlong>(kVersionMajor) << 16) | kVersionMinor;
}

/// 读 jstring；失败时清掉未决异常（带着它继续调 JNI 是未定义行为）。
bool ToString(JNIEnv* env, jstring value, std::string* out) {
    out->clear();
    if (value == nullptr) return true;
    const char* chars = env->GetStringUTFChars(value, nullptr);
    if (chars == nullptr) {
        env->ExceptionClear();
        return false;
    }
    out->assign(chars);
    env->ReleaseStringUTFChars(value, chars);
    return true;
}

bool ToStringList(JNIEnv* env, jobjectArray array, std::vector<std::string>* out) {
    out->clear();
    if (array == nullptr) return true;
    const jsize count = env->GetArrayLength(array);
    out->reserve(static_cast<size_t>(count));
    for (jsize i = 0; i < count; ++i) {
        auto element = static_cast<jstring>(env->GetObjectArrayElement(array, i));
        std::string text;
        const bool ok = ToString(env, element, &text);
        if (element != nullptr) env->DeleteLocalRef(element);
        if (!ok) return false;
        out->push_back(std::move(text));
    }
    return true;
}

bool ReadHostConfig(JNIEnv* env, jstring entryAssemblyPath, jstring coreDir, jstring dotnetDir,
                    jstring entryAssemblyName, jstring entryType, jstring entryMethod, jstring logDir,
                    jstring modsDir, jstring interopDir, pylon::dotnet::HostConfig* config) {
    return ToString(env, entryAssemblyPath, &config->entry_assembly_path) &&
           ToString(env, coreDir, &config->core_dir) && ToString(env, dotnetDir, &config->dotnet_dir) &&
           ToString(env, entryAssemblyName, &config->entry_assembly_name) &&
           ToString(env, entryType, &config->entry_type) &&
           ToString(env, entryMethod, &config->entry_method) &&
           ToString(env, logDir, &config->log_dir) && ToString(env, modsDir, &config->mods_dir) &&
           ToString(env, interopDir, &config->interop_dir);
}

}  // namespace

extern "C" {

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM* vm, void* reserved) {
    (void)vm;
    (void)reserved;
    LOGI("core", "libpylon.so JNI_OnLoad, 内核版本 %d.%d（此时不碰 IL2CPP）", kVersionMajor,
         kVersionMinor);
    return JNI_VERSION_1_6;
}

JNIEXPORT void JNICALL JNI_OnUnload(JavaVM* vm, void* reserved) {
    (void)vm;
    (void)reserved;
    LOGI("core", "libpylon.so JNI_OnUnload");
}

JNIEXPORT jlong JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativeVersion(JNIEnv* env,
                                                                               jobject thiz) {
    (void)env;
    (void)thiz;
    return EncodeVersion();
}

JNIEXPORT jboolean JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativeStart(JNIEnv* env,
                                                                               jobject thiz,
                                                                               jobjectArray refs) {
    (void)thiz;

    const auto status = pylon::boot::Boot::Instance().OnKernelLoaded();
    if (!status) {
        LOGE("core", "nativeStart 失败：%s", status.reason.c_str());
        return JNI_FALSE;
    }

    std::vector<std::string> specs;
    if (!ToStringList(env, refs, &specs)) {
        LOGE("core", "nativeStart：引用清单转换失败（JNI 读取字符串出错）");
        return JNI_FALSE;
    }
    LOGI("core", "nativeStart：待校验引用 %zu 条", specs.size());

    const auto started = pylon::boot::Probe::Instance().Start(specs, pylon::boot::ProbeConfig{});
    if (!started) {
        LOGE("core", "启动就绪探测失败：%s", started.reason.c_str());
        return JNI_FALSE;
    }
    return JNI_TRUE;
}

JNIEXPORT jstring JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativeAwaitReport(JNIEnv* env,
                                                                                     jobject thiz,
                                                                                     jint timeoutMs) {
    (void)thiz;
    pylon::boot::ProbeResult result;
    if (!pylon::boot::Probe::Instance().AwaitResult(timeoutMs, &result)) {
        return env->NewStringUTF("等待内核探测超时（引擎可能一直没到稳定态）");
    }
    if (result.ok) return nullptr;
    return env->NewStringUTF(result.failure_reason.c_str());
}

JNIEXPORT jstring JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativeReport(JNIEnv* env,
                                                                                jobject thiz) {
    (void)thiz;
    const auto snapshot = pylon::boot::Probe::Instance().Snapshot();
    std::string text = snapshot.report;
    if (!snapshot.signal.empty()) text = "就绪信号：" + snapshot.signal + "\n" + text;
    if (!snapshot.finished) text += "(探测尚未结束)\n";
    return env->NewStringUTF(text.c_str());
}

JNIEXPORT jstring JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeTestModProbe(JNIEnv* env, jobject thiz) {
    (void)thiz;
    void* handle = dlopen("libpylontestmod.so", RTLD_NOLOAD | RTLD_NOW);
    if (handle == nullptr) {
        const char* err = dlerror();
        return env->NewStringUTF(
                (std::string("✗ 测试 Mod 未加载（宿主应先 System.loadLibrary(\"pylontestmod\")）：") +
                 (err != nullptr ? err : "unknown")).c_str());
    }
    auto run = reinterpret_cast<const char* (*)()>(dlsym(handle, "PylonTestMod_Run"));
    if (run == nullptr) return env->NewStringUTF("✗ 测试 Mod 里没有 PylonTestMod_Run");
    const char* report = run();
    return env->NewStringUTF(report != nullptr ? report : "(测试 Mod 返回空)");
}

JNIEXPORT void JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativeMarkGameThread(JNIEnv* env,
                                                                                     jobject thiz) {
    (void)env;
    (void)thiz;
    pylon::il2cpp::Domain::MarkGameThread();
}

JNIEXPORT jboolean JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativeIsGameThread(JNIEnv* env,
                                                                                       jobject thiz) {
    (void)env;
    (void)thiz;
    return pylon::il2cpp::Domain::OnGameThread() ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT jboolean JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativePrepareCoreClr(
        JNIEnv* env, jobject thiz, jstring entryAssemblyPath, jstring coreDir, jstring dotnetDir,
        jstring entryAssemblyName, jstring entryType, jstring entryMethod, jstring logDir,
        jstring modsDir, jstring interopDir) {
    (void)thiz;
    if (pylon::dotnet::Prepared()) return JNI_TRUE;

    pylon::dotnet::HostConfig config;
    if (!ReadHostConfig(env, entryAssemblyPath, coreDir, dotnetDir, entryAssemblyName, entryType,
                        entryMethod, logDir, modsDir, interopDir, &config)) {
        LOGE("dotnet", "启动参数转换失败（JNI 读取字符串出错）");
        return JNI_FALSE;
    }

    const auto status = pylon::dotnet::Prepare(config);
    if (!status) {
        LOGE("dotnet", "准备 CoreCLR 失败：%s", status.reason.c_str());
        return JNI_FALSE;
    }
    return JNI_TRUE;
}

JNIEXPORT jboolean JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativeRunCoreClrEntry(
        JNIEnv* env, jobject thiz) {
    (void)env;
    (void)thiz;
    const auto status = pylon::dotnet::RunEntry();
    if (!status) {
        LOGE("dotnet", "执行托管入口失败：%s", status.reason.c_str());
        return JNI_FALSE;
    }
    return JNI_TRUE;
}

JNIEXPORT jboolean JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativeCallManagedEntry(
        JNIEnv* env, jobject thiz, jstring method) {
    (void)thiz;
    std::string name;
    if (!ToString(env, method, &name) || name.empty()) {
        LOGE("dotnet", "方法名为空或转换失败");
        return JNI_FALSE;
    }
    const auto status = pylon::dotnet::CallManagedEntry(name);
    if (!status) {
        LOGE("dotnet", "调用托管入口失败：%s", status.reason.c_str());
        return JNI_FALSE;
    }
    return JNI_TRUE;
}

JNIEXPORT jint JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativePhase(JNIEnv* env,
                                                                            jobject thiz) {
    (void)env;
    (void)thiz;
    return static_cast<jint>(pylon::boot::Boot::Instance().phase());
}

/// 刻意不 dlclose：句柄是 RTLD_NOLOAD 拿到的 libil2cpp.so，卸掉等于拆了 Unity 还在用的库。
JNIEXPORT void JNICALL Java_dev_pylon_loader_game_core_PylonCore_nativeShutdown(JNIEnv* env,
                                                                               jobject thiz) {
    (void)env;
    (void)thiz;
    LOGI("core", "nativeShutdown：保留 libil2cpp 句柄");
}

}  // extern "C"
