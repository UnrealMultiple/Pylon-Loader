#include "NativeWindowProvider.h"

#include <android/native_window_jni.h>
#include <jni.h>

#include "ImGui_Activity.h"
#include "JniRuntime.h"
#include "Logger.h"

namespace AndroidPlatform
{

namespace
{

ANativeWindow* s_cachedWindow = nullptr;

// 反射 Activity → Window → DecorView → ViewRootImpl → mSurface 解析出已 acquire 的 ANativeWindow*；失败返回 nullptr。
ANativeWindow* ResolveNativeWindow(JNIEnv* env, jobject activity)
{
    if (!activity) return nullptr;

    // Activity.getWindow()
    jclass actClass = env->FindClass("android/app/Activity");
    if (!actClass || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    jmethodID getWindowMethod = env->GetMethodID(actClass, "getWindow", "()Landroid/view/Window;");
    if (!getWindowMethod || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    jobject window = env->CallObjectMethod(activity, getWindowMethod);
    if (!window || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    // Window.getDecorView()
    jclass windowClass = env->GetObjectClass(window);
    jmethodID getDecorViewMethod = env->GetMethodID(windowClass, "getDecorView", "()Landroid/view/View;");
    if (!getDecorViewMethod || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    jobject decorView = env->CallObjectMethod(window, getDecorViewMethod);
    if (!decorView || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    // View.getViewRootImpl()（隐藏 API，所有 Android 版本均存在）
    jclass viewClass = env->FindClass("android/view/View");
    if (!viewClass || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    jmethodID getVRIMethod = env->GetMethodID(viewClass, "getViewRootImpl",
        "()Landroid/view/ViewRootImpl;");
    if (!getVRIMethod || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    jobject vri = env->CallObjectMethod(decorView, getVRIMethod);
    if (!vri || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    // ViewRootImpl.mSurface
    jclass vriClass = env->GetObjectClass(vri);
    jfieldID surfaceField = env->GetFieldID(vriClass, "mSurface", "Landroid/view/Surface;");
    if (!surfaceField || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    jobject surface = env->GetObjectField(vri, surfaceField);
    if (!surface || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    // Surface.isValid() 检查
    jclass surfClass = env->GetObjectClass(surface);
    jmethodID isValidMethod = env->GetMethodID(surfClass, "isValid", "()Z");
    if (isValidMethod && !env->ExceptionCheck())
    {
        jboolean valid = env->CallBooleanMethod(surface, isValidMethod);
        if (!valid)
        {
            LOGW("[NativeWindowProvider] Surface not valid");
            return nullptr;
        }
    }
    if (env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    ANativeWindow* result = ANativeWindow_fromSurface(env, surface);
    if (result)
    {
        LOGI("[NativeWindowProvider] ANativeWindow=%p (%dx%d)",
             result, ANativeWindow_getWidth(result), ANativeWindow_getHeight(result));
    }
    return result;
}

} // anonymous namespace

ANativeWindow* GetNativeWindow()
{
    if (s_cachedWindow)
    {
        if (ANativeWindow_getWidth(s_cachedWindow) > 0 &&
            ANativeWindow_getHeight(s_cachedWindow) > 0)
        {
            return s_cachedWindow;
        }

        ANativeWindow_release(s_cachedWindow);
        s_cachedWindow = nullptr;
    }

    jobject activity = GetActivity();
    if (!activity)
        return nullptr;

    JavaVM* vm = GetJavaVM();
    if (!vm)
        return nullptr;

    JNIEnv* env       = nullptr;
    bool    needDetach = false;

    if (vm->GetEnv((void**)&env, JNI_VERSION_1_6) != JNI_OK)
    {
        if (vm->AttachCurrentThread(&env, nullptr) != JNI_OK)
        {
            LOGE("[NativeWindowProvider] AttachCurrentThread failed");
            return nullptr;
        }
        needDetach = true;
    }

    if (env->PushLocalFrame(16) == 0)
    {
        s_cachedWindow = ResolveNativeWindow(env, activity);
        env->PopLocalFrame(nullptr);
    }

    if (env->ExceptionCheck())
        env->ExceptionClear();

    if (needDetach)
        vm->DetachCurrentThread();

    return s_cachedWindow;
}

void InvalidateNativeWindow()
{
    if (!s_cachedWindow)
        return;

    ANativeWindow_release(s_cachedWindow);
    s_cachedWindow = nullptr;
}

}
