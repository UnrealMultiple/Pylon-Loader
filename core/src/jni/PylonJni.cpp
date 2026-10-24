// 层：内核层 / jni —— 唯一的 JNI 边界
// 约定：内核不猜时机。Java 侧只交"要校验哪些名字"（数据）和之后来取结果；**何时能碰 IL2CPP**
// 由内核自己的探测线程按"静置 + 稀疏轮询"决定（见 boot/Probe.h），这条硬约束只有一个地方能破坏。
#include <jni.h>
#include <dlfcn.h>

#include <string>
#include <vector>

#include "boot/BindSelfTest.h"
#include "boot/Boot.h"
#include "boot/Probe.h"
#include "dotnet/CoreClrHost.h"
#include "il2cpp/Il2CppRuntime.h"
#include "pylon/Log.h"

namespace {

constexpr int kVersionMajor = 0;
constexpr int kVersionMinor = 2;  // M2

jlong EncodeVersion() {
    return (static_cast<jlong>(kVersionMajor) << 16) | kVersionMinor;
}

/// 读 jstring；失败（OOM 或未决异常）时**清异常**并返回 false——带未决异常继续调 JNI 是未定义行为。
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

}  // namespace

extern "C" {

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM* vm, void* reserved) {
    (void)vm;
    (void)reserved;
    LOGI("core", "libpylon.so JNI_OnLoad, 内核版本 %d.%d（此时不碰 IL2CPP）",
         kVersionMajor, kVersionMinor);
    return JNI_VERSION_1_6;
}

JNIEXPORT void JNICALL JNI_OnUnload(JavaVM* vm, void* reserved) {
    (void)vm;
    (void)reserved;
    LOGI("core", "libpylon.so JNI_OnUnload");
}

JNIEXPORT jlong JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeVersion(JNIEnv* env, jobject thiz) {
    (void)env;
    (void)thiz;
    return EncodeVersion();
}

/// 游戏 .so 已加载、UnityPlayer 未创建时调用：绑定 il2cpp 符号（不调 API）并用给的清单启动探测线程。
JNIEXPORT jboolean JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeStart(JNIEnv* env, jobject thiz,
                                                     jobjectArray refs, jint settleMs) {
    (void)thiz;

    const auto status = pylon::boot::Boot::Instance().OnKernelLoaded();
    if (!status) {
        LOGE("core", "nativeStart 失败：%s", status.reason.c_str());
        return JNI_FALSE;
    }

    pylon::boot::ProbeConfig config;
    if (settleMs > 0) config.settle_ms = settleMs;

    std::vector<std::string> specs;
    if (!ToStringList(env, refs, &specs)) {
        LOGE("core", "nativeStart：引用清单转换失败（JNI 读取字符串出错）");
        return JNI_FALSE;
    }
    LOGI("core", "nativeStart：待校验引用 %zu 条", specs.size());
    const auto started = pylon::boot::Probe::Instance().Start(specs, config);
    if (!started) {
        LOGE("core", "启动就绪探测失败：%s", started.reason.c_str());
        return JNI_FALSE;
    }
    return JNI_TRUE;
}

/// 阻塞等待探测结束。返回 null = 引用校验全部通过；否则返回"停用 Mod"的原因。
JNIEXPORT jstring JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeAwaitReport(JNIEnv* env, jobject thiz,
                                                           jint timeoutMs) {
    (void)thiz;
    pylon::boot::ProbeResult result;
    const bool finished = pylon::boot::Probe::Instance().AwaitResult(timeoutMs, &result);
    if (!finished) {
        return env->NewStringUTF("等待内核探测超时（引擎可能一直没到稳定态）");
    }
    if (result.ok) return nullptr;
    return env->NewStringUTF(result.failure_reason.c_str());
}

/// 逐条结果文本（多行），随时可调；用于日志与（M3 之后的）内置 UI。
JNIEXPORT jstring JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeReport(JNIEnv* env, jobject thiz) {
    (void)thiz;
    const auto snapshot = pylon::boot::Probe::Instance().Snapshot();
    std::string text = snapshot.report;
    if (!snapshot.signal.empty()) text = "就绪信号：" + snapshot.signal + "\n" + text;
    if (!snapshot.finished) text += "(探测尚未结束)\n";
    return env->NewStringUTF(text.c_str());
}

/// 测试 Mod 探针：dlopen 那个**独立**的 Mod .so 并调用，证明导出 ABI 外部可用；其 DT_NEEDED 指向 libpylon.so，符号由动态链接器解析。
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
    if (run == nullptr) {
        return env->NewStringUTF("✗ 测试 Mod 里没有 PylonTestMod_Run");
    }
    const char* report = run();
    return env->NewStringUTF(report != nullptr ? report : "(测试 Mod 返回空)");
}

/// 登记游戏线程：宿主经 UnityPlayer.queueGLThreadEvent 投递小任务到 UnityMain 后调此函数。
/// 托管方法只允许在这个线程上调。
JNIEXPORT void JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeMarkGameThread(JNIEnv* env, jobject thiz) {
    (void)env;
    (void)thiz;
    pylon::il2cpp::Runtime::Instance().NoteGameThread();
}

/// 当前线程是不是已登记的游戏线程。
JNIEXPORT jboolean JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeIsGameThread(JNIEnv* env, jobject thiz) {
    (void)env;
    (void)thiz;
    return pylon::il2cpp::Runtime::Instance().IsGameThread() ? JNI_TRUE : JNI_FALSE;
}

/// 起 CoreCLR 并调用托管入口；参数都是绝对路径（Kotlin 从 assets 解到 filesDir 后传入）。幂等。
JNIEXPORT jboolean JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeStartCoreClr(
        JNIEnv* env, jobject thiz, jstring entryAssemblyPath, jstring coreDir, jstring dotnetDir,
        jstring entryAssemblyName, jstring entryType, jstring entryMethod, jstring logDir,
        jstring modsDir, jstring interopDir) {
    (void)thiz;
    if (pylon::dotnet::Started()) return JNI_TRUE;

    pylon::dotnet::HostConfig config;
    const bool params_ok =
            ToString(env, entryAssemblyPath, &config.entry_assembly_path) &&
            ToString(env, coreDir, &config.core_dir) && ToString(env, dotnetDir, &config.dotnet_dir) &&
            ToString(env, entryAssemblyName, &config.entry_assembly_name) &&
            ToString(env, entryType, &config.entry_type) &&
            ToString(env, entryMethod, &config.entry_method) && ToString(env, logDir, &config.log_dir) &&
            ToString(env, modsDir, &config.mods_dir) &&
            ToString(env, interopDir, &config.interop_dir);
    if (!params_ok) {
        LOGE("dotnet", "启动参数转换失败（JNI 读取字符串出错）");
        return JNI_FALSE;
    }

    const auto status = pylon::dotnet::Start(config);
    if (!status) {
        LOGE("dotnet", "启动 CoreCLR 失败：%s", status.reason.c_str());
        return JNI_FALSE;
    }
    return JNI_TRUE;
}

/// 在**调用者线程**上调用托管入口的静态无参方法（Java 把 Mod 的 Load() 投到 UnityMain 后用；托管代码碰 IL2CPP 只能在该线程）。
JNIEXPORT jboolean JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeCallManagedEntry(JNIEnv* env, jobject thiz,
                                                                jstring method) {
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

JNIEXPORT jint JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativePhase(JNIEnv* env, jobject thiz) {
    (void)env;
    (void)thiz;
    return static_cast<jint>(pylon::boot::Boot::Instance().phase());
}

/// C# 方法绑定自检：真调几个托管方法拼成多行文本；须在 attach 之后调用（探测已命中），未就绪时只返回一行说明。
JNIEXPORT jstring JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeBindSelfTest(JNIEnv* env, jobject thiz) {
    (void)thiz;
    const std::string report = pylon::boot::RunBindSelfTest();
    LOGI("core", "绑定自检完成：\n%s", report.c_str());
    return env->NewStringUTF(report.c_str());
}

/// 进程退出前调用。**刻意不 dlclose**：句柄是 RTLD_NOLOAD 拿到的 libil2cpp.so，卸掉等于拆了 Unity 还在用的库，只记一笔。
JNIEXPORT void JNICALL
Java_dev_pylon_loader_game_core_PylonCore_nativeShutdown(JNIEnv* env, jobject thiz) {
    (void)env;
    (void)thiz;
    LOGI("core", "nativeShutdown：保留 libil2cpp 句柄（不能卸 Unity 正在用的库）");
}

}  // extern "C"
