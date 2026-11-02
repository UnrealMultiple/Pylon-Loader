// 层：内核层 / graphics —— 标记实现
#include "graphics/Marker.h"

#include <cstring>
#include <initializer_list>

#include "il2cpp/GuardedCall.h"
#include "il2cpp/Il2CppRuntime.h"
#include "pylon/Log.h"

namespace pylon::graphics {

namespace {

constexpr int kWidth = kMarkerWidth;
constexpr int kHeight = kMarkerHeight;

struct Marker {
    bool ready = false;
    uint32_t handles[3] = {0, 0, 0};
    il2cpp::Il2CppObject* source = nullptr;
    il2cpp::Il2CppObject* target = nullptr;
    il2cpp::Il2CppObject* material = nullptr;
    const il2cpp::MethodInfo* blit = nullptr;
};

Marker g_marker;

// IL2CPP 的 MethodInfo 首词是原生代码指针（稳定 ABI）。
void* methodPointer(const il2cpp::MethodInfo* method) {
    return *reinterpret_cast<void* const*>(method);
}

const il2cpp::MethodInfo* findCtor(il2cpp::Il2CppClass* cls, std::initializer_list<const char*> types) {
    auto& api = il2cpp::Runtime::Instance().api();
    void* iter = nullptr;
    while (const il2cpp::MethodInfo* method = api.class_get_methods(cls, &iter)) {
        if (std::strcmp(api.method_get_name(method), ".ctor") != 0) continue;
        if (api.method_get_param_count(method) != types.size()) continue;
        bool same = true;
        uint32_t index = 0;
        for (const char* type : types) {
            char* name = api.type_get_name(api.method_get_param(method, index++));
            same = same && name != nullptr && std::strcmp(name, type) == 0;
            api.il2cpp_free(name);
        }
        if (same) return method;
    }
    return nullptr;
}

il2cpp::Il2CppObject* construct(il2cpp::Il2CppClass* cls, std::initializer_list<const char*> types, void** args) {
    auto& api = il2cpp::Runtime::Instance().api();
    const il2cpp::MethodInfo* ctor = cls != nullptr ? findCtor(cls, types) : nullptr;
    if (ctor == nullptr) return nullptr;
    il2cpp::Il2CppObject* object = api.object_new(cls);
    il2cpp::Il2CppObject* exception = nullptr;
    api.runtime_invoke(ctor, object, args, &exception);
    return exception != nullptr ? nullptr : object;
}

}  // namespace

bool CreateMarker() {
    auto& runtime = il2cpp::Runtime::Instance();
    runtime.Attach();
    auto& api = runtime.api();

    const auto renderTexture = runtime.FindClass("UnityEngine", "RenderTexture");
    const auto texture2D = runtime.FindClass("UnityEngine", "Texture2D");
    const auto material = runtime.FindClass("UnityEngine", "Material");
    const auto graphics = runtime.FindClass("UnityEngine", "Graphics");
    const auto device = runtime.FindClass("Microsoft.Xna.Framework.Graphics", "GraphicsDevice");
    if (!renderTexture || !texture2D || !material || !graphics || !device) {
        LOGE("fx", "标记依赖的类没找齐：世界层关闭");
        return false;
    }

    int width = kWidth, height = kHeight, depth = 0;
    void* targetArgs[3] = {&width, &height, &depth};
    il2cpp::Il2CppObject* target =
        construct(renderTexture.value, {"System.Int32", "System.Int32", "System.Int32"}, targetArgs);

    il2cpp::Il2CppObject* exception = nullptr;
    const il2cpp::MethodInfo* white = api.class_get_method_from_name(texture2D.value, "get_whiteTexture", 0);
    il2cpp::Il2CppObject* source = white != nullptr ? api.runtime_invoke(white, nullptr, nullptr, &exception) : nullptr;

    il2cpp::Il2CppObject* instance = nullptr;
    const auto instanceField = runtime.FindField(device.value, "Instance");
    if (instanceField) api.field_static_get_value(instanceField.value, &instance);

    // Blit 会改材质的 _MainTex：用游戏材质的副本，别把它的精灵弄坏。
    const il2cpp::MethodInfo* getMaterial = api.class_get_method_from_name(device.value, "GetMaterial", 1);
    int32_t offset = 0;
    void* materialArgs[1] = {&offset};
    exception = nullptr;
    il2cpp::Il2CppObject* gameMaterial =
        instance != nullptr && getMaterial != nullptr ? api.runtime_invoke(getMaterial, instance, materialArgs, &exception)
                                                      : nullptr;
    void* copyArgs[1] = {gameMaterial};
    il2cpp::Il2CppObject* copy =
        gameMaterial != nullptr && exception == nullptr ? construct(material.value, {"UnityEngine.Material"}, copyArgs)
                                                       : nullptr;

    const il2cpp::MethodInfo* blit = api.class_get_method_from_name(graphics.value, "Internal_BlitMaterial5", 5);
    if (target == nullptr || source == nullptr || copy == nullptr || blit == nullptr) {
        LOGE("fx", "标记没搭成（target %p / source %p / material %p / blit %p）：世界层关闭", target, source, copy,
             blit);
        return false;
    }

    g_marker.handles[0] = api.gchandle_new(target, false);
    g_marker.handles[1] = api.gchandle_new(source, false);
    g_marker.handles[2] = api.gchandle_new(copy, false);
    g_marker.source = source;
    g_marker.target = target;
    g_marker.material = copy;
    g_marker.blit = blit;
    g_marker.ready = true;
    LOGI("fx", "世界层标记已就绪（%dx%d 的 Blit）", kWidth, kHeight);
    return true;
}

bool MarkerReady() {
    return g_marker.ready;
}

void IssueMarker() {
    if (!g_marker.ready) return;
    using BlitFn = void (*)(il2cpp::Il2CppObject*, il2cpp::Il2CppObject*, il2cpp::Il2CppObject*, int32_t, bool,
                            const il2cpp::MethodInfo*);
    const auto blit = reinterpret_cast<BlitFn>(methodPointer(g_marker.blit));
    // setRT = true：false 时 Unity 不设目标，会画到游戏当前的目标上。
    auto call = [blit] { blit(g_marker.source, g_marker.target, g_marker.material, 0, true, g_marker.blit); };
    if (il2cpp::guarded(call)) return;
    g_marker.ready = false;
    LOGE("fx", "标记的 Blit 抛了：世界层关闭");
}

}  // namespace pylon::graphics
