// 层：内核层 / il2cpp —— 域/线程与按名解析
// **唯一允许触碰 IL2CPP 的地方**：每个入口都要求先 `Attach()` 成功，"绝不能早调"这条
// 约束收在一个点上（关键约束见 docs/13）。
#pragma once

#include <atomic>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

#include "il2cpp/Il2CppApi.h"
#include "pylon/Status.h"

namespace pylon::il2cpp {

class Runtime {
public:
    static Runtime& Instance();

    /// 绑定导出符号表（幂等）。不调用任何 il2cpp API，可在 Unity 启动前调用。
    Status Bind();

    bool Bound() const { return api_.Complete(); }

    /// `il2cpp_domain_get() != null` —— 引擎已过 `il2cpp_init` 窗口；本类**唯一**允许
    /// 未 attach 时调用的 API。不缓存 domain：缓存只发生在 Attach()，免得探测命中被当成已 attach。
    bool EngineLooksReady() const {
        return api_.Complete() && api_.domain_get != nullptr && api_.domain_get() != nullptr;
    }

    /// 取 domain 并 attach **当前线程**（第一次真正调 IL2CPP，幂等）；attach 按线程生效，别的线程要自己 attach。
    Status Attach();

    /// **域**已取到（= 引擎稳定过）。**不代表当前线程已 attach**。
    bool Attached() const { return domain_ != nullptr; }

    /// **当前线程**是否已 attach。判断"能不能在这一线程调 IL2CPP"必须用它。
    bool AttachedOnThisThread() const { return thread_attached_; }

    /// 登记游戏线程（UnityMain）的 tid（宿主投递小任务到该线程后调用）。
    /// 托管方法只允许在这个线程上调；未登记时 `IsGameThread()` 一律 false（宁可不做）。
    void NoteGameThread();

    bool IsGameThread() const;

    /// 已登记的游戏线程 tid（0 = 未登记）。
    unsigned long GameThreadId() const;

    /// 按命名空间 + 名字解析类：遍历已加载程序集的 image。
    Outcome<Il2CppClass*> FindClass(const std::string& namespaze, const std::string& name);

    /// 解析字段：`il2cpp_class_get_field_from_name` 只查本类，这里沿父类链向上找。
    Outcome<FieldInfo*> FindField(Il2CppClass* klass, const std::string& name);

    /// 解析方法：IL2CPP 只按"名字 + 参数个数"查，重载靠 argc 区分；同样走父类链。
    Outcome<const MethodInfo*> FindMethod(Il2CppClass* klass, const std::string& name, int argc);

    /// 只知道名字时：按 argc 0..kMaxProbeArity 依次试，命中回填 argc。
    Outcome<const MethodInfo*> FindMethodAnyArity(Il2CppClass* klass, const std::string& name,
                                                 int* argc);

    /// 字段是否 static。**不能靠 offset==0 判断**：静态块里只有排最前的字段是 0（实测 dayTime=1152、player=1592）。
    bool FieldIsStatic(FieldInfo* field) const;

    /// 字段偏移（写日志与 dump 对账用）。
    size_t FieldOffset(FieldInfo* field) const { return api_.field_get_offset(field); }

    uint32_t MethodParamCount(const MethodInfo* method) const {
        return api_.method_get_param_count(method);
    }
    std::string ClassName(Il2CppClass* klass) const;

    /// 已加载程序集清单（形如 `Assembly-CSharp.dll（1234 类）`），只用于日志。
    std::vector<std::string> Images();

    const Api& api() const { return api_; }

private:
    Runtime() = default;

    /// 省略 argc 时最多试到几个参数。
    static constexpr int kMaxProbeArity = 8;

    /// 缓存最常用的两个 image（Assembly-CSharp / mscorlib）；其余程序集按需全量扫。
    void CachePreferredImages();

    Api api_{};
    Il2CppDomain* domain_ = nullptr;
    const Il2CppImage* game_image_ = nullptr;
    const Il2CppImage* corlib_image_ = nullptr;
    mutable std::mutex mutex_;
    mutable std::mutex cache_mutex_;  // 只保护 class_cache_
    std::unordered_map<std::string, Il2CppClass*> class_cache_;

    static thread_local bool thread_attached_;

    std::atomic<unsigned long> game_thread_id_{0};
};

}  // namespace pylon::il2cpp
