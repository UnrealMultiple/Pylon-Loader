// 层：内核层 / graphics —— 相机钩子实现
#include "graphics/CameraHook.h"

#include "graphics/Hook.h"
#include "graphics/Marker.h"
#include "graphics/Pipe.h"
#include "il2cpp/Domain.h"
#include "pylon/Log.h"
#include "pylon/mod/ModApi.h"

namespace pylon::graphics {

namespace {

using FireFn = void (*)(il2cpp::Il2CppObject* camera, const il2cpp::MethodInfo* method);

FireFn g_original = nullptr;
bool g_installed = false;
bool g_logged = false;

il2cpp::Field g_runnerInstance;
int32_t g_worldSettingsOffset = -1;
int32_t g_uiSettingsOffset = -1;
int32_t g_worldCameraOffset = -1;
int32_t g_uiCameraOffset = -1;

il2cpp::Il2CppObject* readRef(il2cpp::Il2CppObject* object, int32_t offset) {
    if (object == nullptr || offset < 0) return nullptr;
    return *reinterpret_cast<il2cpp::Il2CppObject**>(reinterpret_cast<char*>(object) + offset);
}

// 安装时已验证可读，逐帧不再看返回值。
il2cpp::Il2CppObject* runner() {
    il2cpp::Il2CppObject* instance = nullptr;
    g_runnerInstance.Read(nullptr, &instance);
    return instance;
}

Outcome<int32_t> FieldOffset(const il2cpp::Session& session, il2cpp::Il2CppClass* klass, const char* field) {
    auto found = il2cpp::Field::Resolve(session, klass, field);
    if (!found) return Outcome<int32_t>::Bad(found.reason);
    return Outcome<int32_t>::Good(static_cast<int32_t>(session.raw().field_get_offset(found.value.info())));
}

bool resolveCameras(const il2cpp::Session& session) {
    const auto runnerClass = session.FindClass("", "XNAUnityRunner");
    const auto worldClass = session.FindClass("", "XNAWorldCameraSettings");
    const auto uiClass = session.FindClass("", "XNAUICameraSettings");
    if (!runnerClass || !worldClass || !uiClass) return false;

    auto instanceField = il2cpp::Field::Resolve(session, runnerClass.value, "_instance");
    if (!instanceField) return false;
    g_runnerInstance = instanceField.value;

    const auto worldSettings = FieldOffset(session, runnerClass.value, "WorldCameraSettings");
    const auto uiSettings = FieldOffset(session, runnerClass.value, "UICameraSettings");
    const auto worldCamera = FieldOffset(session, worldClass.value, "Camera");
    const auto uiCamera = FieldOffset(session, uiClass.value, "Camera");
    if (!worldSettings || !uiSettings || !worldCamera || !uiCamera) return false;

    g_worldSettingsOffset = worldSettings.value;
    g_uiSettingsOffset = uiSettings.value;
    g_worldCameraOffset = worldCamera.value;
    g_uiCameraOffset = uiCamera.value;
    return true;
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
    auto attached = il2cpp::Domain::Attach();
    if (!attached) {
        LOGE("fx", "attach 失败，相机钩子不安装：%s", attached.reason.c_str());
        return false;
    }
    const il2cpp::Session& session = attached.value;

    if (!resolveCameras(session)) {
        LOGE("fx", "XNAUnityRunner 的相机没找齐：屏幕效果不可用");
        return false;
    }

    // 读不动就别装钩子：每帧都读一次，失败会被埋在渲染线程里。
    il2cpp::Il2CppObject* probe = nullptr;
    if (Status read = g_runnerInstance.Read(nullptr, &probe); !read) {
        LOGE("fx", "XNAUnityRunner._instance 读不到：%s", read.reason.c_str());
        return false;
    }

    auto target = reinterpret_cast<FireFn>(PylonMod_MethodPointer("UnityEngine.Camera", "FireOnPostRender", 1));
    if (target == nullptr) {
        LOGE("fx", "拿不到 Camera.FireOnPostRender：%s", PylonMod_LastError());
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
