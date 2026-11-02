// 层：内核层 / il2cpp —— 会话查询
#include "il2cpp/Domain.h"

#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

namespace pylon::il2cpp {

namespace {

std::mutex g_cacheMutex;
std::unordered_map<std::string, Il2CppClass*> g_cache;

}  // namespace

Outcome<Il2CppClass*> Session::FindClass(std::string_view namespaze, std::string_view name) const {
    if (name.empty()) return Outcome<Il2CppClass*>::Bad("类名为空");

    std::string key(namespaze);
    key += '.';
    key += name;

    {
        std::lock_guard<std::mutex> lock(g_cacheMutex);
        if (const auto cached = g_cache.find(key); cached != g_cache.end()) {
            return Outcome<Il2CppClass*>::Good(cached->second);
        }
    }

    size_t count = 0;
    const Il2CppAssembly** assemblies = symbols_->domain_get_assemblies(domain_, &count);
    if (assemblies == nullptr || count == 0) return Outcome<Il2CppClass*>::Bad("domain 里没有已加载程序集");

    const std::string namespazeText(namespaze);
    const std::string nameText(name);
    for (size_t i = 0; i < count; ++i) {
        const Il2CppImage* image = symbols_->assembly_get_image(assemblies[i]);
        if (image == nullptr) continue;
        Il2CppClass* klass = symbols_->class_from_name(image, namespazeText.c_str(), nameText.c_str());
        if (klass == nullptr) continue;

        std::lock_guard<std::mutex> lock(g_cacheMutex);
        g_cache[key] = klass;
        return Outcome<Il2CppClass*>::Good(klass);
    }
    return Outcome<Il2CppClass*>::Bad("在 " + std::to_string(count) + " 个程序集里都找不到类 " + key);
}

std::string Session::ClassName(Il2CppClass* klass) const {
    if (klass == nullptr) return "(null)";
    const char* namespaze = symbols_->class_get_namespace(klass);
    const char* name = symbols_->class_get_name(klass);
    std::string full(namespaze != nullptr && *namespaze != '\0' ? std::string(namespaze) + "." : std::string());
    full += name != nullptr ? name : "(null)";
    return full;
}

std::vector<std::string> Session::Images() const {
    std::vector<std::string> names;
    size_t count = 0;
    const Il2CppAssembly** assemblies = symbols_->domain_get_assemblies(domain_, &count);
    if (assemblies == nullptr) return names;

    names.reserve(count);
    for (size_t i = 0; i < count; ++i) {
        const Il2CppImage* image = symbols_->assembly_get_image(assemblies[i]);
        if (image == nullptr) continue;
        const char* name = symbols_->image_get_name(image);
        const size_t classes = symbols_->image_get_class_count(image);
        char buffer[256];
        std::snprintf(buffer, sizeof(buffer), "%s（%zu 类）", name != nullptr ? name : "(null)", classes);
        names.emplace_back(buffer);
    }
    return names;
}

}  // namespace pylon::il2cpp
