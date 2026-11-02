// 层：内核层 / il2cpp —— il2cpp_* 符号表：声明、绑定、计数都由这一张表生成
#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace pylon::il2cpp {

struct Il2CppDomain;
struct Il2CppAssembly;
struct Il2CppImage;
struct Il2CppClass;
struct Il2CppThread;
struct Il2CppType;
struct Il2CppObject;
struct Il2CppArray;
struct Il2CppString;
struct FieldInfo;
struct MethodInfo;

enum class Need { Required, Optional };

// clang-format off
#define PYLON_IL2CPP_SYMBOLS(X) \
    X(Required, domain_get, Il2CppDomain*, ()) \
    X(Required, thread_attach, Il2CppThread*, (Il2CppDomain*)) \
    X(Required, domain_get_assemblies, const Il2CppAssembly**, (const Il2CppDomain*, size_t*)) \
    X(Required, assembly_get_image, const Il2CppImage*, (const Il2CppAssembly*)) \
    X(Required, image_get_name, const char*, (const Il2CppImage*)) \
    X(Required, image_get_class_count, size_t, (const Il2CppImage*)) \
    X(Required, image_get_class, Il2CppClass*, (const Il2CppImage*, size_t)) \
    X(Required, class_from_name, Il2CppClass*, (const Il2CppImage*, const char*, const char*)) \
    X(Required, class_get_name, const char*, (Il2CppClass*)) \
    X(Required, class_get_namespace, const char*, (Il2CppClass*)) \
    X(Required, class_get_parent, Il2CppClass*, (Il2CppClass*)) \
    X(Required, class_get_field_from_name, FieldInfo*, (Il2CppClass*, const char*)) \
    X(Required, field_get_offset, size_t, (FieldInfo*)) \
    X(Required, field_get_flags, uint32_t, (FieldInfo*)) \
    X(Required, class_get_method_from_name, const MethodInfo*, (Il2CppClass*, const char*, int)) \
    X(Required, method_get_param_count, uint32_t, (const MethodInfo*)) \
    X(Required, method_get_name, const char*, (const MethodInfo*)) \
    X(Optional, init, int, (const char*)) \
    X(Optional, field_get_value, void, (Il2CppObject*, FieldInfo*, void*)) \
    X(Optional, field_set_value, void, (Il2CppObject*, FieldInfo*, void*)) \
    X(Optional, field_get_type, const Il2CppType*, (FieldInfo*)) \
    X(Optional, class_from_il2cpp_type, Il2CppClass*, (const Il2CppType*)) \
    X(Optional, class_is_enum, bool, (const Il2CppClass*)) \
    X(Optional, class_is_valuetype, bool, (const Il2CppClass*)) \
    X(Optional, class_is_assignable_from, bool, (Il2CppClass*, Il2CppClass*)) \
    X(Optional, class_enum_basetype, const Il2CppType*, (Il2CppClass*)) \
    X(Optional, class_get_fields, FieldInfo*, (Il2CppClass*, void**)) \
    X(Optional, field_get_name, const char*, (FieldInfo*)) \
    X(Optional, field_get_parent, Il2CppClass*, (FieldInfo*)) \
    X(Optional, method_get_class, Il2CppClass*, (const MethodInfo*)) \
    X(Optional, class_get_element_class, Il2CppClass*, (Il2CppClass*)) \
    X(Optional, class_get_rank, int, (const Il2CppClass*)) \
    X(Optional, value_box, Il2CppObject*, (Il2CppClass*, void*)) \
    X(Optional, class_value_size, int32_t, (Il2CppClass*, uint32_t*)) \
    X(Optional, class_get_type, const Il2CppType*, (Il2CppClass*)) \
    X(Optional, class_get_nested_types, Il2CppClass*, (Il2CppClass*, void**)) \
    X(Optional, field_static_get_value, void, (FieldInfo*, void*)) \
    X(Optional, field_static_set_value, void, (FieldInfo*, void*)) \
    X(Optional, runtime_invoke, Il2CppObject*, (const MethodInfo*, void*, void**, Il2CppObject**)) \
    X(Optional, method_get_flags, uint32_t, (const MethodInfo*, uint32_t*)) \
    X(Optional, method_is_instance, bool, (const MethodInfo*)) \
    X(Optional, class_get_methods, const MethodInfo*, (Il2CppClass*, void**)) \
    X(Optional, method_get_param, const Il2CppType*, (const MethodInfo*, uint32_t)) \
    X(Optional, method_get_param_name, const char*, (const MethodInfo*, uint32_t)) \
    X(Optional, method_get_return_type, const Il2CppType*, (const MethodInfo*)) \
    X(Optional, type_get_name, char*, (const Il2CppType*)) \
    X(Optional, type_get_attrs, uint32_t, (const Il2CppType*)) \
    X(Optional, free, void, (void*)) \
    X(Optional, object_new, Il2CppObject*, (Il2CppClass*)) \
    X(Optional, object_get_class, Il2CppClass*, (Il2CppObject*)) \
    X(Optional, object_unbox, void*, (Il2CppObject*)) \
    X(Optional, array_new, Il2CppArray*, (Il2CppClass*, uintptr_t)) \
    X(Optional, array_new_full, Il2CppArray*, (Il2CppClass*, uintptr_t*, uintptr_t*)) \
    X(Optional, string_new, Il2CppString*, (const char*)) \
    X(Optional, gchandle_new, uint32_t, (Il2CppObject*, bool)) \
    X(Optional, gchandle_new_weakref, uint32_t, (Il2CppObject*, bool)) \
    X(Optional, gchandle_free, void, (uint32_t)) \
    X(Optional, gchandle_get_target, Il2CppObject*, (uint32_t)) \
    X(Optional, gc_alloc_fixed, void*, (size_t)) \
    X(Optional, gc_free_fixed, void, (void*)) \
    X(Optional, gc_wbarrier_set_field, void, (Il2CppObject*, void**, void*)) \
    X(Optional, gc_is_incremental, bool, ()) \
    X(Optional, runtime_class_init, void, (Il2CppClass*)) \
    X(Optional, type_get_object, Il2CppObject*, (const Il2CppType*)) \
    X(Optional, class_from_system_type, Il2CppClass*, (Il2CppObject*)) \
    X(Optional, class_get_declaring_type, Il2CppClass*, (Il2CppClass*)) \
    X(Optional, format_exception, void, (Il2CppObject*, char*, int)) \
    X(Optional, monitor_enter, void, (Il2CppObject*)) \
    X(Optional, monitor_exit, void, (Il2CppObject*))
// clang-format on

#define PYLON_SYMBOL_SLOT(need, name, type, params) type (*name) params = nullptr;
#define PYLON_SYMBOL_COUNT(need, name, type, params) + (Need::need == Need::Required ? 1 : 0)
#define PYLON_SYMBOL_ALWAYS(need, name, type, params) + 1

struct BindReport {
    int required = 0;
    int optional = 0;
    std::vector<std::string> missingRequired;
    std::vector<std::string> missingOptional;

    bool ok() const { return missingRequired.empty(); }
};

struct Symbols {
    PYLON_IL2CPP_SYMBOLS(PYLON_SYMBOL_SLOT)

    static constexpr int kRequiredCount = 0 PYLON_IL2CPP_SYMBOLS(PYLON_SYMBOL_COUNT);
    static constexpr int kTotalCount = 0 PYLON_IL2CPP_SYMBOLS(PYLON_SYMBOL_ALWAYS);
    static constexpr int kOptionalCount = kTotalCount - kRequiredCount;

    /// RTLD_NOLOAD：只查已加载的库，不替 Unity 决定加载时机；绑定本身不调用任何 API。
    bool Load(BindReport* report);
};

#undef PYLON_SYMBOL_SLOT
#undef PYLON_SYMBOL_COUNT
#undef PYLON_SYMBOL_ALWAYS

}  // namespace pylon::il2cpp
