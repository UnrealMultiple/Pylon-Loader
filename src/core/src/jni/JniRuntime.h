// 层：内核层 / jni —— JavaVM 与 JNIEnv 的唯一来源
// 内核不依赖原工程的 ElfScanner：从 libart/libnativehelper 里 dlsym JNI_GetCreatedJavaVMs。
#pragma once

#include <jni.h>

namespace AndroidPlatform
{
    /// 进程内已创建的 JavaVM（失败返回 nullptr）。
    JavaVM* GetJavaVM();

    /// 当前线程已 attach 的 JNIEnv；未 attach 返回 nullptr（不隐式 attach）。
    JNIEnv* GetJavaEnv();

    /// 当前线程的 JNIEnv，必要时 attach 并登记线程退出时 detach。
    /// 未 attach 的线程若直接退出，ART 会在 epoll 回调里 abort 整个进程，所以 detach 必须登记。
    JNIEnv* EnsureEnv();
}
