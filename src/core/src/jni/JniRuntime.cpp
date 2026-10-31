#include "JniRuntime.h"

#include <dlfcn.h>
#include <pthread.h>

#include "pylon/Log.h"

namespace AndroidPlatform
{

// 与上游 CheatButton.cpp 同法：找 JNI_GetCreatedJavaVMs，不依赖原工程的 ElfScanner。
JavaVM* GetJavaVM()
{
    static JavaVM* s_vm = nullptr;
    if (s_vm) return s_vm;

    using GetCreatedVMs = jint (*)(JavaVM**, jsize, jsize*);
    GetCreatedVMs fn = nullptr;
    for (const char* so : {"libnativehelper.so", "libart.so", "libandroid_runtime.so"})
    {
        void* h = dlopen(so, RTLD_NOW | RTLD_NOLOAD);
        if (!h) h = dlopen(so, RTLD_NOW);
        if (!h) continue;
        fn = reinterpret_cast<GetCreatedVMs>(dlsym(h, "JNI_GetCreatedJavaVMs"));
        if (fn) break;
    }
    if (!fn)
    {
        LOGE("jni", "JNI_GetCreatedJavaVMs 未找到");
        return nullptr;
    }

    JavaVM* vm = nullptr;
    jsize count = 0;
    if (fn(&vm, 1, &count) == JNI_OK && count > 0)
    {
        s_vm = vm;
        return s_vm;
    }
    LOGE("jni", "JNI_GetCreatedJavaVMs 失败，count=%d", (int)count);
    return nullptr;
}

JNIEnv* GetJavaEnv()
{
    JavaVM* vm = GetJavaVM();
    if (!vm) return nullptr;
    JNIEnv* env = nullptr;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_OK) return env;
    return nullptr;
}

namespace
{
    pthread_key_t g_detachKey;
    pthread_once_t g_detachOnce = PTHREAD_ONCE_INIT;
    bool g_detachKeyOk = false;

    void DetachOnExit(void*)
    {
        if (JavaVM* vm = GetJavaVM()) vm->DetachCurrentThread();
    }

    void CreateDetachKey()
    {
        g_detachKeyOk = pthread_key_create(&g_detachKey, DetachOnExit) == 0;
    }
}

JNIEnv* EnsureEnv()
{
    if (JNIEnv* env = GetJavaEnv()) return env;

    JavaVM* vm = GetJavaVM();
    if (!vm) return nullptr;

    JNIEnv* env = nullptr;
    if (vm->AttachCurrentThread(&env, nullptr) != JNI_OK)
    {
        LOGE("jni", "AttachCurrentThread 失败");
        return nullptr;
    }
    pthread_once(&g_detachOnce, CreateDetachKey);
    if (g_detachKeyOk) pthread_setspecific(g_detachKey, env);
    return env;
}

} // namespace AndroidPlatform
