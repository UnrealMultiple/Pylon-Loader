#include "ImGuiHost.h"

#include <atomic>
#include <chrono>
#include <limits>
#include <memory>
#include <mutex>
#include <thread>

#include <android/native_window.h>
#include <android/log.h>
#include <android/native_window_jni.h>
#include <jni.h>

#include "Logger.h"
#include "pylon/imgui/ImguiApi.h"
#include "../AndroidPlatform.h"
#include "../Graphics/VulkanBackend.h"
#include "../Render/ImGuiSetup.h"

namespace ImGuiHost
{

namespace
{

// 覆盖层窗口的 WindowManager.LayoutParams 标志位：组合出「悬浮于游戏之上、不抢焦点/触摸、全屏、硬件加速」的覆盖层。
constexpr jint kFlagNotFocusable         = 0x00000008;   // FLAG_NOT_FOCUSABLE
constexpr jint kFlagNotTouchable         = 0x00000010;   // FLAG_NOT_TOUCHABLE
constexpr jint kFlagNotTouchModal        = 0x00000020;   // FLAG_NOT_TOUCH_MODAL
constexpr jint kFlagLayoutInScreen       = 0x00000100;   // FLAG_LAYOUT_IN_SCREEN
constexpr jint kFlagLayoutNoLimits       = 0x00000200;   // FLAG_LAYOUT_NO_LIMITS
constexpr jint kFlagFullscreen           = 0x00000400;   // FLAG_FULLSCREEN
constexpr jint kFlagHardwareAccelerated  = 0x01000000;   // FLAG_HARDWARE_ACCELERATED
// 0x40000000 非公开标志位（原项目沿用），保留原始值以避免改变行为。
constexpr jint kOverlayBaseFlags =
    kFlagLayoutNoLimits | kFlagNotFocusable | kFlagNotTouchable |
    kFlagNotTouchModal | kFlagFullscreen | kFlagLayoutInScreen |
    kFlagHardwareAccelerated | 0x40000000;
constexpr jint kOverlayWindowType       = 2;             // WindowManager.LayoutParams.TYPE_PHONE
constexpr jint kOverlayGravity          = 3 | 48;        // Gravity.LEFT | Gravity.TOP

constexpr jint kPixelFormatTranslucent = -3;             // PixelFormat.TRANSLUCENT
constexpr jint kPixelFormatRgba8888 = 1;                 // PixelFormat.RGBA_8888
constexpr jint kSkipScreenshotFlag = 0x00000040;         // SurfaceControl 隐藏「跳过屏幕捕获」标志
constexpr jint kOverlayLayer = std::numeric_limits<jint>::max() - 1;
constexpr auto kBackgroundRebuildDelay = std::chrono::milliseconds(500);
constexpr auto kSurfaceHealthCheckInterval = std::chrono::seconds(1);
constexpr auto kSurfaceRefreshInterval = std::chrono::seconds(2);
constexpr auto kPresentationRecoveryDelay = std::chrono::seconds(3);
constexpr int kParentUnknownRebuildThreshold = 3;
constexpr int kSurfaceRefreshRebuildThreshold = 2;

std::mutex g_Mutex;
InitOptions g_Options;
std::unique_ptr<VulkanBackend> g_Backend;
ANativeWindow* g_OverlayWindow = nullptr;
jobject g_OverlaySurfaceView = nullptr;
jobject g_OverlaySurfaceControl = nullptr;
jobject g_OverlayParentSurfaceControl = nullptr;
jobject g_OverlaySurface = nullptr;
uint64_t g_ObservedPresentedFrames = 0;
std::chrono::steady_clock::time_point g_LastPresentationProgressAt;
std::atomic<bool> g_LifecycleMonitorEnabled{false};
std::atomic<bool> g_LifecycleMonitorStarted{false};
std::atomic<bool> g_ActivityFocusLost{false};
std::atomic<bool> g_ActivityWindowFocused{true};
int g_OverlayWidth = 0;
int g_OverlayHeight = 0;
bool g_Started = false;
bool g_ContextReady = false;
std::atomic<bool> g_Initialized{false};
std::atomic<bool> g_OverlayThreadStarted{false};
std::atomic<bool> g_CaptureHiding{false};
std::atomic<bool> g_SurfaceRebuildRequested{false};
std::atomic<bool> g_UsingSurfaceViewWindow{false};
std::atomic<bool> g_HiddenApiReady{false};
std::chrono::steady_clock::time_point g_LastTickAt;
std::chrono::steady_clock::time_point g_LastSurfaceHealthCheckAt;
std::chrono::steady_clock::time_point g_LastSurfaceRefreshAt;
uint64_t g_ObservedActivityGeneration = 0;
int g_ConsecutiveParentUnknownChecks = 0;
int g_ConsecutiveSurfaceRefreshFailures = 0;
int g_SurfaceControlReparentSupport = 0;
std::atomic<std::uint64_t> g_AcquireAttempts{0};

bool ShouldLogAcquire(std::uint64_t attempt)
{
    return attempt == 1 || attempt % 600 == 0;
}

bool ClearException(JNIEnv* env, const char* stage = nullptr)
{
    if (!env || !env->ExceptionCheck())
        return false;
    env->ExceptionClear();
    return true;
}

JNIEnv* GetEnv(bool& needDetach)
{
    needDetach = false;
    JavaVM* vm = AndroidPlatform::GetJavaVM();
    if (!vm)
        return nullptr;

    JNIEnv* env = nullptr;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_OK)
        return env;

    if (vm->AttachCurrentThread(&env, nullptr) != JNI_OK)
        return nullptr;

    needDetach = true;
    return env;
}

void DetachEnv(bool needDetach)
{
    if (!needDetach)
        return;

    JavaVM* vm = AndroidPlatform::GetJavaVM();
    if (vm)
        vm->DetachCurrentThread();
}

bool EnableHiddenApiExemptions(JNIEnv* env)
{
    if (!env)
        return false;
    if (g_HiddenApiReady.load())
        return true;
    if (env->PushLocalFrame(24) != 0)
        return false;

    bool enabled = false;
    jclass stringClass = env->FindClass("java/lang/String");
    jobjectArray exemptions = stringClass
        ? env->NewObjectArray(1, stringClass, nullptr) : nullptr;
    jstring prefix = env->NewStringUTF("L");
    if (!ClearException(env) && exemptions && prefix)
    {
        env->SetObjectArrayElement(exemptions, 0, prefix);
        ClearException(env);

        jclass zygoteClass = env->FindClass("com/android/internal/os/ZygoteInit");
        if (zygoteClass && !ClearException(env))
        {
            jmethodID setExemptions = env->GetStaticMethodID(
                zygoteClass, "setApiBlacklistExemptions",
                "([Ljava/lang/String;)V");
            const bool blacklistLookupFailed = ClearException(env);
            if (!setExemptions || blacklistLookupFailed)
            {
                setExemptions = env->GetStaticMethodID(
                    zygoteClass, "setApiDenylistExemptions",
                    "([Ljava/lang/String;)V");
                ClearException(env);
            }
            if (setExemptions)
            {
                env->CallStaticVoidMethod(
                    zygoteClass, setExemptions, exemptions);
                enabled = !ClearException(env);
            }
        }
        ClearException(env);

        if (!enabled)
        {
            jclass runtimeClass = env->FindClass("dalvik/system/VMRuntime");
            if (runtimeClass && !ClearException(env))
            {
                jmethodID getRuntime = env->GetStaticMethodID(
                    runtimeClass, "getRuntime", "()Ldalvik/system/VMRuntime;");
                const bool getRuntimeLookupFailed = ClearException(env);
                jmethodID setExemptions = nullptr;
                if (getRuntime && !getRuntimeLookupFailed)
                {
                    setExemptions = env->GetMethodID(
                        runtimeClass, "setHiddenApiExemptions",
                        "([Ljava/lang/String;)V");
                    ClearException(env);
                }
                if (getRuntime && setExemptions)
                {
                    jobject runtime = env->CallStaticObjectMethod(
                        runtimeClass, getRuntime);
                    if (runtime && !ClearException(env))
                    {
                        env->CallVoidMethod(runtime, setExemptions, exemptions);
                        enabled = !ClearException(env);
                    }
                }
            }
            ClearException(env);
        }
    }

    env->PopLocalFrame(nullptr);
    if (enabled)
        g_HiddenApiReady.store(true);
    return enabled;
}

void PrepareJavaLooper(JNIEnv* env)
{
    if (!env)
        return;

    jclass looperClass = env->FindClass("android/os/Looper");
    const bool classLookupFailed = ClearException(env, "Looper class");
    if (!looperClass || classLookupFailed)
        return;

    jmethodID myLooper = env->GetStaticMethodID(
        looperClass, "myLooper", "()Landroid/os/Looper;");
    jobject currentLooper = myLooper
        ? env->CallStaticObjectMethod(looperClass, myLooper) : nullptr;
    ClearException(env, "Looper.myLooper");

    if (!currentLooper)
    {
        jmethodID prepare = env->GetStaticMethodID(looperClass, "prepare", "()V");
        if (prepare && !ClearException(env, "Looper.prepare lookup"))
            env->CallStaticVoidMethod(looperClass, prepare);
        ClearException(env, "Looper.prepare");
    }
    else
    {
        env->DeleteLocalRef(currentLooper);
    }

    env->DeleteLocalRef(looperClass);
}

void LoopJavaLooper(JNIEnv* env)
{
    if (!env)
        return;

    jclass looperClass = env->FindClass("android/os/Looper");
    const bool classLookupFailed = ClearException(env, "Looper class for loop");
    if (!looperClass || classLookupFailed)
        return;

    jmethodID loop = env->GetStaticMethodID(looperClass, "loop", "()V");
    if (loop && !ClearException(env, "Looper.loop lookup"))
        env->CallStaticVoidMethod(looperClass, loop);
    ClearException(env, "Looper.loop");
    env->DeleteLocalRef(looperClass);
}

jobject CreateOverlayLayoutParams(JNIEnv* env, int width, int height)
{
    jclass paramsClass = env->FindClass("android/view/WindowManager$LayoutParams");
    const bool classLookupFailed = ClearException(env, "LayoutParams class");
    if (!paramsClass || classLookupFailed)
        return nullptr;

    jmethodID constructor = env->GetMethodID(paramsClass, "<init>", "()V");
    jobject params = constructor ? env->NewObject(paramsClass, constructor) : nullptr;
    const bool constructorFailed = ClearException(env, "LayoutParams constructor");
    if (!params || constructorFailed)
        return nullptr;

    jfieldID typeField = env->GetFieldID(paramsClass, "type", "I");
    ClearException(env, "LayoutParams.type");
    jfieldID flagsField = env->GetFieldID(paramsClass, "flags", "I");
    ClearException(env, "LayoutParams.flags");
    jfieldID gravityField = env->GetFieldID(paramsClass, "gravity", "I");
    ClearException(env, "LayoutParams.gravity");
    jfieldID formatField = env->GetFieldID(paramsClass, "format", "I");
    ClearException(env, "LayoutParams.format");
    jfieldID widthField = env->GetFieldID(paramsClass, "width", "I");
    ClearException(env, "LayoutParams.width");
    jfieldID heightField = env->GetFieldID(paramsClass, "height", "I");
    ClearException(env, "LayoutParams.height");
    if (!typeField || !flagsField || !gravityField || !formatField ||
        !widthField || !heightField)
        return nullptr;

    env->SetIntField(params, typeField, kOverlayWindowType);
    env->SetIntField(params, flagsField, kOverlayBaseFlags);
    env->SetIntField(params, gravityField, kOverlayGravity);
    env->SetIntField(params, formatField, kPixelFormatTranslucent);
    env->SetIntField(params, widthField, width);
    env->SetIntField(params, heightField, height);
    if (ClearException(env, "LayoutParams values"))
        return nullptr;

    return params;
}

struct OverlayViewRefs
{
    jobject surfaceView = nullptr;
};

OverlayViewRefs CreateAndAttachSurfaceView(JNIEnv* env, jobject activity,
                                           int width, int height)
{
    OverlayViewRefs refs{};
    if (!env || !activity || width <= 0 || height <= 0)
        return refs;

    jclass activityClass = env->GetObjectClass(activity);
    jmethodID getSystemService = activityClass ? env->GetMethodID(
        activityClass, "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;") : nullptr;
    jstring serviceName = env->NewStringUTF("window");
    jobject windowManager = (getSystemService && serviceName)
        ? env->CallObjectMethod(activity, getSystemService, serviceName) : nullptr;
    if (!windowManager || ClearException(env, "Activity.getSystemService(window)"))
        return refs;

    jclass surfaceViewClass = env->FindClass("android/view/SurfaceView");
    jmethodID surfaceViewConstructor = surfaceViewClass
        ? env->GetMethodID(surfaceViewClass, "<init>", "(Landroid/content/Context;)V")
        : nullptr;
    jobject surfaceView = surfaceViewConstructor
        ? env->NewObject(surfaceViewClass, surfaceViewConstructor, activity) : nullptr;
    if (!surfaceView || ClearException(env, "SurfaceView constructor"))
        return refs;

    jmethodID setZOrderOnTop = env->GetMethodID(
        surfaceViewClass, "setZOrderOnTop", "(Z)V");
    if (setZOrderOnTop)
        env->CallVoidMethod(surfaceView, setZOrderOnTop, JNI_TRUE);
    if (ClearException(env, "SurfaceView.setZOrderOnTop"))
        return refs;

    jmethodID getHolder = env->GetMethodID(
        surfaceViewClass, "getHolder", "()Landroid/view/SurfaceHolder;");
    jobject surfaceHolder = getHolder
        ? env->CallObjectMethod(surfaceView, getHolder) : nullptr;
    if (!surfaceHolder || ClearException(env, "SurfaceView.getHolder"))
        return refs;

    jclass holderClass = env->GetObjectClass(surfaceHolder);
    jmethodID setFormat = holderClass
        ? env->GetMethodID(holderClass, "setFormat", "(I)V") : nullptr;
    if (setFormat)
        env->CallVoidMethod(surfaceHolder, setFormat, kPixelFormatTranslucent);
    if (ClearException(env, "SurfaceHolder.setFormat"))
        return refs;

    jobject layoutParams = CreateOverlayLayoutParams(env, width, height);
    jclass windowManagerClass = env->GetObjectClass(windowManager);
    jmethodID addView = windowManagerClass ? env->GetMethodID(
        windowManagerClass, "addView",
        "(Landroid/view/View;Landroid/view/ViewGroup$LayoutParams;)V") : nullptr;
    if (!layoutParams || !addView || ClearException(env, "WindowManager.addView lookup"))
        return refs;

    env->CallVoidMethod(windowManager, addView, surfaceView, layoutParams);
    if (ClearException(env, "WindowManager.addView"))
        return refs;

    refs.surfaceView = env->NewGlobalRef(surfaceView);
    if (!refs.surfaceView || ClearException(env, "overlay global reference"))
    {
        if (refs.surfaceView) env->DeleteGlobalRef(refs.surfaceView);
        return {};
    }

    return refs;
}

void CreateOverlaySurfaceViewOnCurrentThread(JNIEnv* env)
{
    if (!env)
        return;

    jobject activity = AndroidPlatform::GetActivity();
    AndroidPlatform::DisplaySize size = AndroidPlatform::GetDisplaySize();
    if (!activity || size.width <= 0 || size.height <= 0)
        return;

    OverlayViewRefs refs{};
    if (env->PushLocalFrame(48) == 0)
    {
        refs = CreateAndAttachSurfaceView(
            env, activity, size.width, size.height);
        env->PopLocalFrame(nullptr);
    }
    ClearException(env, "SurfaceView creation frame");

    if (!refs.surfaceView)
        return;

    {
        std::lock_guard<std::mutex> lock(g_Mutex);
        g_OverlaySurfaceView = refs.surfaceView;
        g_OverlayWidth = size.width;
        g_OverlayHeight = size.height;
    }
}

void StartOverlaySurfaceThread()
{
    if (g_OverlayThreadStarted.exchange(true))
        return;

    std::thread([]()
    {
        bool needDetach = false;
        JNIEnv* env = GetEnv(needDetach);
        if (!env)
        {
            g_OverlayThreadStarted.store(false);
            return;
        }

        PrepareJavaLooper(env);
        EnableHiddenApiExemptions(env);
        CreateOverlaySurfaceViewOnCurrentThread(env);
        LoopJavaLooper(env);
        DetachEnv(needDetach);
        g_OverlayThreadStarted.store(false);
    }).detach();
}

void StartLifecycleMonitorThread()
{
    if (g_LifecycleMonitorStarted.exchange(true))
        return;

    std::thread([]()
    {
        bool needDetach = false;
        JNIEnv* env = GetEnv(needDetach);
        if (!env)
        {
            g_LifecycleMonitorStarted.store(false);
            return;
        }

        while (g_LifecycleMonitorEnabled.load())
        {
            bool focusKnown = false;
            bool focused = false;
            jobject activity = AndroidPlatform::GetActivity();
            if (activity && env->PushLocalFrame(8) == 0)
            {
                jclass activityClass = env->GetObjectClass(activity);
                jmethodID hasWindowFocus = activityClass
                    ? env->GetMethodID(activityClass, "hasWindowFocus", "()Z")
                    : nullptr;
                const bool lookupFailed = ClearException(env);
                if (hasWindowFocus && !lookupFailed)
                {
                    const jboolean result =
                        env->CallBooleanMethod(activity, hasWindowFocus);
                    if (!ClearException(env))
                    {
                        focusKnown = true;
                        focused = result == JNI_TRUE;
                    }
                }
                env->PopLocalFrame(nullptr);
            }
            ClearException(env);

            // Track the system IME: its show/hide toggles window focus; a fresh keyboard timestamp
            // lets Tick() tell that from a real background/resume cycle and skip the stuttering rebuild.

            if (focusKnown)
            {
                g_ActivityWindowFocused.store(focused);
                if (!focused)
                    g_ActivityFocusLost.store(true);
            }

            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }

        DetachEnv(needDetach);
        g_LifecycleMonitorStarted.store(false);
    }).detach();
}

jobject GetActivitySurfaceControl(JNIEnv* env, jobject activity)
{
    if (!env || !activity)
        return nullptr;

    jclass activityClass = env->FindClass("android/app/Activity");
    if (ClearException(env) || !activityClass)
        return nullptr;
    jmethodID getWindow = activityClass
        ? env->GetMethodID(
            activityClass, "getWindow", "()Landroid/view/Window;")
        : nullptr;
    if (ClearException(env) || !getWindow)
        return nullptr;
    jobject window = getWindow
        ? env->CallObjectMethod(activity, getWindow) : nullptr;
    const bool getWindowFailed = ClearException(env);
    if (!window || getWindowFailed)
        return nullptr;

    jclass windowClass = env->GetObjectClass(window);
    if (ClearException(env) || !windowClass)
        return nullptr;
    jmethodID getDecorView = windowClass
        ? env->GetMethodID(
            windowClass, "getDecorView", "()Landroid/view/View;")
        : nullptr;
    if (ClearException(env) || !getDecorView)
        return nullptr;
    jobject decorView = getDecorView
        ? env->CallObjectMethod(window, getDecorView) : nullptr;
    const bool getDecorViewFailed = ClearException(env);
    if (!decorView || getDecorViewFailed)
        return nullptr;

    jclass viewClass = env->GetObjectClass(decorView);
    if (ClearException(env) || !viewClass)
        return nullptr;
    jmethodID getViewRootImpl = viewClass
        ? env->GetMethodID(
            viewClass, "getViewRootImpl", "()Landroid/view/ViewRootImpl;")
        : nullptr;
    if (ClearException(env) || !getViewRootImpl)
        return nullptr;
    jobject viewRoot = getViewRootImpl
        ? env->CallObjectMethod(decorView, getViewRootImpl) : nullptr;
    const bool getViewRootFailed = ClearException(env);
    if (!viewRoot || getViewRootFailed)
        return nullptr;

    jclass viewRootClass = env->GetObjectClass(viewRoot);
    if (ClearException(env) || !viewRootClass)
        return nullptr;
    jmethodID getSurfaceControl = viewRootClass
        ? env->GetMethodID(
            viewRootClass, "getSurfaceControl",
            "()Landroid/view/SurfaceControl;")
        : nullptr;
    if (ClearException(env) || !getSurfaceControl)
        return nullptr;
    jobject surfaceControl = getSurfaceControl
        ? env->CallObjectMethod(viewRoot, getSurfaceControl) : nullptr;
    const bool getSurfaceControlFailed = ClearException(env);
    if (!surfaceControl || getSurfaceControlFailed)
        return nullptr;
    return surfaceControl;
}

ANativeWindow* AcquireSurfaceViewNativeWindow(JNIEnv* env, jobject surfaceView)
{
    if (!env || !surfaceView || env->PushLocalFrame(16) != 0)
        return nullptr;

    ANativeWindow* window = nullptr;
    do
    {
        jclass surfaceViewClass = env->GetObjectClass(surfaceView);
        jmethodID getHolder = surfaceViewClass
            ? env->GetMethodID(
                surfaceViewClass, "getHolder",
                "()Landroid/view/SurfaceHolder;")
            : nullptr;
        jobject surfaceHolder = getHolder
            ? env->CallObjectMethod(surfaceView, getHolder) : nullptr;
        if (!surfaceHolder ||
            ClearException(env, "fallback SurfaceView.getHolder"))
        {
            break;
        }

        jclass holderClass = env->GetObjectClass(surfaceHolder);
        jmethodID getSurface = holderClass
            ? env->GetMethodID(
                holderClass, "getSurface", "()Landroid/view/Surface;")
            : nullptr;
        jobject surface = getSurface
            ? env->CallObjectMethod(surfaceHolder, getSurface) : nullptr;
        if (!surface ||
            ClearException(env, "fallback SurfaceHolder.getSurface"))
        {
            break;
        }

        jclass surfaceClass = env->GetObjectClass(surface);
        jmethodID isValid = surfaceClass
            ? env->GetMethodID(surfaceClass, "isValid", "()Z") : nullptr;
        if (!isValid ||
            ClearException(env, "fallback Surface.isValid lookup"))
        {
            break;
        }

        const jboolean valid = env->CallBooleanMethod(surface, isValid);
        if (ClearException(env, "fallback Surface.isValid") ||
            valid != JNI_TRUE)
        {
            break;
        }

        window = ANativeWindow_fromSurface(env, surface);
    } while (false);

    env->PopLocalFrame(nullptr);
    return window;
}

bool SetBuilderSkipScreenshot(JNIEnv* env, jobject builder, jclass builderClass)
{
    jmethodID setFlags = env->GetMethodID(
        builderClass, "setFlags", "(II)Landroid/view/SurfaceControl$Builder;");
    const bool twoArgLookupFailed = ClearException(env, "Builder.setFlags(II) lookup");
    if (setFlags && !twoArgLookupFailed)
    {
        jobject result = env->CallObjectMethod(
            builder, setFlags, kSkipScreenshotFlag, kSkipScreenshotFlag);
        const bool applied = !ClearException(env, "Builder.setFlags");
        if (result)
            env->DeleteLocalRef(result);
        if (applied)
            return true;
    }
    ClearException(env, "Builder.setFlags fallback");

    setFlags = env->GetMethodID(
        builderClass, "setFlags", "(I)Landroid/view/SurfaceControl$Builder;");
    const bool oneArgLookupFailed = ClearException(env, "Builder.setFlags(I) lookup");
    if (setFlags && !oneArgLookupFailed)
    {
        jobject result = env->CallObjectMethod(
            builder, setFlags, kSkipScreenshotFlag);
        const bool applied = !ClearException(env, "Builder.setFlags(I)");
        if (result)
            env->DeleteLocalRef(result);
        if (applied)
            return true;
    }
    ClearException(env);

    jfieldID flagsField = env->GetFieldID(builderClass, "mFlags", "I");
    if (ClearException(env, "Builder.mFlags lookup") || !flagsField)
        return false;

    const jint flags = env->GetIntField(builder, flagsField);
    if (ClearException(env, "Builder.mFlags read"))
        return false;
    env->SetIntField(builder, flagsField, flags | kSkipScreenshotFlag);
    return !ClearException(env, "Builder.mFlags write");
}

jobject BuildOverlaySurfaceControl(JNIEnv* env, jobject parent,
                                   int width, int height, bool secure,
                                   bool* skipScreenshotApplied)
{
    if (skipScreenshotApplied)
        *skipScreenshotApplied = false;

    jclass builderClass = env->FindClass("android/view/SurfaceControl$Builder");
    if (ClearException(env, "SurfaceControl.Builder class") || !builderClass)
        return nullptr;

    jmethodID constructor = env->GetMethodID(builderClass, "<init>", "()V");
    if (ClearException(env, "SurfaceControl.Builder constructor lookup") || !constructor)
        return nullptr;
    jobject builder = constructor ? env->NewObject(builderClass, constructor) : nullptr;
    if (ClearException(env, "SurfaceControl.Builder constructor") || !builder)
        return nullptr;

    jmethodID setName = env->GetMethodID(
        builderClass, "setName", "(Ljava/lang/String;)Landroid/view/SurfaceControl$Builder;");
    if (ClearException(env, "Builder.setName lookup") || !setName)
        return nullptr;
    jstring name = env->NewStringUTF("Supernatural Vulkan Overlay");
    if (ClearException(env, "Builder name string") || !name)
        return nullptr;
    jobject result = env->CallObjectMethod(builder, setName, name);
    if (ClearException(env, "Builder.setName") || !result)
        return nullptr;
    env->DeleteLocalRef(result);

    jmethodID setBufferSize = env->GetMethodID(
        builderClass, "setBufferSize", "(II)Landroid/view/SurfaceControl$Builder;");
    if (ClearException(env, "Builder.setBufferSize lookup") || !setBufferSize)
        return nullptr;
    result = env->CallObjectMethod(builder, setBufferSize, width, height);
    if (ClearException(env, "Builder.setBufferSize") || !result)
        return nullptr;
    env->DeleteLocalRef(result);

    jmethodID setFormat = env->GetMethodID(
        builderClass, "setFormat", "(I)Landroid/view/SurfaceControl$Builder;");
    const bool setFormatLookupFailed = ClearException(env, "Builder.setFormat lookup");
    if (setFormat && !setFormatLookupFailed)
    {
        result = env->CallObjectMethod(builder, setFormat, kPixelFormatRgba8888);
        if (ClearException(env, "Builder.setFormat") || !result)
            return nullptr;
        env->DeleteLocalRef(result);
    }

    jmethodID setParent = env->GetMethodID(
        builderClass, "setParent",
        "(Landroid/view/SurfaceControl;)Landroid/view/SurfaceControl$Builder;");
    if (ClearException(env, "Builder.setParent lookup") || !setParent)
        return nullptr;
    result = env->CallObjectMethod(builder, setParent, parent);
    if (ClearException(env, "Builder.setParent") || !result)
        return nullptr;
    env->DeleteLocalRef(result);

    if (secure)
    {
        const bool applied =
            SetBuilderSkipScreenshot(env, builder, builderClass);
        if (skipScreenshotApplied)
            *skipScreenshotApplied = applied;
    }

    jmethodID build = env->GetMethodID(
        builderClass, "build", "()Landroid/view/SurfaceControl;");
    if (ClearException(env, "Builder.build lookup") || !build)
        return nullptr;
    jobject surfaceControl = env->CallObjectMethod(builder, build);
    if (ClearException(env, "Builder.build") || !surfaceControl)
        return nullptr;
    return surfaceControl;
}

bool TrySetTransactionBooleanFlag(JNIEnv* env, jobject transaction,
                                  jclass transactionClass, jobject surfaceControl,
                                  const char* methodName)
{
    jmethodID method = env->GetMethodID(
        transactionClass, methodName,
        "(Landroid/view/SurfaceControl;Z)Landroid/view/SurfaceControl$Transaction;");
    if (method && !ClearException(env))
    {
        jobject result = env->CallObjectMethod(
            transaction, method, surfaceControl, JNI_TRUE);
        const bool applied = !ClearException(env);
        if (result)
            env->DeleteLocalRef(result);
        if (applied)
            return true;
    }
    ClearException(env);

    method = env->GetMethodID(
        transactionClass, methodName,
        "(Landroid/view/SurfaceControl;Z)V");
    if (method && !ClearException(env))
    {
        env->CallVoidMethod(transaction, method, surfaceControl, JNI_TRUE);
        if (!ClearException(env))
            return true;
    }
    ClearException(env);
    return false;
}

bool ConfigureOverlaySurfaceControl(JNIEnv* env, jobject surfaceControl,
                                    bool secure, bool skipScreenshotApplied,
                                    jobject parent)
{
    jclass transactionClass = env->FindClass("android/view/SurfaceControl$Transaction");
    if (ClearException(env, "SurfaceControl.Transaction class") || !transactionClass)
        return false;

    jmethodID constructor = env->GetMethodID(transactionClass, "<init>", "()V");
    if (ClearException(env, "SurfaceControl.Transaction constructor lookup") || !constructor)
        return false;
    jobject transaction = env->NewObject(transactionClass, constructor);
    if (ClearException(env, "SurfaceControl.Transaction constructor") || !transaction)
        return false;

    jobject result = nullptr;
    if (parent && g_SurfaceControlReparentSupport >= 0)
    {
        jmethodID reparent = env->GetMethodID(
            transactionClass, "reparent",
            "(Landroid/view/SurfaceControl;Landroid/view/SurfaceControl;)Landroid/view/SurfaceControl$Transaction;");
        const bool reparentLookupFailed =
            ClearException(env, "Transaction.reparent lookup");
        if (!reparent || reparentLookupFailed)
        {
            g_SurfaceControlReparentSupport = -1;
        }
        else
        {
            g_SurfaceControlReparentSupport = 1;
            result = env->CallObjectMethod(
                transaction, reparent, surfaceControl, parent);
            if (ClearException(env, "Transaction.reparent") || !result)
                return false;
            env->DeleteLocalRef(result);
        }
    }

    jmethodID setAlpha = env->GetMethodID(
        transactionClass, "setAlpha",
        "(Landroid/view/SurfaceControl;F)Landroid/view/SurfaceControl$Transaction;");
    if (ClearException(env, "Transaction.setAlpha lookup") || !setAlpha)
        return false;
    result = env->CallObjectMethod(transaction, setAlpha, surfaceControl, 1.0f);
    if (ClearException(env, "Transaction.setAlpha") || !result)
        return false;
    env->DeleteLocalRef(result);

    if (secure && !skipScreenshotApplied)
    {
        skipScreenshotApplied = TrySetTransactionBooleanFlag(
            env, transaction, transactionClass, surfaceControl, "setSkipScreenshot");
    }
    if (secure && !skipScreenshotApplied)
        return false;

    jmethodID setLayer = env->GetMethodID(
        transactionClass, "setLayer",
        "(Landroid/view/SurfaceControl;I)Landroid/view/SurfaceControl$Transaction;");
    if (ClearException(env, "Transaction.setLayer lookup") || !setLayer)
        return false;
    result = env->CallObjectMethod(transaction, setLayer, surfaceControl, kOverlayLayer);
    if (ClearException(env, "Transaction.setLayer") || !result)
        return false;
    env->DeleteLocalRef(result);

    jmethodID setPosition = env->GetMethodID(
        transactionClass, "setPosition",
        "(Landroid/view/SurfaceControl;FF)Landroid/view/SurfaceControl$Transaction;");
    if (ClearException(env, "Transaction.setPosition lookup") || !setPosition)
        return false;
    result = env->CallObjectMethod(transaction, setPosition, surfaceControl, 0.0f, 0.0f);
    if (ClearException(env, "Transaction.setPosition") || !result)
        return false;
    env->DeleteLocalRef(result);

    jmethodID show = env->GetMethodID(
        transactionClass, "show",
        "(Landroid/view/SurfaceControl;)Landroid/view/SurfaceControl$Transaction;");
    if (ClearException(env, "Transaction.show lookup") || !show)
        return false;
    result = env->CallObjectMethod(transaction, show, surfaceControl);
    if (ClearException(env, "Transaction.show") || !result)
        return false;
    env->DeleteLocalRef(result);

    jmethodID apply = env->GetMethodID(transactionClass, "apply", "()V");
    if (ClearException(env, "Transaction.apply lookup") || !apply)
        return false;
    env->CallVoidMethod(transaction, apply);
    return !ClearException(env, "Transaction.apply") &&
           (!secure || skipScreenshotApplied);
}

jobject CreateSurfaceFromControl(JNIEnv* env, jobject surfaceControl)
{
    jclass surfaceClass = env->FindClass("android/view/Surface");
    if (ClearException(env, "Surface class") || !surfaceClass)
        return nullptr;

    jmethodID constructor = env->GetMethodID(
        surfaceClass, "<init>", "(Landroid/view/SurfaceControl;)V");
    if (ClearException(env, "Surface(SurfaceControl) lookup") || !constructor)
        return nullptr;
    jobject surface = env->NewObject(surfaceClass, constructor, surfaceControl);
    if (ClearException(env, "Surface(SurfaceControl)") || !surface)
        return nullptr;
    return surface;
}

enum class OverlayParentState
{
    Unknown,
    Current,
    Changed,
};

bool IsJavaSurfaceInvalid(JNIEnv* env, jobject object)
{
    if (!env || !object)
        return true;

    jclass objectClass = env->GetObjectClass(object);
    if (!objectClass || ClearException(env))
        return false;

    jmethodID isValid = env->GetMethodID(objectClass, "isValid", "()Z");
    if (!isValid || ClearException(env))
        return false;

    const jboolean valid = env->CallBooleanMethod(object, isValid);
    if (ClearException(env))
        return false;
    return valid != JNI_TRUE;
}

OverlayParentState CheckOverlayParentState()
{
    if (g_UsingSurfaceViewWindow.load())
    {
        if (!g_OverlayWindow)
            return OverlayParentState::Changed;
        return ANativeWindow_getWidth(g_OverlayWindow) > 0 &&
               ANativeWindow_getHeight(g_OverlayWindow) > 0
            ? OverlayParentState::Current
            : OverlayParentState::Changed;
    }
    if (!g_OverlayParentSurfaceControl)
        return OverlayParentState::Unknown;

    bool needDetach = false;
    JNIEnv* env = GetEnv(needDetach);
    if (!env)
        return OverlayParentState::Unknown;

    OverlayParentState state = OverlayParentState::Unknown;
    jobject activity = AndroidPlatform::GetActivity();
    if (activity && env->PushLocalFrame(16) == 0)
    {
        jobject currentParent = GetActivitySurfaceControl(env, activity);
        if (currentParent)
        {
            const bool sameParent =
                env->IsSameObject(currentParent, g_OverlayParentSurfaceControl) == JNI_TRUE;
            if (!ClearException(env))
            {
                state = sameParent
                    ? OverlayParentState::Current
                    : OverlayParentState::Changed;

                if (sameParent)
                {
                    if (IsJavaSurfaceInvalid(env, currentParent) ||
                        IsJavaSurfaceInvalid(env, g_OverlaySurfaceControl) ||
                        IsJavaSurfaceInvalid(env, g_OverlaySurface))
                        state = OverlayParentState::Changed;
                }
            }
        }
        env->PopLocalFrame(nullptr);
    }
    ClearException(env);
    DetachEnv(needDetach);
    return state;
}

bool RefreshOverlaySurfaceControl()
{
    if (!g_OverlaySurfaceControl)
        return false;

    bool needDetach = false;
    JNIEnv* env = GetEnv(needDetach);
    if (!env)
        return false;

    bool refreshed = false;
    if (env->PushLocalFrame(24) == 0)
    {
        jobject activity = AndroidPlatform::GetActivity();
        jobject parent = activity
            ? GetActivitySurfaceControl(env, activity) : nullptr;
        if (parent)
        {
            refreshed = ConfigureOverlaySurfaceControl(
                env, g_OverlaySurfaceControl, g_CaptureHiding.load(),
                true, parent);
        }
        env->PopLocalFrame(nullptr);
    }
    ClearException(env);
    DetachEnv(needDetach);
    return refreshed;
}

ANativeWindow* AcquireOverlayWindow()
{
    const std::uint64_t attempt =
        g_AcquireAttempts.fetch_add(1, std::memory_order_relaxed) + 1;
    bool needDetach = false;
    JNIEnv* env = GetEnv(needDetach);
    if (!env)
    {
        if (ShouldLogAcquire(attempt))
            LOGW("[ImGuiHost] no JNIEnv while acquiring overlay, attempt=%llu",
                 static_cast<unsigned long long>(attempt));
        return nullptr;
    }

    EnableHiddenApiExemptions(env);
    jobject activity = AndroidPlatform::GetActivity();
    if (!activity)
    {
        if (ShouldLogAcquire(attempt))
            LOGW("[ImGuiHost] no Activity while acquiring overlay, attempt=%llu",
                 static_cast<unsigned long long>(attempt));
        DetachEnv(needDetach);
        return nullptr;
    }

    if (g_OverlayWidth <= 0 || g_OverlayHeight <= 0)
    {
        const AndroidPlatform::DisplaySize size =
            AndroidPlatform::GetDisplaySize();
        g_OverlayWidth = size.width;
        g_OverlayHeight = size.height;
    }
    if (g_OverlayWidth <= 0 || g_OverlayHeight <= 0)
    {
        if (ShouldLogAcquire(attempt))
            LOGW("[ImGuiHost] invalid display size %dx%d, attempt=%llu",
                 g_OverlayWidth, g_OverlayHeight,
                 static_cast<unsigned long long>(attempt));
        DetachEnv(needDetach);
        return nullptr;
    }

    const bool secure = g_CaptureHiding.load();
    ANativeWindow* window = nullptr;
    jobject globalSurfaceControl = nullptr;
    jobject globalParentSurfaceControl = nullptr;
    jobject globalSurface = nullptr;
    if (env->PushLocalFrame(48) == 0)
    {
        jobject parent = GetActivitySurfaceControl(env, activity);
        if (parent)
        {
            bool skipScreenshotApplied = false;
            jobject surfaceControl = BuildOverlaySurfaceControl(
                env, parent, g_OverlayWidth, g_OverlayHeight,
                secure, &skipScreenshotApplied);
            if (surfaceControl && ConfigureOverlaySurfaceControl(
                    env, surfaceControl, secure,
                    skipScreenshotApplied, parent))
            {
                jobject surface = CreateSurfaceFromControl(env, surfaceControl);
                if (surface)
                    window = ANativeWindow_fromSurface(env, surface);
                if (window)
                {
                    globalSurfaceControl = env->NewGlobalRef(surfaceControl);
                    globalParentSurfaceControl = env->NewGlobalRef(parent);
                    globalSurface = env->NewGlobalRef(surface);
                }
            }
        }
        env->PopLocalFrame(nullptr);
    }

    if (!window || !globalSurfaceControl || !globalParentSurfaceControl ||
        !globalSurface)
    {
        if (window)
            ANativeWindow_release(window);
        if (globalSurfaceControl)
            env->DeleteGlobalRef(globalSurfaceControl);
        if (globalParentSurfaceControl)
            env->DeleteGlobalRef(globalParentSurfaceControl);
        if (globalSurface)
            env->DeleteGlobalRef(globalSurface);

        if (secure || !g_OverlaySurfaceView)
        {
            if (ShouldLogAcquire(attempt))
            {
                LOGW("[ImGuiHost] SurfaceControl failed; fallback unavailable "
                     "secure=%d surfaceView=%p attempt=%llu",
                     secure ? 1 : 0, g_OverlaySurfaceView,
                     static_cast<unsigned long long>(attempt));
            }
            g_UsingSurfaceViewWindow.store(false);
            DetachEnv(needDetach);
            return nullptr;
        }

        ANativeWindow* fallbackWindow =
            AcquireSurfaceViewNativeWindow(env, g_OverlaySurfaceView);
        g_UsingSurfaceViewWindow.store(fallbackWindow != nullptr);
        if (fallbackWindow)
            LOGI("[ImGuiHost] acquired SurfaceView fallback window=%p", fallbackWindow);
        else if (ShouldLogAcquire(attempt))
            LOGW("[ImGuiHost] SurfaceView fallback failed, attempt=%llu",
                 static_cast<unsigned long long>(attempt));
        DetachEnv(needDetach);
        return fallbackWindow;
    }

    g_OverlaySurfaceControl = globalSurfaceControl;
    g_OverlayParentSurfaceControl = globalParentSurfaceControl;
    g_OverlaySurface = globalSurface;
    g_UsingSurfaceViewWindow.store(false);
    LOGI("[ImGuiHost] acquired SurfaceControl window=%p size=%dx%d secure=%d",
         window, g_OverlayWidth, g_OverlayHeight, secure ? 1 : 0);
    DetachEnv(needDetach);
    return window;
}

void ShutdownBackendLocked()
{
    if (g_Backend)
    {
        g_Backend->Shutdown();
        g_Backend.reset();
    }

    g_Initialized.store(false);
    g_ObservedPresentedFrames = 0;
    g_LastPresentationProgressAt = {};
}

void ShutdownSurfaceRenderingLocked()
{
    ShutdownBackendLocked();

    if (g_OverlayWindow)
    {
        ANativeWindow_release(g_OverlayWindow);
        g_OverlayWindow = nullptr;
    }

}

void DestroyOverlaySurfaceControlLocked()
{
    if (!g_OverlaySurfaceControl && !g_OverlayParentSurfaceControl &&
        !g_OverlaySurface)
        return;

    bool needDetach = false;
    JNIEnv* env = GetEnv(needDetach);
    if (!env)
        return;

    if (g_OverlaySurfaceControl && env->PushLocalFrame(16) == 0)
    {
        jclass transactionClass = env->FindClass("android/view/SurfaceControl$Transaction");
        jmethodID constructor = transactionClass
            ? env->GetMethodID(transactionClass, "<init>", "()V") : nullptr;
        jobject transaction = constructor
            ? env->NewObject(transactionClass, constructor) : nullptr;
        if (transaction && !ClearException(env, "remove Transaction constructor"))
        {
            jmethodID remove = env->GetMethodID(
                transactionClass, "remove",
                "(Landroid/view/SurfaceControl;)Landroid/view/SurfaceControl$Transaction;");
            jobject result = remove
                ? env->CallObjectMethod(transaction, remove, g_OverlaySurfaceControl)
                : nullptr;
            if (result && !ClearException(env, "Transaction.remove"))
            {
                jmethodID apply = env->GetMethodID(transactionClass, "apply", "()V");
                if (apply && !ClearException(env, "remove Transaction.apply lookup"))
                    env->CallVoidMethod(transaction, apply);
                ClearException(env, "remove Transaction.apply");
            }
        }
        env->PopLocalFrame(nullptr);
    }

    if (g_OverlaySurfaceControl)
    {
        env->DeleteGlobalRef(g_OverlaySurfaceControl);
        g_OverlaySurfaceControl = nullptr;
    }
    if (g_OverlayParentSurfaceControl)
    {
        env->DeleteGlobalRef(g_OverlayParentSurfaceControl);
        g_OverlayParentSurfaceControl = nullptr;
    }
    if (g_OverlaySurface)
    {
        env->DeleteGlobalRef(g_OverlaySurface);
        g_OverlaySurface = nullptr;
    }
    DetachEnv(needDetach);
}

void UpdateInputTransformLocked()
{
    if (g_OverlayWidth <= 0 || g_OverlayHeight <= 0)
        return;

    AndroidPlatform::DisplaySize inputSize{};
    ANativeWindow* appWindow = AndroidPlatform::GetNativeWindow();
    if (appWindow)
    {
        inputSize.width = ANativeWindow_getWidth(appWindow);
        inputSize.height = ANativeWindow_getHeight(appWindow);
    }
    if (inputSize.width <= 0 || inputSize.height <= 0)
        inputSize = AndroidPlatform::GetDisplaySize();
    if (inputSize.width <= 0 || inputSize.height <= 0)
        inputSize = {g_OverlayWidth, g_OverlayHeight};

    int transform = 0;
    if ((g_OverlayWidth < g_OverlayHeight) != (inputSize.width < inputSize.height))
        transform = 1;

    // 触摸坐标变换改由 C# 喂入时处理（此工程原用 ImGui_ImplAndroid_SetInputTransform）

}

bool EnsureReadyLocked()
{
    if (!g_Started)
    {
        __android_log_print(ANDROID_LOG_INFO, "PylonLoader", "[imgui] EnsureReady: !g_Started");
        return false;
    }

    if (g_SurfaceRebuildRequested.exchange(false))
    {
        g_ConsecutiveParentUnknownChecks = 0;
        g_ConsecutiveSurfaceRefreshFailures = 0;
        ShutdownSurfaceRenderingLocked();
        DestroyOverlaySurfaceControlLocked();
        AndroidPlatform::InvalidateNativeWindow();
    }

    if (!g_OverlayWindow)
    {
        if (g_OverlaySurfaceControl)
            DestroyOverlaySurfaceControlLocked();
        g_OverlayWindow = AcquireOverlayWindow();
        __android_log_print(ANDROID_LOG_INFO, "PylonLoader", "[imgui] EnsureReady: AcquireOverlayWindow -> %p", (void*)g_OverlayWindow);
        if (!g_OverlayWindow)
            return false;
    }

    if (g_Backend && !g_Backend->IsReady())
    {
        const bool surfaceLost = g_Backend->RequiresSurfaceRebuild();
        ShutdownBackendLocked();
        if (surfaceLost)
        {
            ShutdownSurfaceRenderingLocked();
            DestroyOverlaySurfaceControlLocked();
            AndroidPlatform::InvalidateNativeWindow();
            return false;
        }
    }

    if (g_Backend && g_Backend->IsReady())
    {
        const int width = ANativeWindow_getWidth(g_OverlayWindow);
        const int height = ANativeWindow_getHeight(g_OverlayWindow);
        if (width <= 0 || height <= 0)
        {
            ShutdownSurfaceRenderingLocked();
            DestroyOverlaySurfaceControlLocked();
            AndroidPlatform::InvalidateNativeWindow();
            return false;
        }
        if (width == g_OverlayWidth && height == g_OverlayHeight)
            return true;

        ShutdownBackendLocked();
    }

    int windowWidth = ANativeWindow_getWidth(g_OverlayWindow);
    int windowHeight = ANativeWindow_getHeight(g_OverlayWindow);
    if (windowWidth <= 0 || windowHeight <= 0)
    {
        ShutdownSurfaceRenderingLocked();
        DestroyOverlaySurfaceControlLocked();
        AndroidPlatform::InvalidateNativeWindow();
        return false;
    }

    g_OverlayWidth = windowWidth;
    g_OverlayHeight = windowHeight;

    ANativeWindow* platformWindow = AndroidPlatform::GetNativeWindow();
    ImGuiSetup::EnsureContext(platformWindow ? platformWindow : g_OverlayWindow);
    g_ContextReady = true;

    g_Backend = std::make_unique<VulkanBackend>();
    if (!g_Backend->Init(g_OverlayWindow, g_OverlayWidth, g_OverlayHeight))
    {
        LOGE("[ImGuiHost] Vulkan backend initialization failed for %dx%d",
             g_OverlayWidth, g_OverlayHeight);
        ShutdownSurfaceRenderingLocked();
        DestroyOverlaySurfaceControlLocked();
        AndroidPlatform::InvalidateNativeWindow();
        return false;
    }

    g_Initialized.store(true);
    LOGI("[ImGuiHost] rendering ready window=%p size=%dx%d fallback=%d",
         g_OverlayWindow, g_OverlayWidth, g_OverlayHeight,
         g_UsingSurfaceViewWindow.load() ? 1 : 0);
    g_ObservedPresentedFrames = g_Backend->PresentedFrameCount();
    g_LastPresentationProgressAt = std::chrono::steady_clock::now();
    g_LastSurfaceRefreshAt = g_LastPresentationProgressAt;
    return true;
}

} // namespace

bool Init(const InitOptions& opt)
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    if (g_Started)
    {
        LOGW("[ImGuiHost] Init called while already started");
        return false;
    }

    g_Options = opt;
    g_CaptureHiding.store(opt.secureCapture);
    g_SurfaceRebuildRequested.store(false);
    g_UsingSurfaceViewWindow.store(false);
    g_ObservedPresentedFrames = 0;
    g_LastPresentationProgressAt = {};
    g_ActivityFocusLost.store(false);
    g_ActivityWindowFocused.store(true);
    g_LifecycleMonitorEnabled.store(true);
    g_LastTickAt = {};
    g_LastSurfaceHealthCheckAt = {};
    g_LastSurfaceRefreshAt = {};
    g_ObservedActivityGeneration = AndroidPlatform::GetActivityGeneration();
    g_ConsecutiveParentUnknownChecks = 0;
    g_ConsecutiveSurfaceRefreshFailures = 0;
    g_Started = true;
    LOGI("[ImGuiHost] Init accepted secureCapture=%d activityGeneration=%llu",
         opt.secureCapture ? 1 : 0,
         static_cast<unsigned long long>(g_ObservedActivityGeneration));
    StartOverlaySurfaceThread();
    if (g_LifecycleMonitorEnabled.load())
        StartLifecycleMonitorThread();
    return true;
}

void Clean()
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    g_Started = false;
    ShutdownSurfaceRenderingLocked();
    DestroyOverlaySurfaceControlLocked();
    AndroidPlatform::InvalidateNativeWindow();
    if (g_ContextReady)
    {
        ImGuiSetup::TeardownContext();
        g_ContextReady = false;
    }
    g_LifecycleMonitorEnabled.store(false);
    g_ActivityFocusLost.store(false);
    g_ActivityWindowFocused.store(true);
    g_LastTickAt = {};
    g_LastSurfaceHealthCheckAt = {};
    g_LastSurfaceRefreshAt = {};
    g_ObservedActivityGeneration = 0;
    g_ConsecutiveParentUnknownChecks = 0;
    g_ConsecutiveSurfaceRefreshFailures = 0;
}

void Tick()
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    const auto now = std::chrono::steady_clock::now();
    if (g_LifecycleMonitorEnabled.load())
        StartLifecycleMonitorThread();

    bool activityChanged = false;
    const uint64_t activityGeneration = AndroidPlatform::GetActivityGeneration();
    if (activityGeneration != 0 &&
        activityGeneration != g_ObservedActivityGeneration)
    {
        g_ObservedActivityGeneration = activityGeneration;
        g_SurfaceRebuildRequested.store(true);
        activityChanged = true;
    }

    const bool windowFocused = g_ActivityWindowFocused.load();
    const bool focusRegained =
        windowFocused && g_ActivityFocusLost.exchange(false);
    // IME 弹出/收起会切换窗口焦点，且（adjustResize 下）会重排甚至替换 Activity 的
    // Surface/SurfaceControl；键盘活跃期间（C# 键盘桥经 igSetKeyboardActive 驱动，1.5s 时效）
    // 的焦点回归/父 Surface 变化/解析失败都是键盘噪声，绝不能触发覆盖层 surface 重建（整屏闪
    // 一下，撞上键盘动画还会留下渲染冻结的覆盖层）。真实的后台/恢复仍由下面的 surface 健康检查
    // 与呈现停滞检查兜底（键盘时间戳过期后守卫自动失效）。
    const bool keyboardRecentlyActive = pylon::imgui::KeyboardRecentlyActive();
    if (focusRegained && !keyboardRecentlyActive)
        g_SurfaceRebuildRequested.store(true);

    const bool resumedAfterGap =
        g_LastTickAt.time_since_epoch().count() != 0 &&
        now - g_LastTickAt > kBackgroundRebuildDelay;
    const bool healthCheckDue =
        activityChanged || focusRegained || resumedAfterGap ||
        g_LastSurfaceHealthCheckAt.time_since_epoch().count() == 0 ||
        now - g_LastSurfaceHealthCheckAt >= kSurfaceHealthCheckInterval;
    if (healthCheckDue)
    {
        const OverlayParentState parentState = CheckOverlayParentState();
        if (parentState == OverlayParentState::Changed)
        {
            // 键盘会话期间的父 Surface 变化是 IME 引起的，跳过重建（等守卫过期后下次健康检查重新评估）。
            if (!keyboardRecentlyActive)
            {
                g_ConsecutiveParentUnknownChecks = 0;
                g_ConsecutiveSurfaceRefreshFailures = 0;
                g_SurfaceRebuildRequested.store(true);
            }
        }
        else if (parentState == OverlayParentState::Unknown)
        {
            // 键盘弹出的瞬间父 Surface 可能暂时解析不到；不把这类瞬时失败计入重建阈值。
            if (!keyboardRecentlyActive)
            {
                ++g_ConsecutiveParentUnknownChecks;
                if (g_ConsecutiveParentUnknownChecks >=
                    kParentUnknownRebuildThreshold)
                {
                    g_ConsecutiveParentUnknownChecks = 0;
                    g_SurfaceRebuildRequested.store(true);
                }
            }
        }
        else if (parentState == OverlayParentState::Current &&
                 !g_UsingSurfaceViewWindow.load() &&
                 (activityChanged || focusRegained || resumedAfterGap ||
                  g_LastSurfaceRefreshAt.time_since_epoch().count() == 0 ||
                  now - g_LastSurfaceRefreshAt >= kSurfaceRefreshInterval))
        {
            // 键盘活跃期间不碰 surface（不刷新也不计失败），避免与键盘动画互相干扰累积虚假失败重建。
            if (keyboardRecentlyActive)
            {
                g_ConsecutiveSurfaceRefreshFailures = 0;
            }
            else if (RefreshOverlaySurfaceControl())
            {
                g_ConsecutiveSurfaceRefreshFailures = 0;
                g_LastSurfaceRefreshAt = now;
            }
            else
            {
                ++g_ConsecutiveSurfaceRefreshFailures;
                if (g_ConsecutiveSurfaceRefreshFailures >=
                    kSurfaceRefreshRebuildThreshold)
                {
                    g_ConsecutiveSurfaceRefreshFailures = 0;
                    g_SurfaceRebuildRequested.store(true);
                }
            }
            g_ConsecutiveParentUnknownChecks = 0;
        }
        else if (parentState == OverlayParentState::Current)
        {
            g_ConsecutiveParentUnknownChecks = 0;
            g_ConsecutiveSurfaceRefreshFailures = 0;
        }
        g_LastSurfaceHealthCheckAt = now;
    }
    g_LastTickAt = now;

    if (!EnsureReadyLocked())
        return;

    const uint64_t presentedFrames = g_Backend->PresentedFrameCount();
    if (g_LastPresentationProgressAt.time_since_epoch().count() == 0 ||
        presentedFrames != g_ObservedPresentedFrames)
    {
        g_ObservedPresentedFrames = presentedFrames;
        g_LastPresentationProgressAt = now;
    }
    else if (now - g_LastPresentationProgressAt >= kPresentationRecoveryDelay)
    {
        g_SurfaceRebuildRequested.store(true);
        if (!EnsureReadyLocked())
            return;
    }
}

// 每帧渲染前半：Vulkan 取帧 + ImGui::NewFrame。必须与 EndFrame 同帧成对。
void BeginFrame()
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    if (!EnsureReadyLocked())
        return;

    UpdateInputTransformLocked();

    g_Backend->BeginFrame();
    ImGuiIO& io = ImGui::GetIO();
    if (g_OverlayWidth > 0 && g_OverlayHeight > 0)
    {
        io.DisplaySize = ImVec2(
            static_cast<float>(g_OverlayWidth),
            static_cast<float>(g_OverlayHeight));
        io.DisplayFramebufferScale = ImVec2(1.0f, 1.0f);
    }
    ImGui::NewFrame();
}

// 每帧渲染后半：ImGui::Render + 呈现。与 BeginFrame 成对。
void EndFrame()
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    ImGui::Render();
    g_Backend->EndFrame();
}

bool IsInitialized()
{
    return g_Initialized;
}

ImVec2 GetDisplaySize()
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    if (g_ContextReady)
        return ImGui::GetIO().DisplaySize;
    return ImVec2(
        static_cast<float>(g_OverlayWidth),
        static_cast<float>(g_OverlayHeight));
}

void SetCaptureHiding(bool enabled)
{
    if (g_CaptureHiding.exchange(enabled) == enabled)
        return;

    g_SurfaceRebuildRequested.store(true);
}

void RequestSurfaceRebuild()
{
    g_SurfaceRebuildRequested.store(true);
}

} // namespace ImGuiHost
