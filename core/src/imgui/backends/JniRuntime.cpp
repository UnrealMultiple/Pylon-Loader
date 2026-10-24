#include "JniRuntime.h"

#include <dlfcn.h>
#include <initializer_list>

#include "Logger.h"

namespace AndroidPlatform
{

// 与上游 CheatButton.cpp 同法：从 libart/libnativehelper 找 JNI_GetCreatedJavaVMs，不依赖原工程的 ElfScanner。
JavaVM* GetJavaVM()
{
    static JavaVM* s_vm = nullptr;
    if (s_vm) return s_vm;

    using JNI_GetCreatedJavaVMs_t = jint (*)(JavaVM**, jsize, jsize*);
    JNI_GetCreatedJavaVMs_t fn = nullptr;
    for (const char* so : {"libnativehelper.so", "libart.so", "libandroid_runtime.so"})
    {
        void* h = dlopen(so, RTLD_NOW | RTLD_NOLOAD);
        if (!h) h = dlopen(so, RTLD_NOW);
        if (!h) continue;
        fn = reinterpret_cast<JNI_GetCreatedJavaVMs_t>(dlsym(h, "JNI_GetCreatedJavaVMs"));
        if (fn) break;
    }
    if (!fn)
    {
        LOGE("[JniRuntime] GetJavaVM: JNI_GetCreatedJavaVMs nao encontrado");
        return nullptr;
    }

    JavaVM* vm = nullptr;
    jsize count = 0;
    if (fn(&vm, 1, &count) == JNI_OK && count > 0)
    {
        s_vm = vm;
        LOGI("[JniRuntime] GetJavaVM: got VM=%p", s_vm);
        return s_vm;
    }

    LOGE("[JniRuntime] GetJavaVM: JNI_GetCreatedJavaVMs failed, count=%d", (int)count);
    return nullptr;
}

JNIEnv* GetJavaEnv()
{
    JavaVM* vm = GetJavaVM();
    if (!vm)
    {
        LOGE("[JniRuntime] GetJavaEnv: JavaVM is null");
        return nullptr;
    }
    JNIEnv* env = nullptr;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_OK)
        return env;
    LOGW("[JniRuntime] GetJavaEnv: GetEnv failed (thread not attached?)");
    return nullptr;
}

} // namespace AndroidPlatform