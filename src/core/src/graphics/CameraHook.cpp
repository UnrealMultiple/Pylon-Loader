// 层：内核层 / graphics —— 相机钩子实现
#include "graphics/CameraHook.h"

#include <cstring>
#include <mutex>

#include "graphics/Hook.h"
#include "graphics/Marker.h"
#include "graphics/Pipe.h"
#include "il2cpp/Il2CppRuntime.h"
#include "pylon/Log.h"
#include "pylon/mod/ModApi.h"

namespace pylon::graphics {

namespace {

using FireFn = void (*)(il2cpp::Il2CppObject* camera, const il2cpp::MethodInfo* method);

FireFn g_original = nullptr;
bool g_installed = false;

il2cpp::FieldInfo* g_runnerInstance = nullptr;
int32_t g_worldSettingsOffset = -1, g_uiSettingsOffset = -1;
int32_t g_worldCameraOffset = -1, g_uiCameraOffset = -1;
bool g_logged = false;

il2cpp::Il2CppObject* readRef(il2cpp::Il2CppObject* object, int32_t offset) {
    if (object == nullptr || offset < 0) return nullptr;
    return *reinterpret_cast<il2cpp::Il2CppObject**>(reinterpret_cast<char*>(object) + offset);
}

il2cpp::Il2CppObject* runner() {
    il2cpp::Il2CppObject* instance = nullptr;
    il2cpp::Runtime::Instance().api().field_static_get_value(g_runnerInstance, &instance);
    return instance;
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
    g_worldSettingsOffset = static_cast<int32_t>(runtime.FieldOffset(worldSettings.value));
    g_uiSettingsOffset = static_cast<int32_t>(runtime.FieldOffset(uiSettings.value));
    g_worldCameraOffset = static_cast<int32_t>(runtime.FieldOffset(worldCamera.value));
    g_uiCameraOffset = static_cast<int32_t>(runtime.FieldOffset(uiCamera.value));
    return g_worldSettingsOffset >= 0 && g_uiSettingsOffset >= 0 && g_worldCameraOffset >= 0 &&
           g_uiCameraOffset >= 0;
}

void hookFireOnPostRender(il2cpp::Il2CppObject* camera, const il2cpp::MethodInfo* method) {
    g_original(camera, method);

    il2cpp::Il2CppObject* instance = runner();
    const il2cpp::Il2CppObject* world = readRef(readRef(instance, g_worldSettingsOffset), g_worldCameraOffset);
    const il2cpp::Il2CppObject* ui = readRef(readRef(instance, g_uiSettingsOffset), g_uiCameraOffset);
    if (camera == world) {
        Pipe::NoteCamera(Camera::World);
        if (!g_logged && world != nullptr && ui != nullptr) {
            g_logged = true;
            LOGI("fx", "相机：world %p / ui %p", world, ui);
        }
        if (Pipe::WorldPending() && MarkerReady()) {
            IssueMarker();
            Pipe::NoteMarkerIssued();
        }
        return;
    }
    Pipe::NoteCamera(camera == ui ? Camera::Ui : Camera::Other);
}

}  // namespace

bool InstallCameraHook() {
    auto& runtime = il2cpp::Runtime::Instance();
    runtime.Attach();
    auto& api = runtime.api();

    if (!resolveCameras()) {
        LOGE("fx", "XNAUnityRunner 的相机没找齐：屏幕效果不可用");
        return false;
    }

    const auto cameraClass = runtime.FindClass("UnityEngine", "Camera");
    const il2cpp::MethodInfo* fire =
        cameraClass ? api.class_get_method_from_name(cameraClass.value, "FireOnPostRender", 1) : nullptr;
    auto target = reinterpret_cast<FireFn>(
        fire != nullptr ? PylonMod_MethodPointer("UnityEngine.Camera", "FireOnPostRender", 1) : nullptr);
    if (target == nullptr) {
        LOGE("fx", "拿不到 Camera.FireOnPostRender");
        return false;
    }
    if (!Hook(target, &hookFireOnPostRender, &g_original)) return false;

    g_installed = true;
    LOGI("fx", "Camera.FireOnPostRender 已挂钩");
    return true;
}

bool CameraHookInstalled() {
    return g_installed;
}

}  // namespace pylon::graphics
