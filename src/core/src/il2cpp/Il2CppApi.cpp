// 层：内核层 / il2cpp —— 符号绑定
#include "il2cpp/Il2CppApi.h"

#include <dlfcn.h>

#include <string>
#include <vector>

#include "pylon/Log.h"

namespace pylon::il2cpp {
namespace {

/// 一次 dlsym：成功写进成员，失败记下符号名。用宏是因为每个符号指针类型都不同；
/// 展开在 `Api::Bind` 内部，直接写成员名即可（不需要 this->，也不假设有 api 局部变量）。
#define PYLON_BIND(member, symbol)                                             \
    do {                                                                       \
        void* addr = dlsym(handle, symbol);                                    \
        if (addr == nullptr) {                                                 \
            missing_symbols.push_back(symbol);                                 \
        } else {                                                              \
            member = reinterpret_cast<decltype(member)>(addr);                 \
            ++required;                                                       \
        }                                                                     \
    } while (0)

/// 可选符号：缺失不记进 missing_symbols（不会让绑定失败），只计数并**记名**
/// ——"缺了哪些"有用（如某 API 在该 Unity 版本没有），但绝不让加载器整体不工作。
#define PYLON_BIND_OPTIONAL(member, symbol)                                    \
    do {                                                                       \
        void* addr = dlsym(handle, symbol);                                    \
        if (addr != nullptr) {                                                 \
            member = reinterpret_cast<decltype(member)>(addr);                 \
            ++optional;                                                       \
        } else {                                                              \
            missing_optional.push_back(symbol);                                \
        }                                                                     \
    } while (0)

}  // namespace

bool Api::Bind(const char* so_name) {
    // RTLD_NOLOAD：只取**已加载**的句柄，不替 Unity 决定何时加载 libil2cpp.so；此刻未加载
    // （正常时序不会：宿主已按序 loadLibrary）就明确失败——猜时机加载会打乱 JNI_OnLoad 与初始化顺序。
    handle = dlopen(so_name, RTLD_NOLOAD | RTLD_NOW);
    if (handle == nullptr) {
        const char* err = dlerror();
        missing = std::string("libil2cpp.so 尚未加载（dlopen RTLD_NOLOAD 失败：") +
                  (err != nullptr ? err : "未知") + "）";
        required_bound = 0;
        return false;
    }

    std::vector<std::string> missing_symbols;
    std::vector<std::string> missing_optional;
    int required = 0;
    int optional = 0;

    // ---- 必需：M2 的解析链路（域/镜像/类/字段/方法）----
    // il2cpp_init 是可选符号：绑不到就说明这个构建里没法"等 init 返回"，探测会拒绝启动而不是猜时间。
    PYLON_BIND_OPTIONAL(init, "il2cpp_init");
    PYLON_BIND(domain_get, "il2cpp_domain_get");
    PYLON_BIND(thread_attach, "il2cpp_thread_attach");
    PYLON_BIND(domain_get_assemblies, "il2cpp_domain_get_assemblies");
    PYLON_BIND(assembly_get_image, "il2cpp_assembly_get_image");
    PYLON_BIND(image_get_name, "il2cpp_image_get_name");
    PYLON_BIND(image_get_class_count, "il2cpp_image_get_class_count");
    PYLON_BIND(image_get_class, "il2cpp_image_get_class");
    PYLON_BIND(class_from_name, "il2cpp_class_from_name");
    PYLON_BIND(class_get_name, "il2cpp_class_get_name");
    PYLON_BIND(class_get_namespace, "il2cpp_class_get_namespace");
    PYLON_BIND(class_get_parent, "il2cpp_class_get_parent");
    PYLON_BIND(class_get_field_from_name, "il2cpp_class_get_field_from_name");
    PYLON_BIND(field_get_offset, "il2cpp_field_get_offset");
    PYLON_BIND(field_get_flags, "il2cpp_field_get_flags");
    PYLON_BIND(class_get_method_from_name, "il2cpp_class_get_method_from_name");
    PYLON_BIND(method_get_param_count, "il2cpp_method_get_param_count");
    PYLON_BIND(method_get_name, "il2cpp_method_get_name");

    // ---- 可选：M3（游戏原生 UI）/M4（Mod 内容与存档）会用到 ----
    PYLON_BIND_OPTIONAL(field_get_value, "il2cpp_field_get_value");
    PYLON_BIND_OPTIONAL(field_set_value, "il2cpp_field_set_value");
    PYLON_BIND_OPTIONAL(field_get_type, "il2cpp_field_get_type");
    PYLON_BIND_OPTIONAL(class_from_il2cpp_type, "il2cpp_class_from_il2cpp_type");
    PYLON_BIND_OPTIONAL(class_is_enum, "il2cpp_class_is_enum");
    PYLON_BIND_OPTIONAL(class_is_valuetype, "il2cpp_class_is_valuetype");
    PYLON_BIND_OPTIONAL(class_is_assignable_from, "il2cpp_class_is_assignable_from");
    PYLON_BIND_OPTIONAL(class_enum_basetype, "il2cpp_class_enum_basetype");
    PYLON_BIND_OPTIONAL(class_get_fields, "il2cpp_class_get_fields");
    PYLON_BIND_OPTIONAL(field_get_name, "il2cpp_field_get_name");
    PYLON_BIND_OPTIONAL(field_get_parent, "il2cpp_field_get_parent");
    PYLON_BIND_OPTIONAL(method_get_class, "il2cpp_method_get_class");
    PYLON_BIND_OPTIONAL(class_get_element_class, "il2cpp_class_get_element_class");
    PYLON_BIND_OPTIONAL(class_get_rank, "il2cpp_class_get_rank");
    PYLON_BIND_OPTIONAL(value_box, "il2cpp_value_box");
    PYLON_BIND_OPTIONAL(class_value_size, "il2cpp_class_value_size");
    PYLON_BIND_OPTIONAL(class_get_type, "il2cpp_class_get_type");
    PYLON_BIND_OPTIONAL(class_get_nested_types, "il2cpp_class_get_nested_types");
    PYLON_BIND_OPTIONAL(field_static_get_value, "il2cpp_field_static_get_value");
    PYLON_BIND_OPTIONAL(field_static_set_value, "il2cpp_field_static_set_value");
    PYLON_BIND_OPTIONAL(runtime_invoke, "il2cpp_runtime_invoke");
    PYLON_BIND_OPTIONAL(method_get_flags, "il2cpp_method_get_flags");
    PYLON_BIND_OPTIONAL(method_is_instance, "il2cpp_method_is_instance");
    PYLON_BIND_OPTIONAL(class_get_methods, "il2cpp_class_get_methods");
    PYLON_BIND_OPTIONAL(method_get_param, "il2cpp_method_get_param");
    PYLON_BIND_OPTIONAL(method_get_param_name, "il2cpp_method_get_param_name");
    PYLON_BIND_OPTIONAL(method_get_return_type, "il2cpp_method_get_return_type");
    PYLON_BIND_OPTIONAL(type_get_name, "il2cpp_type_get_name");
    PYLON_BIND_OPTIONAL(type_get_attrs, "il2cpp_type_get_attrs");
    PYLON_BIND_OPTIONAL(il2cpp_free, "il2cpp_free");
    PYLON_BIND_OPTIONAL(object_new, "il2cpp_object_new");
    PYLON_BIND_OPTIONAL(object_get_class, "il2cpp_object_get_class");
    PYLON_BIND_OPTIONAL(object_unbox, "il2cpp_object_unbox");
    PYLON_BIND_OPTIONAL(array_new, "il2cpp_array_new");
    PYLON_BIND_OPTIONAL(array_new_full, "il2cpp_array_new_full");
    PYLON_BIND_OPTIONAL(string_new, "il2cpp_string_new");
    PYLON_BIND_OPTIONAL(gchandle_new, "il2cpp_gchandle_new");
    PYLON_BIND_OPTIONAL(gchandle_new_weakref, "il2cpp_gchandle_new_weakref");
    PYLON_BIND_OPTIONAL(gchandle_free, "il2cpp_gchandle_free");
    PYLON_BIND_OPTIONAL(gchandle_get_target, "il2cpp_gchandle_get_target");
    PYLON_BIND_OPTIONAL(gc_alloc_fixed, "il2cpp_gc_alloc_fixed");
    PYLON_BIND_OPTIONAL(gc_free_fixed, "il2cpp_gc_free_fixed");
    PYLON_BIND_OPTIONAL(gc_wbarrier_set_field, "il2cpp_gc_wbarrier_set_field");
    PYLON_BIND_OPTIONAL(gc_is_incremental, "il2cpp_gc_is_incremental");
    PYLON_BIND_OPTIONAL(runtime_class_init, "il2cpp_runtime_class_init");
    PYLON_BIND_OPTIONAL(type_get_object, "il2cpp_type_get_object");
    PYLON_BIND_OPTIONAL(class_from_system_type, "il2cpp_class_from_system_type");
    PYLON_BIND_OPTIONAL(class_get_declaring_type, "il2cpp_class_get_declaring_type");
    PYLON_BIND_OPTIONAL(format_exception, "il2cpp_format_exception");
    PYLON_BIND_OPTIONAL(monitor_enter, "il2cpp_monitor_enter");
    PYLON_BIND_OPTIONAL(monitor_exit, "il2cpp_monitor_exit");

#undef PYLON_BIND
#undef PYLON_BIND_OPTIONAL

    required_bound = required;
    optional_bound = optional;

    if (!missing_optional.empty()) {
        std::string optional_list;
        for (size_t i = 0; i < missing_optional.size(); ++i) {
            if (i != 0) optional_list += ", ";
            optional_list += missing_optional[i];
        }
        // 只是信息：这些是 M3/M4 才用的附件 API，缺了不影响 M2，也绝不让内核失败。
        LOGW("core", "可选符号缺失（不影响 M2，用到时判空）：%s", optional_list.c_str());
    }

    if (missing_symbols.empty() && required == kRequiredSymbolCount) {
        LOGI("core", "il2cpp API 绑定：必需 %d/%d，可选 %d/%d（dlopen RTLD_NOLOAD，未调用任何 API）",
             required, kRequiredSymbolCount, optional, kOptionalSymbolCount);
        missing.clear();
        return true;
    }

    std::string joined;
    for (size_t i = 0; i < missing_symbols.size(); ++i) {
        if (i != 0) joined += ", ";
        joined += missing_symbols[i];
    }
    missing = "缺少必需导出符号：" + joined;
    LOGW("core", "il2cpp API 绑定失败：必需 %d/%d，可选 %d/%d，%s",
         required, kRequiredSymbolCount, optional, kOptionalSymbolCount, missing.c_str());
    return false;
}

}  // namespace pylon::il2cpp
