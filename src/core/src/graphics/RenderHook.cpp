#include "graphics/RenderHook.h"
#include "graphics/GlHooks.h"
#include "graphics/EffectRenderer.h"

#include <chrono>
#include <cstring>
#include <deque>
#include <initializer_list>
#include <mutex>

#include "graphics/GuardedCall.h"
#include "il2cpp/Il2CppRuntime.h"
#include "pylon/Log.h"
#include "pylon/mod/ModApi.h"

namespace pylon::graphics {

namespace {

// IL2CPP 的 MethodInfo 首词就是该方法的原生代码指针（稳定 ABI，Bunny 同款取法）。
inline void* methodPointer(const il2cpp::MethodInfo* m) {
    return *reinterpret_cast<void* const*>(m);
}

using FireFn = void (*)(il2cpp::Il2CppObject* camera, const il2cpp::MethodInfo*);

FireFn g_origFire = nullptr;

il2cpp::FieldInfo* g_runnerInstance = nullptr;  // XNAUnityRunner._instance
int32_t g_worldSettingsOff = -1, g_uiSettingsOff = -1, g_worldCameraOff = -1, g_uiCameraOff = -1;
const il2cpp::MethodInfo* g_getTargetTexture = nullptr;
const il2cpp::MethodInfo* g_getClearFlags = nullptr;
const il2cpp::MethodInfo* g_getDepth = nullptr;
bool g_camerasLogged = false;

// 本帧效果：游戏线程的 submit 进 `g_pending`；世界相机的 FireOnPostRender 把它分到渲染线程
// 队列（按 marker 顺序，另一条给帧末）。没消费（marker 丢了）时队列在 kMaxQueued 停住。
constexpr size_t kMaxQueued = 4;
std::mutex g_queueMutex;
EffectFrame g_pending;
bool g_pendingSet = false;
std::deque<EffectFrame> g_worldQueue, g_screenQueue;

std::mutex g_statsMutex;
RenderStats g_stats;

// marker（见 kMarkerWidth）：Unity 对象用 gchandle 握死（IL2CPP 不搬对象，指针稳定）。
struct Marker {
    bool tried = false, ready = false;
    uint32_t handles[3] = {0, 0, 0};
    il2cpp::Il2CppObject* target = nullptr;
    il2cpp::Il2CppObject* source = nullptr;
    il2cpp::Il2CppObject* material = nullptr;
    const il2cpp::MethodInfo* blit = nullptr;  // Graphics.Internal_BlitMaterial5
};
Marker g_marker;

il2cpp::Il2CppObject* readRef(il2cpp::Il2CppObject* obj, int32_t offset) {
    if (!obj || offset < 0) return nullptr;
    return *reinterpret_cast<il2cpp::Il2CppObject**>(reinterpret_cast<char*>(obj) + offset);
}

il2cpp::Il2CppObject* runner() {
    il2cpp::Il2CppObject* r = nullptr;
    il2cpp::Runtime::Instance().api().field_static_get_value(g_runnerInstance, &r);
    return r;
}

il2cpp::Il2CppObject* invoke(const il2cpp::MethodInfo* method, il2cpp::Il2CppObject* self) {
    if (!method || !self) return nullptr;
    il2cpp::Il2CppObject* exc = nullptr;
    il2cpp::Il2CppObject* r = il2cpp::Runtime::Instance().api().runtime_invoke(method, self, nullptr, &exc);
    return exc ? nullptr : r;
}

template <typename T>
T unbox(il2cpp::Il2CppObject* boxed) {
    auto& a = il2cpp::Runtime::Instance().api();
    if (!boxed || !a.object_unbox) return T{};
    T out = T{};
    std::memcpy(&out, a.object_unbox(boxed), sizeof(T));
    return out;
}

void logCamera(const char* label, il2cpp::Il2CppObject* camera) {
    LOGI("fx", "camera %s %p：target %p, clearFlags %d, depth %.1f", label, camera,
         invoke(g_getTargetTexture, camera), unbox<int32_t>(invoke(g_getClearFlags, camera)),
         unbox<float>(invoke(g_getDepth, camera)));
}

// 按每个参数的类型名（"System.Int32"、"UnityEngine.Material"）找构造器：只按参数个数会
// 把 Material(Material) 当成 Material(Shader)，游戏直接卡死。
const il2cpp::MethodInfo* findCtor(il2cpp::Il2CppClass* cls, std::initializer_list<const char*> types) {
    auto& a = il2cpp::Runtime::Instance().api();
    void* iter = nullptr;
    while (const il2cpp::MethodInfo* m = a.class_get_methods(cls, &iter)) {
        if (std::strcmp(a.method_get_name(m), ".ctor") != 0) continue;
        if (a.method_get_param_count(m) != types.size()) continue;
        bool same = true;
        uint32_t i = 0;
        for (const char* type : types) {
            char* name = a.type_get_name(a.method_get_param(m, i++));
            same = same && name && std::strcmp(name, type) == 0;
            a.il2cpp_free(name);
        }
        if (same) return m;
    }
    return nullptr;
}

il2cpp::Il2CppObject* construct(il2cpp::Il2CppClass* cls, std::initializer_list<const char*> types, void** args) {
    auto& a = il2cpp::Runtime::Instance().api();
    const il2cpp::MethodInfo* ctor = cls ? findCtor(cls, types) : nullptr;
    if (!ctor) return nullptr;
    il2cpp::Il2CppObject* obj = a.object_new(cls);
    il2cpp::Il2CppObject* exc = nullptr;
    a.runtime_invoke(ctor, obj, args, &exc);
    return exc ? nullptr : obj;
}

// 一张 17x3 RenderTexture，用 Unity 的白纹理当源，COPY 一个游戏材质（Blit 会改材质的
// _MainTex；用原材质会把它的精灵全弄坏）。必须在游戏线程、相机绘制之外搭（在
// FireOnPostRender 里搭实测卡死：render multithread）。
void setupMarker() {
    g_marker.tried = true;
    auto& runtime = il2cpp::Runtime::Instance();
    // 按内核纪律：先 Attach 当前线程（attach 按线程生效；已 attach 立即返回）。
    runtime.Attach();
    auto& a = runtime.api();
    const auto renderTexture = runtime.FindClass("UnityEngine", "RenderTexture");
    const auto texture2D = runtime.FindClass("UnityEngine", "Texture2D");
    const auto material = runtime.FindClass("UnityEngine", "Material");
    const auto graphics = runtime.FindClass("UnityEngine", "Graphics");
    const auto device = runtime.FindClass("Microsoft.Xna.Framework.Graphics", "GraphicsDevice");
    if (!renderTexture || !texture2D || !material || !graphics || !device) {
        LOGE("fx", "marker 依赖的类没找齐（RenderTexture/Texture2D/Material/Graphics/GraphicsDevice）；"
                   "效果将画在帧末（盖住界面）");
        return;
    }

    int32_t w = kMarkerWidth, h = kMarkerHeight, depth = 0;
    void* rtArgs[3] = {&w, &h, &depth};
    il2cpp::Il2CppObject* target = construct(renderTexture.value, {"System.Int32", "System.Int32", "System.Int32"}, rtArgs);

    il2cpp::Il2CppObject* exc = nullptr;
    const il2cpp::MethodInfo* white = a.class_get_method_from_name(texture2D.value, "get_whiteTexture", 0);
    il2cpp::Il2CppObject* source = white ? a.runtime_invoke(white, nullptr, nullptr, &exc) : nullptr;

    il2cpp::Il2CppObject* instance = nullptr;
    const auto instanceField = runtime.FindField(device.value, "Instance");
    if (instanceField) a.field_static_get_value(instanceField.value, &instance);
    const il2cpp::MethodInfo* getMaterial = a.class_get_method_from_name(device.value, "GetMaterial", 1);
    int32_t offset = 0;
    void* matArgs[1] = {&offset};
    exc = nullptr;
    il2cpp::Il2CppObject* gameMaterial =
        instance && getMaterial ? a.runtime_invoke(getMaterial, instance, matArgs, &exc) : nullptr;
    void* copyArgs[1] = {gameMaterial};
    il2cpp::Il2CppObject* copy = gameMaterial && !exc ? construct(material.value, {"UnityEngine.Material"}, copyArgs) : nullptr;

    g_marker.blit = a.class_get_method_from_name(graphics.value, "Internal_BlitMaterial5", 5);
    if (!target || !source || !copy || !g_marker.blit) {
        LOGE("fx", "marker 没搭成（target %p, source %p, material %p, blit %p）；效果将画在帧末",
             target, source, copy, g_marker.blit);
        return;
    }
    g_marker.handles[0] = a.gchandle_new(target, false);
    g_marker.handles[1] = a.gchandle_new(source, false);
    g_marker.handles[2] = a.gchandle_new(copy, false);
    g_marker.target = target;
    g_marker.source = source;
    g_marker.material = copy;
    g_marker.ready = true;
    LOGI("fx", "marker 搭好");
}

// 发 marker。与 Bunny Loader 完全一致：methodPointer 直调 + 汇编受保护调用（GuardedCall.S）。
// 直调是 IL2CPP 原生签名（参数原样进寄存器，没有 runtime_invoke 的参数形状问题）；
// guarded 把托管异常（真实 C++ 异常）拦在我们边界，不让它穿过帧进游戏。
bool issueMarker() {
    using BlitFn = void (*)(il2cpp::Il2CppObject*, il2cpp::Il2CppObject*, il2cpp::Il2CppObject*, int32_t, bool,
                            const il2cpp::MethodInfo*);
    const auto blit = reinterpret_cast<BlitFn>(methodPointer(g_marker.blit));
    // setRT = true：false 时 Unity 不设目标，会画到当前活动目标（游戏的）上。
    auto call = [blit] { blit(g_marker.source, g_marker.target, g_marker.material, 0, true, g_marker.blit); };
    if (il2cpp::guarded(call)) return true;
    LOGE("fx", "marker 的 Blit 抛了；marker 关掉");
    g_marker.ready = false;
    return false;
}

void push(std::deque<EffectFrame>& queue, EffectFrame&& frame) {
    if (queue.size() >= kMaxQueued) queue.pop_front();
    queue.push_back(std::move(frame));
}

// 把 submit 留下的帧按层拆开（目标两边都有；上一帧只在世界层）。取走即消费：
// 下一帧没有 submit 就不画。
void splitPending(EffectFrame& world, EffectFrame& screen) {
    EffectFrame pending;
    {
        std::lock_guard<std::mutex> lock(g_queueMutex);
        if (!g_pendingSet) return;
        std::swap(pending, g_pending);
        g_pendingSet = false;
    }
    for (EffectDraw& e : pending.draws) {
        (e.layer == EffectLayer::Screen ? screen : world).draws.push_back(std::move(e));
    }
    world.targets = pending.targets;
    screen.targets = std::move(pending.targets);
    world.history = pending.history;
}

void hkFireOnPostRender(il2cpp::Il2CppObject* camera, const il2cpp::MethodInfo* method) {
    g_origFire(camera, method);

    il2cpp::Il2CppObject* r = runner();
    il2cpp::Il2CppObject* world = readRef(readRef(r, g_worldSettingsOff), g_worldCameraOff);
    il2cpp::Il2CppObject* ui = readRef(readRef(r, g_uiSettingsOff), g_uiCameraOff);
    if (!g_camerasLogged && world && ui) {
        g_camerasLogged = true;
        logCamera("world", world);
        logCamera("ui", ui);
    }

    bool issued = false;
    int64_t markerNs = 0;
    if (camera == world) {
        EffectFrame worldList, screenList;
        splitPending(worldList, screenList);
        if ((!worldList.empty() || worldList.history) && g_marker.ready) {
            const auto started = std::chrono::steady_clock::now();
            issued = issueMarker();
            markerNs = std::chrono::duration_cast<std::chrono::nanoseconds>(
                std::chrono::steady_clock::now() - started).count();
        }
        // 没有 marker：世界的效果挪到帧末（盖住界面）。
        if (!issued) {
            for (EffectDraw& e : worldList.draws) screenList.draws.push_back(std::move(e));
            screenList.history = screenList.history || worldList.history;
            worldList = EffectFrame();
        }
        std::lock_guard<std::mutex> lock(g_queueMutex);
        if (issued) push(g_worldQueue, std::move(worldList));
        if (!screenList.empty()) push(g_screenQueue, std::move(screenList));
    }

    std::lock_guard<std::mutex> lock(g_statsMutex);
    if (camera == world) g_stats.worldPostRender++;
    else if (camera == ui) g_stats.uiPostRender++;
    else g_stats.otherPostRender++;
    if (issued) g_stats.markersIssued++;
    g_stats.markerCpuNs += markerNs;
    g_stats.markerReady = g_marker.ready;
}

bool resolveCameras() {
    auto& runtime = il2cpp::Runtime::Instance();
    const auto runnerClass = runtime.FindClass("", "XNAUnityRunner");
    const auto worldClass = runtime.FindClass("", "XNAWorldCameraSettings");
    const auto uiClass = runtime.FindClass("", "XNAUICameraSettings");
    if (!runnerClass || !worldClass || !uiClass) return false;

    const auto runnerInstance = runtime.FindField(runnerClass.value, "_instance");
    const auto worldSettings = runtime.FindField(runnerClass.value, "WorldCameraSettings");
    const auto uiSettings = runtime.FindField(runnerClass.value, "UICameraSettings");
    const auto worldCamera = runtime.FindField(worldClass.value, "Camera");
    const auto uiCamera = runtime.FindField(uiClass.value, "Camera");
    if (!runnerInstance || !worldSettings || !uiSettings || !worldCamera || !uiCamera) return false;

    g_runnerInstance = runnerInstance.value;
    g_worldSettingsOff = static_cast<int32_t>(runtime.FieldOffset(worldSettings.value));
    g_uiSettingsOff = static_cast<int32_t>(runtime.FieldOffset(uiSettings.value));
    g_worldCameraOff = static_cast<int32_t>(runtime.FieldOffset(worldCamera.value));
    g_uiCameraOff = static_cast<int32_t>(runtime.FieldOffset(uiCamera.value));
    return g_runnerInstance && g_worldSettingsOff >= 0 && g_uiSettingsOff >= 0 && g_worldCameraOff >= 0 &&
           g_uiCameraOff >= 0;
}

}  // namespace

void installRenderHook() {
    auto& runtime = il2cpp::Runtime::Instance();
    // 按内核纪律：先 Attach 当前线程（游戏线程；已 attach 立即返回）。
    runtime.Attach();
    auto& a = runtime.api();
    if (!resolveCameras()) {
        LOGE("fx", "XNAUnityRunner 的相机没找齐；屏幕效果不可用");
        return;
    }

    const auto camera = runtime.FindClass("UnityEngine", "Camera");
    const il2cpp::MethodInfo* fire = camera ? a.class_get_method_from_name(camera.value, "FireOnPostRender", 1) : nullptr;
    if (camera) {
        g_getTargetTexture = a.class_get_method_from_name(camera.value, "get_targetTexture", 0);
        g_getClearFlags = a.class_get_method_from_name(camera.value, "get_clearFlags", 0);
        g_getDepth = a.class_get_method_from_name(camera.value, "get_depth", 0);
    }

    bool hooked = false;
    if (fire) {
        void* code = PylonMod_MethodPointer("UnityEngine.Camera", "FireOnPostRender", 1);
        if (code) {
            void* trampoline = nullptr;
            const int rc = PylonMod_HookApply(code, reinterpret_cast<void*>(&hkFireOnPostRender), &trampoline);
            if (rc == 0 && trampoline) {
                g_origFire = reinterpret_cast<FireFn>(trampoline);
                hooked = true;
            } else {
                LOGE("fx", "Camera.FireOnPostRender 挂钩失败（rc=%d）", rc);
            }
        } else {
            LOGE("fx", "Camera.FireOnPostRender 拿不到方法指针");
        }
    }
    {
        std::lock_guard<std::mutex> lock(g_statsMutex);
        g_stats.hookInstalled = hooked;
    }
    if (hooked) LOGI("fx", "Camera.FireOnPostRender 已挂钩");

    installGlHooks();
}

void noteMarkerHit() {
    std::lock_guard<std::mutex> lock(g_statsMutex);
    g_stats.markerHits++;
}

bool renderHookInstalled() {
    std::lock_guard<std::mutex> lock(g_statsMutex);
    return g_stats.hookInstalled;
}

void submitEffects(EffectFrame&& frame) {
    // marker 在游戏线程搭，且必须在相机绘制之外。
    if (!frame.empty() && !g_marker.tried) setupMarker();
    std::lock_guard<std::mutex> lock(g_queueMutex);
    g_pending = std::move(frame);
    g_pendingSet = true;
}

bool takeWorldEffects(EffectFrame& out) {
    std::lock_guard<std::mutex> lock(g_queueMutex);
    if (g_worldQueue.empty()) return false;
    out = std::move(g_worldQueue.front());
    g_worldQueue.pop_front();
    return true;
}

bool takeScreenEffects(EffectFrame& out) {
    std::lock_guard<std::mutex> lock(g_queueMutex);
    if (g_screenQueue.empty()) return false;
    out = std::move(g_screenQueue.front());
    g_screenQueue.pop_front();
    std::lock_guard<std::mutex> statsLock(g_statsMutex);
    g_stats.screenFrames++;
    return true;
}

RenderStats renderStats() {
    RenderStats copy;
    {
        std::lock_guard<std::mutex> lock(g_statsMutex);
        copy = g_stats;
    }
    EffectRenderer::FillStats(copy);
    fillGlHookStats(copy);
    return copy;
}

}  // namespace pylon::graphics
