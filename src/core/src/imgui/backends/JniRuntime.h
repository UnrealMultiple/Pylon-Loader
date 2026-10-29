#pragma once

#include <jni.h>

namespace AndroidPlatform
{
    JavaVM* GetJavaVM();
    JNIEnv* GetJavaEnv();
}
