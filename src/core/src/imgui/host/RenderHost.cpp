#include "RenderHost.h"

#include <android/native_window.h>
#include <android/native_window_jni.h>

#include <chrono>
#include <mutex>

#include "imgui/render/VulkanBackend.h"
#include "jni/JniRuntime.h"
#include "pylon/Log.h"

namespace {

constexpr const char* kStage = "imgui";
constexpr int32_t kDefaultKeyboardGuardMs = 1500;

std::mutex g_mutex;                 // 保护下面全部字段：Attach 在游戏线程，取帧也在游戏线程
ANativeWindow* g_window = nullptr;   // 持有一份引用
VulkanBackend* g_backend = nullptr;
bool g_rebuild = false;              // 表面换过，下次取帧时重建
int32_t g_width = 0;
int32_t g_height = 0;
int32_t g_guardMs = kDefaultKeyboardGuardMs;
int64_t g_guardUntilNs = 0;

int64_t NowNs()
{
    return std::chrono::steady_clock::now().time_since_epoch().count();
}

void DestroyBackendLocked()
{
    if (g_backend == nullptr) return;
    g_backend->Shutdown();
    delete g_backend;
    g_backend = nullptr;
    g_width = 0;
    g_height = 0;
    g_guardUntilNs = 0;
}

void ReleaseWindowLocked()
{
    if (g_window == nullptr) return;
    ANativeWindow_release(g_window);
    g_window = nullptr;
}

// 表面不可用：后端、窗口、重建标记一起清掉。
void DropSurfaceLocked()
{
    DestroyBackendLocked();
    ReleaseWindowLocked();
    g_rebuild = false;
}

}  // namespace

extern "C" {

int32_t PylonImgui_AttachSurface(jobject surface)
{
    if (surface == nullptr) return 0;

    JNIEnv* env = AndroidPlatform::EnsureEnv();
    if (env == nullptr)
    {
        LOGE(kStage, "拿不到 JNIEnv");
        return 0;
    }

    ANativeWindow* window = ANativeWindow_fromSurface(env, surface);
    if (window == nullptr)
    {
        LOGE(kStage, "ANativeWindow_fromSurface 返回空");
        return 0;
    }

    std::lock_guard<std::mutex> lock(g_mutex);

    // 按指针判断换没换表面；切后台回来时 SurfaceView 会换一块新 Surface。
    if (g_window == window)
    {
        ANativeWindow_release(window);
        return 1;
    }

    const bool replacing = (g_window != nullptr);
    if (replacing)
    {
        LOGI(kStage, "检测到新表面，重建渲染器（旧表面已失效）");
        DestroyBackendLocked();
        ReleaseWindowLocked();
    }

    LOGI(kStage, "拿到表面 %p %dx%d", static_cast<void*>(window),
         ANativeWindow_getWidth(window), ANativeWindow_getHeight(window));
    g_window = window;
    g_rebuild = true;
    return 1;
}
void PylonImgui_DetachSurface()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    DropSurfaceLocked();
    LOGI(kStage, "surface detached");
}

int32_t PylonImgui_IsReady()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return (g_window != nullptr && g_backend != nullptr && g_backend->IsReady()) ? 1 : 0;
}

void PylonImgui_PixelSize(int32_t* width, int32_t* height)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    if (width) *width = g_width;
    if (height) *height = g_height;
}

int32_t PylonImgui_BeginFrame()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_window == nullptr) return 0;

    const int32_t width = ANativeWindow_getWidth(g_window);
    const int32_t height = ANativeWindow_getHeight(g_window);
    if (width <= 0 || height <= 0)
    {
        // 后台/旋转瞬间 Surface 可能已被收回；继续建交换链会让 Vulkan 驱动 abort。
        LOGI(kStage, "表面尺寸无效（%dx%d），等待新表面", width, height);
        DropSurfaceLocked();
        return 0;
    }

    if (g_rebuild)
    {
        DestroyBackendLocked();
        g_rebuild = false;
    }

    if (g_backend == nullptr)
    {
        auto* backend = new VulkanBackend();
        if (!backend->Init(g_window, width, height))
        {
            LOGE(kStage, "Vulkan 初始化失败（%dx%d）", width, height);
            delete backend;
            return 0;
        }
        g_backend = backend;
        g_width = width;
        g_height = height;
    }
    else if (width != g_width || height != g_height)
    {
        // 键盘动画期间尺寸会连抖几帧，跳过就是本帧不画。
        if (NowNs() < g_guardUntilNs) return 0;
        if (!g_backend->Resize(width, height))
        {
            DestroyBackendLocked();
            g_rebuild = true;
            return 0;
        }
        g_width = width;
        g_height = height;
    }
    else if (!g_backend->IsReady())
    {
        // Surface 丢失时窗口也可能一起失效，释放后由 Java 侧重新 AttachSurface。
        if (g_backend->RequiresSurfaceRebuild())
        {
            DropSurfaceLocked();
            LOGI(kStage, "Surface 丢失，等待重新接管");
        }
        else
        {
            DestroyBackendLocked();
            g_rebuild = true;
        }
        return 0;
    }

    g_backend->BeginFrame();
    return 1;
}

void PylonImgui_EndFrame()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_backend != nullptr) g_backend->EndFrame();
}

void PylonImgui_SetKeyboardGuardMs(int32_t ms)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    g_guardMs = ms > 0 ? ms : kDefaultKeyboardGuardMs;
    g_guardUntilNs = NowNs() + static_cast<int64_t>(g_guardMs) * 1000000LL;
}

}  // extern "C"