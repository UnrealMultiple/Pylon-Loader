// 层：内核层 / il2cpp —— il2cpp_* 导出符号表
// 只做两件事：按名字 dlsym 绑定；绑定未成功时拒绝一切解析。不认具体游戏类型，
// 要解析哪些名字由上一层（boot/RefValidator）从清单传入。
// 不用官方头文件：Release 不含游戏/Unity 二进制，构建机也不依赖 Unity 安装目录；
// 这些签名是 IL2CPP 稳定导出 ABI（实测/上游验证的清单），结构体一律 opaque，指针只转交不 deref。
// 必需/可选分开（上游实测结论）：必需缺一个整体失败（宁可不启用 Mod）；可选（M3/M4 附件）
// 缺失只告警，否则某 Unity 版本少一个符号就让整个加载器不工作，代价与收益不成比例。
#pragma once

#include <cstddef>
#include <cstdint>
#include <string>

namespace pylon::il2cpp {

// IL2CPP 的对象都是不透明句柄：我们只把指针原样转交，绝不按自己的布局去读字段。
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

// --- 必需（M2：域/线程/镜像/类/字段/方法） ---
using Fn_DomainGet = Il2CppDomain* (*)();
using Fn_ThreadAttach = Il2CppThread* (*)(Il2CppDomain*);
using Fn_DomainGetAssemblies = const Il2CppAssembly** (*)(const Il2CppDomain*, size_t*);
using Fn_AssemblyGetImage = const Il2CppImage* (*)(const Il2CppAssembly*);
using Fn_ImageGetName = const char* (*)(const Il2CppImage*);
using Fn_ImageGetClassCount = size_t (*)(const Il2CppImage*);
using Fn_ImageGetClass = Il2CppClass* (*)(const Il2CppImage*, size_t);
using Fn_ClassFromName = Il2CppClass* (*)(const Il2CppImage*, const char*, const char*);
using Fn_ClassGetName = const char* (*)(Il2CppClass*);
using Fn_ClassGetNamespace = const char* (*)(Il2CppClass*);
using Fn_ClassGetParent = Il2CppClass* (*)(Il2CppClass*);
using Fn_ClassGetFieldFromName = FieldInfo* (*)(Il2CppClass*, const char*);
using Fn_FieldGetOffset = size_t (*)(FieldInfo*);
using Fn_FieldGetFlags = uint32_t (*)(FieldInfo*);using Fn_ClassGetMethodFromName = const MethodInfo* (*)(Il2CppClass*, const char*, int);
using Fn_MethodGetParamCount = uint32_t (*)(const MethodInfo*);
using Fn_MethodGetName = const char* (*)(const MethodInfo*);

// --- 可选（M3/M4：类型信息、枚举、静态字段、调用、对象/数组/字符串、GC 句柄） ---
using Fn_FieldGetValue = void (*)(Il2CppObject*, FieldInfo*, void*);
using Fn_FieldSetValue = void (*)(Il2CppObject*, FieldInfo*, void*);
using Fn_FieldGetType = const Il2CppType* (*)(FieldInfo*);using Fn_ClassFromIl2CppType = Il2CppClass* (*)(const Il2CppType*);
using Fn_ClassIsEnum = bool (*)(const Il2CppClass*);
using Fn_ClassIsValueType = bool (*)(const Il2CppClass*);
using Fn_ClassIsAssignableFrom = bool (*)(Il2CppClass*, Il2CppClass*);
using Fn_ClassEnumBaseType = const Il2CppType* (*)(Il2CppClass*);
using Fn_ClassGetFields = FieldInfo* (*)(Il2CppClass*, void**);
using Fn_FieldGetName = const char* (*)(FieldInfo*);
using Fn_FieldGetParent = Il2CppClass* (*)(FieldInfo*);
using Fn_MethodGetClass = Il2CppClass* (*)(const MethodInfo*);
using Fn_ClassGetElementClass = Il2CppClass* (*)(Il2CppClass*);
using Fn_ClassGetRank = int (*)(const Il2CppClass*);
using Fn_ValueBox = Il2CppObject* (*)(Il2CppClass*, void*);
using Fn_ClassValueSize = int32_t (*)(Il2CppClass*, uint32_t*);
using Fn_ClassGetType = const Il2CppType* (*)(Il2CppClass*);
using Fn_ClassGetNestedTypes = Il2CppClass* (*)(Il2CppClass*, void**);
using Fn_FieldStaticGetValue = void (*)(FieldInfo*, void*);
using Fn_FieldStaticSetValue = void (*)(FieldInfo*, void*);
using Fn_RuntimeInvoke = Il2CppObject* (*)(const MethodInfo*, void*, void**, Il2CppObject**);
using Fn_MethodGetFlags = uint32_t (*)(const MethodInfo*, uint32_t*);
using Fn_MethodIsInstance = bool (*)(const MethodInfo*);
using Fn_ClassGetMethods = const MethodInfo* (*)(Il2CppClass*, void**);
using Fn_MethodGetParam = const Il2CppType* (*)(const MethodInfo*, uint32_t);
using Fn_MethodGetParamName = const char* (*)(const MethodInfo*, uint32_t);
using Fn_MethodGetReturnType = const Il2CppType* (*)(const MethodInfo*);
using Fn_TypeGetName = char* (*)(const Il2CppType*);  // malloc 出来的，用 il2cpp_free 释放
using Fn_TypeGetAttrs = uint32_t (*)(const Il2CppType*);
using Fn_Free = void (*)(void*);
using Fn_ObjectNew = Il2CppObject* (*)(Il2CppClass*);
using Fn_ObjectGetClass = Il2CppClass* (*)(Il2CppObject*);
using Fn_ObjectUnbox = void* (*)(Il2CppObject*);
using Fn_ArrayNew = Il2CppArray* (*)(Il2CppClass*, uintptr_t);
using Fn_ArrayNewFull = Il2CppArray* (*)(Il2CppClass*, uintptr_t*, uintptr_t*);
using Fn_StringNew = Il2CppString* (*)(const char*);
using Fn_GcHandleNew = uint32_t (*)(Il2CppObject*, bool);
using Fn_GcHandleNewWeakRef = uint32_t (*)(Il2CppObject*, bool);
using Fn_GcHandleFree = void (*)(uint32_t);
using Fn_GcHandleGetTarget = Il2CppObject* (*)(uint32_t);
using Fn_GcAllocFixed = void* (*)(size_t);
using Fn_GcFreeFixed = void (*)(void*);
using Fn_GcWriteBarrierSetField = void (*)(Il2CppObject*, void**, void*);
using Fn_GcIsIncremental = bool (*)();
using Fn_RuntimeClassInit = void (*)(Il2CppClass*);
using Fn_Init = int (*)(const char*);   // il2cpp_init：引擎初始化入口（等它返回才允许碰 API）
using Fn_TypeGetObject = Il2CppObject* (*)(const Il2CppType*);
using Fn_ClassFromSystemType = Il2CppClass* (*)(Il2CppObject*);
using Fn_ClassGetDeclaringType = Il2CppClass* (*)(Il2CppClass*);
using Fn_FormatException = void (*)(Il2CppObject*, char*, int);
using Fn_MonitorEnter = void (*)(Il2CppObject*);
using Fn_MonitorExit = void (*)(Il2CppObject*);

/// dlsym 出来的函数指针集合 + 绑定结果。
struct Api {
    void* handle = nullptr;

    // 必需
    Fn_Init init = nullptr;
    Fn_DomainGet domain_get = nullptr;
    Fn_ThreadAttach thread_attach = nullptr;
    Fn_DomainGetAssemblies domain_get_assemblies = nullptr;
    Fn_AssemblyGetImage assembly_get_image = nullptr;
    Fn_ImageGetName image_get_name = nullptr;
    Fn_ImageGetClassCount image_get_class_count = nullptr;
    Fn_ImageGetClass image_get_class = nullptr;
    Fn_ClassFromName class_from_name = nullptr;
    Fn_ClassGetName class_get_name = nullptr;
    Fn_ClassGetNamespace class_get_namespace = nullptr;
    Fn_ClassGetParent class_get_parent = nullptr;
    Fn_ClassGetFieldFromName class_get_field_from_name = nullptr;
    Fn_FieldGetOffset field_get_offset = nullptr;
    Fn_FieldGetFlags field_get_flags = nullptr;
    Fn_ClassGetMethodFromName class_get_method_from_name = nullptr;
    Fn_MethodGetParamCount method_get_param_count = nullptr;
    Fn_MethodGetName method_get_name = nullptr;

    // 可选（缺失只告警；用之前判空）
    Fn_FieldGetValue field_get_value = nullptr;
    Fn_FieldSetValue field_set_value = nullptr;
    Fn_FieldGetType field_get_type = nullptr;
    Fn_ClassFromIl2CppType class_from_il2cpp_type = nullptr;
    Fn_ClassIsEnum class_is_enum = nullptr;
    Fn_ClassIsValueType class_is_valuetype = nullptr;
    Fn_ClassIsAssignableFrom class_is_assignable_from = nullptr;
    Fn_ClassEnumBaseType class_enum_basetype = nullptr;
    Fn_ClassGetFields class_get_fields = nullptr;
    Fn_FieldGetName field_get_name = nullptr;
    Fn_FieldGetParent field_get_parent = nullptr;
    Fn_MethodGetClass method_get_class = nullptr;
    Fn_ClassGetElementClass class_get_element_class = nullptr;
    Fn_ClassGetRank class_get_rank = nullptr;
    Fn_ValueBox value_box = nullptr;
    Fn_ClassValueSize class_value_size = nullptr;
    Fn_ClassGetType class_get_type = nullptr;
    Fn_ClassGetNestedTypes class_get_nested_types = nullptr;
    Fn_FieldStaticGetValue field_static_get_value = nullptr;
    Fn_FieldStaticSetValue field_static_set_value = nullptr;
    Fn_RuntimeInvoke runtime_invoke = nullptr;
    Fn_MethodGetFlags method_get_flags = nullptr;
    Fn_MethodIsInstance method_is_instance = nullptr;
    Fn_ClassGetMethods class_get_methods = nullptr;
    Fn_MethodGetParam method_get_param = nullptr;
    Fn_MethodGetParamName method_get_param_name = nullptr;
    Fn_MethodGetReturnType method_get_return_type = nullptr;
    Fn_TypeGetName type_get_name = nullptr;
    Fn_TypeGetAttrs type_get_attrs = nullptr;
    Fn_Free il2cpp_free = nullptr;
    Fn_ObjectNew object_new = nullptr;
    Fn_ObjectGetClass object_get_class = nullptr;
    Fn_ObjectUnbox object_unbox = nullptr;
    Fn_ArrayNew array_new = nullptr;
    Fn_ArrayNewFull array_new_full = nullptr;
    Fn_StringNew string_new = nullptr;
    Fn_GcHandleNew gchandle_new = nullptr;
    Fn_GcHandleNewWeakRef gchandle_new_weakref = nullptr;
    Fn_GcHandleFree gchandle_free = nullptr;
    Fn_GcHandleGetTarget gchandle_get_target = nullptr;
    Fn_GcAllocFixed gc_alloc_fixed = nullptr;
    Fn_GcFreeFixed gc_free_fixed = nullptr;
    Fn_GcWriteBarrierSetField gc_wbarrier_set_field = nullptr;
    Fn_GcIsIncremental gc_is_incremental = nullptr;
    Fn_RuntimeClassInit runtime_class_init = nullptr;
    Fn_TypeGetObject type_get_object = nullptr;
    Fn_ClassFromSystemType class_from_system_type = nullptr;
    Fn_ClassGetDeclaringType class_get_declaring_type = nullptr;
    Fn_FormatException format_exception = nullptr;
    Fn_MonitorEnter monitor_enter = nullptr;
    Fn_MonitorExit monitor_exit = nullptr;

    /// 表长，日志里的 "必需 n/17，可选 m/51" 用它们当分母（加符号时不会忘记改日志）。
    static constexpr int kRequiredSymbolCount = 17;
    static constexpr int kOptionalSymbolCount = 51;

    /// 绑定已完成且**必需**符号都在。
    bool Complete() const { return handle != nullptr && required_bound == kRequiredSymbolCount; }

    int required_bound = 0;
    int optional_bound = 0;
    std::string missing;  // 必需符号缺失清单；空表示必需全齐

    /// 在**已加载**的 libil2cpp.so 上绑定符号。用 `RTLD_NOLOAD` 只查已加载的库，绝不触发加载：
    /// Unity 自己会在引擎启动时加载它，抢在前面加载会打乱初始化顺序（JNI_OnLoad 跑两次，
    /// 实测能看到两次 `IL2CPP: JNI_OnLoad`）。绑定本身**不调用任何 il2cpp API**，因此安全。
    bool Bind(const char* so_name = "libil2cpp.so");
};

}  // namespace pylon::il2cpp
