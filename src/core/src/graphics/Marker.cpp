// 层：内核层 / graphics —— 标记实现
#include "graphics/Marker.h"

#include <cstring>
#include <initializer_list>

#include "il2cpp/Domain.h"
#include "il2cpp/Guard.h"
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

std::string TypeName(const il2cpp::Symbols& symbols, const il2cpp::Il2CppType* type) {
    if (type == nullptr) return {};
    char* raw = symbols.type_get_name(type);
    if (raw == nullptr) return {};
    std::string name(raw);
    if (symbols.free != nullptr) symbols.free(raw);
    return name;
}

const il2cpp::MethodInfo* findCtor(const il2cpp::Symbols& symbols, il2cpp::Il2CppClass* klass,
                                   std::initializer_list<const char*> types) {
    void* iter = nullptr;
    while (const il2cpp::MethodInfo* method = symbols.class_get_methods(klass, &iter)) {
        if (std::strcmp(symbols.method_get_name(method), ".ctor") != 0) continue;
        if (symbols.method_get_param_count(method) != types.size()) continue;

        bool same = true;
        uint32_t index = 0;
        for (const char* type : types) {
            same = same && TypeName(symbols, symbols.method_get_param(method, index++)) == type;
        }
        if (same) return method;
    }
    return nullptr;
}

il2cpp::Il2CppObject* construct(const il2cpp::Symbols& symbols, il2cpp::Il2CppClass* klass,
                                std::initializer_list<const char*> types, void** args) {
    const il2cpp::MethodInfo* ctor = klass != nullptr ? findCtor(symbols, klass, types) : nullptr;
    if (ctor == nullptr) return nullptr;

    il2cpp::Il2CppObject* object = symbols.object_new(klass);
    il2cpp::Il2CppObject* exception = nullptr;
    symbols.runtime_invoke(ctor, object, args, &exception);
    return exception != nullptr ? nullptr : object;
}

}  // namespace

bool CreateMarker() {
    auto attached = il2cpp::Domain::Attach();
    if (!attached) {
        LOGE("fx", "attach 失败，世界层标记不创建：%s", attached.reason.c_str());
        return false;
    }
    const il2cpp::Session& session = attached.value;
    const il2cpp::Symbols& symbols = session.raw();

    const auto renderTexture = session.FindClass("UnityEngine", "RenderTexture");
    const auto texture2D = session.FindClass("UnityEngine", "Texture2D");
    const auto material = session.FindClass("UnityEngine", "Material");
    const auto graphics = session.FindClass("UnityEngine", "Graphics");
    const auto device = session.FindClass("Microsoft.Xna.Framework.Graphics", "GraphicsDevice");
    if (!renderTexture || !texture2D || !material || !graphics || !device) {
        LOGE("fx", "标记依赖的类没找齐：世界层关闭");
        return false;
    }

    int width = kWidth;
    int height = kHeight;
    int depth = 0;
    void* targetArgs[3] = {&width, &height, &depth};
    il2cpp::Il2CppObject* target =
            construct(symbols, renderTexture.value, {"System.Int32", "System.Int32", "System.Int32"}, targetArgs);

    il2cpp::Il2CppObject* exception = nullptr;
    const il2cpp::MethodInfo* white = symbols.class_get_method_from_name(texture2D.value, "get_whiteTexture", 0);
    il2cpp::Il2CppObject* source =
            white != nullptr ? symbols.runtime_invoke(white, nullptr, nullptr, &exception) : nullptr;

    il2cpp::Il2CppObject* instance = nullptr;
    if (auto instanceField = il2cpp::Field::Resolve(session, device.value, "Instance"); instanceField) {
        instanceField.value.Read(nullptr, &instance);
    }

    // Blit 改 _MainTex：用副本，别弄坏游戏材质的精灵。
    const il2cpp::MethodInfo* getMaterial = symbols.class_get_method_from_name(device.value, "GetMaterial", 1);
    int32_t offset = 0;
    void* materialArgs[1] = {&offset};
    exception = nullptr;
    il2cpp::Il2CppObject* gameMaterial = instance != nullptr && getMaterial != nullptr
                                                 ? symbols.runtime_invoke(getMaterial, instance, materialArgs, &exception)
                                                 : nullptr;
    void* copyArgs[1] = {gameMaterial};
    il2cpp::Il2CppObject* copy = gameMaterial != nullptr && exception == nullptr
                                         ? construct(symbols, material.value, {"UnityEngine.Material"}, copyArgs)
                                         : nullptr;

    const il2cpp::MethodInfo* blit =
            symbols.class_get_method_from_name(graphics.value, "Internal_BlitMaterial5", 5);
    if (target == nullptr || source == nullptr || copy == nullptr || blit == nullptr) {
        LOGE("fx", "标记没搭成（target %p / source %p / material %p / blit %p）：世界层关闭", target, source, copy,
             blit);
        return false;
    }

    g_marker.handles[0] = symbols.gchandle_new(target, false);
    g_marker.handles[1] = symbols.gchandle_new(source, false);
    g_marker.handles[2] = symbols.gchandle_new(copy, false);
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
    // setRT=false 时不设目标，会画到游戏当前的目标上。
    auto call = [blit] { blit(g_marker.source, g_marker.target, g_marker.material, 0, true, g_marker.blit); };
    if (il2cpp::Guard::Run(call)) return;

    g_marker.ready = false;
    LOGE("fx", "标记的 Blit 抛了：世界层关闭");
}

}  // namespace pylon::graphics
