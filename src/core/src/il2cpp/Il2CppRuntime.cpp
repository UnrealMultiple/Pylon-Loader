// 层：内核层 / il2cpp —— 实现
#include "il2cpp/Il2CppRuntime.h"

#include <cstdio>
#include <cstring>
#include <unistd.h>

#include "pylon/Log.h"

namespace pylon::il2cpp {

Runtime& Runtime::Instance() {
    static Runtime instance;
    return instance;
}

Status Runtime::Bind() {
    std::lock_guard<std::mutex> lock(mutex_);
    if (api_.Complete()) return Status::Ok();
    if (!api_.Bind()) return Status::Fail(api_.missing);
    return Status::Ok();
}

thread_local bool Runtime::thread_attached_ = false;

void Runtime::NoteGameThread() {
    const unsigned long tid = static_cast<unsigned long>(gettid());
    const unsigned long previous = game_thread_id_.exchange(tid);
    if (previous == tid) return;

    // 打印线程名（UnityMain）——出问题时日志里能直接看出是不是登记错了线程。
    char name[32] = {0};
    char path[64];
    std::snprintf(path, sizeof(path), "/proc/self/task/%lu/comm", tid);
    if (FILE* file = std::fopen(path, "r")) {
        if (std::fgets(name, sizeof(name), file) != nullptr) {
            for (char* p = name; *p != '\0'; ++p) {
                if (*p == '\n') *p = '\0';
            }
        }
        std::fclose(file);
    }
    LOGI("core", "已登记游戏线程：tid=%lu 名字=\"%s\"（托管方法只允许在这个线程上调）",
         tid, name[0] != '\0' ? name : "?");
}

bool Runtime::IsGameThread() const {
    const unsigned long registered = game_thread_id_.load();
    return registered != 0 && registered == static_cast<unsigned long>(gettid());
}

unsigned long Runtime::GameThreadId() const { return game_thread_id_.load(); }

Status Runtime::Attach() {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!api_.Complete()) {
        return Status::Fail("il2cpp API 未绑定完成，拒绝调用（先 nativeStart）");
    }

    // 到这一步才允许第一次碰 IL2CPP：Java 侧已经确认引擎稳定（il2cpp_init 早已返回）。
    if (domain_ == nullptr) {
        domain_ = api_.domain_get();
        if (domain_ == nullptr) {
            return Status::Fail("il2cpp_domain_get() 返回 null（引擎尚未完成初始化？）");
        }
        LOGI("core", "domain=%p", static_cast<void*>(domain_));
    }

    // domain 只取一次，但**每个线程**都要 attach（docs/06 结论①）；Mod 从自己线程调进来也走这里。
    if (!thread_attached_) {
        // 对**任何**未 attach 的线程都要 attach：曾试过"游戏线程跳过"，探测线程被误判漏 attach → 启动即崩（见 docs/15 §18）。
        if (api_.thread_attach(domain_) == nullptr) {
            return Status::Fail("il2cpp_thread_attach() 返回 null");
        }
        thread_attached_ = true;
        LOGI("core", "已 attach 当前线程（线程 %lu）", static_cast<unsigned long>(gettid()));
    }

    // 只补**没命中**的那个镜像：早先"两个都没命中才扫"，命中 Assembly-CSharp.dll 后 mscorlib.dll 永不被缓存，解析退化成扫全量。
    if (game_image_ == nullptr || corlib_image_ == nullptr) {
        CachePreferredImages();
    }
    return Status::Ok();
}

void Runtime::CachePreferredImages() {
    size_t count = 0;
    const Il2CppAssembly** assemblies = api_.domain_get_assemblies(domain_, &count);
    if (assemblies == nullptr) return;
    for (size_t i = 0; i < count; ++i) {
        const Il2CppImage* image = api_.assembly_get_image(assemblies[i]);
        if (image == nullptr) continue;
        const char* name = api_.image_get_name(image);
        if (name == nullptr) continue;
        if (std::strcmp(name, "Assembly-CSharp.dll") == 0) game_image_ = image;
        else if (std::strcmp(name, "mscorlib.dll") == 0) corlib_image_ = image;
    }
    LOGI("core", "常用镜像：Assembly-CSharp.dll=%s mscorlib.dll=%s",
         game_image_ != nullptr ? "命中" : "未命中",
         corlib_image_ != nullptr ? "命中" : "未命中");
}

Outcome<Il2CppClass*> Runtime::FindClass(const std::string& namespaze, const std::string& name) {
    if (domain_ == nullptr || !thread_attached_) return Outcome<Il2CppClass*>::Bad("当前线程未 attach，拒绝解析（先 Runtime::Attach()）");
    if (name.empty()) return Outcome<Il2CppClass*>::Bad("类名为空");

    const std::string key = namespaze + "." + name;
    {
        std::lock_guard<std::mutex> lock(cache_mutex_);
        auto cached = class_cache_.find(key);
        if (cached != class_cache_.end()) return Outcome<Il2CppClass*>::Good(cached->second);
    }

    // 先查两个最常用的镜像（绝大多数解析落在这里，省一次全量遍历）。
    const Il2CppImage* preferred[2] = {game_image_, corlib_image_};
    for (const Il2CppImage* image : preferred) {
        if (image == nullptr) continue;
        Il2CppClass* klass = api_.class_from_name(image, namespaze.c_str(), name.c_str());
        if (klass != nullptr) {
            {
                std::lock_guard<std::mutex> lock(cache_mutex_);
                class_cache_[key] = klass;
            }
            return Outcome<Il2CppClass*>::Good(klass);
        }
    }

    // 退路：扫全部已加载程序集。必须保留——Texture2D 在 CoreModule、ImageConversion 在自己
    // 的模块，只看 game+corlib 会整片 UnityEngine 解析不到。
    size_t count = 0;
    const Il2CppAssembly** assemblies = api_.domain_get_assemblies(domain_, &count);
    if (assemblies == nullptr || count == 0) {
        return Outcome<Il2CppClass*>::Bad("domain 里没有已加载程序集");
    }
    for (size_t i = 0; i < count; ++i) {
        const Il2CppImage* image = api_.assembly_get_image(assemblies[i]);
        if (image == nullptr || image == game_image_ || image == corlib_image_) continue;
        Il2CppClass* klass = api_.class_from_name(image, namespaze.c_str(), name.c_str());
        if (klass != nullptr) {
            {
                std::lock_guard<std::mutex> lock(cache_mutex_);
                class_cache_[key] = klass;
            }
            return Outcome<Il2CppClass*>::Good(klass);
        }
    }
    return Outcome<Il2CppClass*>::Bad("在 " + std::to_string(count) + " 个程序集里都找不到类 " + key);
}

Outcome<FieldInfo*> Runtime::FindField(Il2CppClass* klass, const std::string& name) {
    if (domain_ == nullptr || !thread_attached_) return Outcome<FieldInfo*>::Bad("当前线程未 attach，拒绝解析（先 Runtime::Attach()）");
    for (Il2CppClass* current = klass; current != nullptr; current = api_.class_get_parent(current)) {
        FieldInfo* field = api_.class_get_field_from_name(current, name.c_str());
        if (field != nullptr) return Outcome<FieldInfo*>::Good(field);
    }
    return Outcome<FieldInfo*>::Bad("字段 " + name + " 在本类与父类链上都不存在");
}

Outcome<const MethodInfo*> Runtime::FindMethod(Il2CppClass* klass, const std::string& name, int argc) {
    if (domain_ == nullptr || !thread_attached_) return Outcome<const MethodInfo*>::Bad("当前线程未 attach，拒绝解析（先 Runtime::Attach()）");
    for (Il2CppClass* current = klass; current != nullptr; current = api_.class_get_parent(current)) {
        const MethodInfo* method = api_.class_get_method_from_name(current, name.c_str(), argc);
        if (method != nullptr) return Outcome<const MethodInfo*>::Good(method);
    }
    return Outcome<const MethodInfo*>::Bad(
            "方法 " + name + "/" + std::to_string(argc) + " 在本类与父类链上都不存在");
}

Outcome<const MethodInfo*> Runtime::FindMethodAnyArity(Il2CppClass* klass, const std::string& name,
                                                      int* argc) {
    if (domain_ == nullptr || !thread_attached_) return Outcome<const MethodInfo*>::Bad("当前线程未 attach，拒绝解析（先 Runtime::Attach()）");
    for (int candidate = 0; candidate <= kMaxProbeArity; ++candidate) {
        // 不能直接复用 FindMethod：那条路会为每个 argc 生成一条错误串，日志会被刷爆。
        for (Il2CppClass* current = klass; current != nullptr; current = api_.class_get_parent(current)) {
            const MethodInfo* method = api_.class_get_method_from_name(current, name.c_str(), candidate);
            if (method != nullptr) {
                if (argc != nullptr) *argc = candidate;
                return Outcome<const MethodInfo*>::Good(method);
            }
        }
    }
    return Outcome<const MethodInfo*>::Bad(
            "方法 " + name + " 在 0.." + std::to_string(kMaxProbeArity) +
            " 个参数下都找不到（本类与父类链）");
}

bool Runtime::FieldIsStatic(FieldInfo* field) const {
    // FIELD_ATTRIBUTE_STATIC = 0x0010（ECMA-335 的 FieldAttributes）
    return (api_.field_get_flags(field) & 0x0010u) != 0;
}

std::string Runtime::ClassName(Il2CppClass* klass) const {
    if (klass == nullptr) return "(null)";
    const char* ns = api_.class_get_namespace(klass);
    const char* name = api_.class_get_name(klass);
    std::string full;
    if (ns != nullptr && *ns != '\0') {
        full = std::string(ns) + ".";
    }
    full += (name != nullptr ? name : "(null)");
    return full;
}

std::vector<std::string> Runtime::Images() {
    std::vector<std::string> names;
    if (domain_ == nullptr) return names;
    size_t count = 0;
    const Il2CppAssembly** assemblies = api_.domain_get_assemblies(domain_, &count);
    if (assemblies == nullptr) return names;
    for (size_t i = 0; i < count; ++i) {
        const Il2CppImage* image = api_.assembly_get_image(assemblies[i]);
        if (image == nullptr) continue;
        const char* name = api_.image_get_name(image);
        const size_t classes = api_.image_get_class_count(image);
        char buffer[256];
        std::snprintf(buffer, sizeof(buffer), "%s（%zu 类）", name != nullptr ? name : "(null)", classes);
        names.emplace_back(buffer);
    }
    return names;
}

}  // namespace pylon::il2cpp
