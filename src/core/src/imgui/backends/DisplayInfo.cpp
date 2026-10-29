#include "DisplayInfo.h"

#include <jni.h>

#include "ImGui_Activity.h"
#include "JniRuntime.h"
#include "Logger.h"

namespace AndroidPlatform
{

namespace
{

DisplaySize s_cached{};
bool s_cacheReady = false;

bool ResolveDisplaySize(JNIEnv* env, jobject activity, DisplaySize& out)
{
    if (!activity) return false;

    // activity.getSystemService("window")
    jclass actClass = env->GetObjectClass(activity);
    jmethodID getSystemService = env->GetMethodID(actClass,
        "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;");
    if (!getSystemService || env->ExceptionCheck()) { env->ExceptionClear(); return false; }

    jstring serviceName = env->NewStringUTF("window");
    jobject windowMgr   = env->CallObjectMethod(activity, getSystemService, serviceName);
    env->DeleteLocalRef(serviceName);
    if (!windowMgr || env->ExceptionCheck()) { env->ExceptionClear(); return false; }

    // windowMgr.getDefaultDisplay()
    jclass wmClass = env->FindClass("android/view/WindowManager");
    jmethodID getDefaultDisplay = env->GetMethodID(wmClass,
        "getDefaultDisplay", "()Landroid/view/Display;");
    if (!getDefaultDisplay || env->ExceptionCheck()) { env->ExceptionClear(); return false; }

    jobject display = env->CallObjectMethod(windowMgr, getDefaultDisplay);
    if (!display || env->ExceptionCheck()) { env->ExceptionClear(); return false; }

    // display.getRealSize(Point)
    jclass displayClass = env->GetObjectClass(display);
    jmethodID getRealSize = env->GetMethodID(displayClass, "getRealSize", "(Landroid/graphics/Point;)V");
    if (!getRealSize || env->ExceptionCheck()) { env->ExceptionClear(); return false; }

    jclass pointClass = env->FindClass("android/graphics/Point");
    jmethodID pointCtor = env->GetMethodID(pointClass, "<init>", "()V");
    jobject point = env->NewObject(pointClass, pointCtor);
    if (!point || env->ExceptionCheck()) { env->ExceptionClear(); return false; }

    env->CallVoidMethod(display, getRealSize, point);
    if (env->ExceptionCheck()) { env->ExceptionClear(); return false; }

    jfieldID xField = env->GetFieldID(pointClass, "x", "I");
    jfieldID yField = env->GetFieldID(pointClass, "y", "I");
    jint x = env->GetIntField(point, xField);
    jint y = env->GetIntField(point, yField);

    out.width = x;
    out.height = y;

    return out.width > 0 && out.height > 0;
}

} // anonymous namespace

DisplaySize GetDisplaySize()
{
    jobject activity = GetActivity();
    if (!activity)
        return s_cacheReady ? s_cached : DisplaySize{};

    JavaVM* vm = GetJavaVM();
    if (!vm)
        return s_cacheReady ? s_cached : DisplaySize{};

    JNIEnv* env       = nullptr;
    bool    needDetach = false;

    if (vm->GetEnv((void**)&env, JNI_VERSION_1_6) != JNI_OK)
    {
        if (vm->AttachCurrentThread(&env, nullptr) != JNI_OK)
        {
            LOGE("[DisplayInfo] AttachCurrentThread failed");
            return s_cacheReady ? s_cached : DisplaySize{};
        }
        needDetach = true;
    }

    DisplaySize size{};
    bool resolved = false;
    if (env->PushLocalFrame(16) == 0)
    {
        if (ResolveDisplaySize(env, activity, size))
        {
            resolved = true;
            s_cached     = size;
            s_cacheReady = true;
            LOGI("[DisplayInfo] DisplaySize=%dx%d", size.width, size.height);
        }
        env->PopLocalFrame(nullptr);
    }

    if (env->ExceptionCheck())
        env->ExceptionClear();

    if (needDetach)
        vm->DetachCurrentThread();

    if (resolved)
        return size;
    return s_cacheReady ? s_cached : DisplaySize{};
}

}
