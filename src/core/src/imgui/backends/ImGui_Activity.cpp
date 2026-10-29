#include "ImGui_Activity.h"

#include <atomic>
#include <mutex>
#include <vector>

#include "JniRuntime.h"
#include "Logger.h"

namespace AndroidPlatform
{

namespace
{

std::mutex s_activityMutex;
std::vector<jobject> s_activityRefs;
jobject s_globalActivity = nullptr;
std::atomic<uint64_t> s_activityGeneration{0};

jobject ResolveAnyActivityLocal(JNIEnv* env)
{
    // 1. ActivityThread.currentActivityThread()
    jclass atClass = env->FindClass("android/app/ActivityThread");
    if (!atClass || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    jmethodID catMethod = env->GetStaticMethodID(atClass, "currentActivityThread",
        "()Landroid/app/ActivityThread;");
    if (!catMethod || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    jobject at = env->CallStaticObjectMethod(atClass, catMethod);
    if (!at || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    // 2. mActivities: ArrayMap<IBinder, ActivityClientRecord> (API 21+)
    jfieldID activitiesField = env->GetFieldID(atClass, "mActivities",
        "Landroid/util/ArrayMap;");
    if (!activitiesField || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    jobject activities = env->GetObjectField(at, activitiesField);
    if (!activities || env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }

    // 3. ArrayMap.size() / valueAt(i)
    jclass mapClass = env->GetObjectClass(activities);
    jmethodID sizeMethod    = env->GetMethodID(mapClass, "size", "()I");
    jmethodID valueAtMethod = env->GetMethodID(mapClass, "valueAt", "(I)Ljava/lang/Object;");
    if (!sizeMethod || !valueAtMethod) return nullptr;

    jint size = env->CallIntMethod(activities, sizeMethod);
    if (size <= 0) return nullptr;

    // Prefer the focused Activity: Unity may keep several non-finishing Activity records
    // during transitions, so valueAt(0) could bind the overlay and focus monitor to a stale window.
    jclass actClass = env->FindClass("android/app/Activity");
    jmethodID isFinishingMethod = (actClass && !env->ExceptionCheck())
        ? env->GetMethodID(actClass, "isFinishing", "()Z")
        : nullptr;
    if (env->ExceptionCheck()) env->ExceptionClear();
    jmethodID isDestroyedMethod = actClass
        ? env->GetMethodID(actClass, "isDestroyed", "()Z")
        : nullptr;
    if (env->ExceptionCheck()) env->ExceptionClear();
    jmethodID hasWindowFocusMethod = actClass
        ? env->GetMethodID(actClass, "hasWindowFocus", "()Z")
        : nullptr;
    if (env->ExceptionCheck()) env->ExceptionClear();

    jobject firstActivity   = nullptr;  // 兜底：第一个非 null 的 Activity
    jobject runningActivity = nullptr;  // 优先：第一个 !isFinishing() 的 Activity
    jobject focusedActivity = nullptr;  // 最优：当前真正拥有窗口焦点的 Activity

    for (jint i = 0; i < size; ++i)
    {
        jobject record = env->CallObjectMethod(activities, valueAtMethod, i);
        if (!record) continue;

        jclass recClass = env->GetObjectClass(record);
        jfieldID actField = env->GetFieldID(recClass, "activity", "Landroid/app/Activity;");
        if (!actField || env->ExceptionCheck()) { env->ExceptionClear(); continue; }

        jobject activity = env->GetObjectField(record, actField);
        if (!activity) continue;

        if (!firstActivity)
            firstActivity = activity;

        bool finishing = false;
        if (isFinishingMethod)
        {
            finishing = env->CallBooleanMethod(activity, isFinishingMethod) == JNI_TRUE;
            if (env->ExceptionCheck()) { env->ExceptionClear(); continue; }
        }

        bool destroyed = false;
        if (isDestroyedMethod)
        {
            destroyed = env->CallBooleanMethod(activity, isDestroyedMethod) == JNI_TRUE;
            if (env->ExceptionCheck()) { env->ExceptionClear(); continue; }
        }

        if (finishing || destroyed)
            continue;

        if (!runningActivity)
            runningActivity = activity;

        if (hasWindowFocusMethod)
        {
            const bool focused =
                env->CallBooleanMethod(activity, hasWindowFocusMethod) == JNI_TRUE;
            if (env->ExceptionCheck()) { env->ExceptionClear(); continue; }
            if (focused)
            {
                focusedActivity = activity;
                break;
            }
        }
    }

    return focusedActivity
        ? focusedActivity
        : (runningActivity ? runningActivity : firstActivity);
}

jobject FindOrCreateActivityRefLocked(JNIEnv* env, jobject local)
{
    for (jobject ref : s_activityRefs)
    {
        if (env->IsSameObject(ref, local) == JNI_TRUE)
            return ref;
        if (env->ExceptionCheck())
            env->ExceptionClear();
    }

    jobject ref = env->NewGlobalRef(local);
    if (ref)
        s_activityRefs.push_back(ref);
    return ref;
}

} // anonymous namespace

jobject GetActivity()
{
    JavaVM* vm = GetJavaVM();
    if (!vm)
        return nullptr;

    JNIEnv* env       = nullptr;
    bool    needDetach = false;

    if (vm->GetEnv((void**)&env, JNI_VERSION_1_6) != JNI_OK)
    {
        if (vm->AttachCurrentThread(&env, nullptr) != JNI_OK)
        {
            LOGE("[ActivityHost] AttachCurrentThread failed");
            return nullptr;
        }
        needDetach = true;
    }

    if (env->PushLocalFrame(32) == 0)
    {
        jobject local = ResolveAnyActivityLocal(env);
        if (local)
        {
            std::lock_guard<std::mutex> lock(s_activityMutex);
            if (!s_globalActivity ||
                env->IsSameObject(s_globalActivity, local) != JNI_TRUE)
            {
                if (env->ExceptionCheck())
                    env->ExceptionClear();
                jobject nextActivity = FindOrCreateActivityRefLocked(env, local);
                if (nextActivity)
                {
                    s_globalActivity = nextActivity;
                    const uint64_t generation =
                        s_activityGeneration.fetch_add(1) + 1;
                    LOGI("[ActivityHost] Activity=%p generation=%llu",
                         s_globalActivity,
                         static_cast<unsigned long long>(generation));
                }
            }
        }
        env->PopLocalFrame(nullptr);
    }

    if (env->ExceptionCheck())
        env->ExceptionClear();

    if (needDetach)
        vm->DetachCurrentThread();

    std::lock_guard<std::mutex> lock(s_activityMutex);
    return s_globalActivity;
}

uint64_t GetActivityGeneration()
{
    return s_activityGeneration.load();
}

}
