// 层：内核层 / dotnet —— CoreCLR 宿主实现（坑与理由见 docs/10）
#include "dotnet/CoreClrHost.h"

#include <dirent.h>
#include <dlfcn.h>
#include <pthread.h>
#include <unistd.h>

#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

#include "pylon/Log.h"
#include "pylon/dotnet/HostContract.h"

namespace pylon::dotnet {
namespace {

constexpr int kPropertyCount = 5;

using CoreClrInitializeFn = int (*)(const char*, const char*, int, const char**, const char**, void**,
                                    unsigned int*);
using CoreClrCreateDelegateFn = int (*)(void*, unsigned int, const char*, const char*, const char*,
                                        void**);
using ErrorWriterFn = void (*)(const char*);
using EntryFn = void (*)();

void* g_host_handle = nullptr;
unsigned int g_domain_id = 0;
CoreClrCreateDelegateFn g_create_delegate = nullptr;
std::string g_entry_assembly;
std::string g_entry_type;

// CoreCLR 可能在 coreclr_initialize 返回**之后**才回调 get_runtime_property（加载程序集时），
// 所以属性键值必须是 static 生存期 —— 曾经指向 Start() 的局部变量，那是 use-after-free。
const char* kPropertyKeys[kPropertyCount] = {
        kPropertyTrustedPlatformAssemblies, kPropertyAppPaths, kPropertyAppContextBaseDirectory,
        kPropertySystemCorelibDirectory,    kPropertyHostRuntimeContract,
};
const char* g_property_values[kPropertyCount] = {};
std::string g_tpa_list;
std::string g_app_paths;
std::string g_core_dir;
std::string g_dotnet_dir;
char g_contract_address[64] = {};

struct ContractContext {
    const char** keys;
    const char** values;
    int count;
};

ContractContext g_contract_context{};
HostRuntimeContract g_contract{};

bool FileExists(const std::string& path) { return access(path.c_str(), F_OK) == 0; }

/// 目录下的 .dll 绝对路径列表（TPA 用）；用 opendir 而非 popen("ls")：游戏进程里
/// 不该 fork/exec 一个 shell，也免掉路径里的引号问题。
std::vector<std::string> ScanDlls(const std::string& dir) {
    std::vector<std::string> result;
    DIR* handle = opendir(dir.c_str());
    if (handle == nullptr) return result;

    constexpr const char* kSuffix = ".dll";
    const size_t suffix_len = std::strlen(kSuffix);
    while (dirent* entry = readdir(handle)) {
        const std::string name = entry->d_name;
        if (name.size() <= suffix_len) continue;
        if (name.compare(name.size() - suffix_len, suffix_len, kSuffix) != 0) continue;
        result.push_back(dir + "/" + name);
    }
    closedir(handle);
    std::sort(result.begin(), result.end());
    return result;
}

size_t PYLON_HOST_CONTRACT_CALLTYPE GetRuntimeProperty(const char* key, char* value_buffer,
                                                       size_t value_buffer_size,
                                                       void* contract_context) {
    auto* context = static_cast<ContractContext*>(contract_context);
    if (context == nullptr) return static_cast<size_t>(-1);
    for (int i = 0; i < context->count; ++i) {
        if (std::strcmp(context->keys[i], key) != 0) continue;
        const size_t length = std::strlen(context->values[i]) + 1;
        if (value_buffer_size < length) return length;
        std::memcpy(value_buffer, context->values[i], length);
        return length;
    }
    return static_cast<size_t>(-1);
}

void* EntryThreadMain(void* arg) {
    LOGI("dotnet", "托管入口开始（线程 %lu）", static_cast<unsigned long>(gettid()));
    reinterpret_cast<EntryFn>(arg)();
    LOGI("dotnet", "托管入口返回");
    return nullptr;
}

/// 入口必须跑在独立线程上：UnityMain 的栈放不下"生成 interop + chainloader"，
/// 直接调会 Stack overflow → SIGABRT。**创建不了线程就如实失败**，不要退回调用者线程上跑。
Status RunEntryOnWorker(EntryFn fn, size_t stack_bytes) {
    pthread_attr_t attr;
    if (pthread_attr_init(&attr) != 0) return Status::Fail("pthread_attr_init 失败");
    if (pthread_attr_setstacksize(&attr, stack_bytes) != 0) {
        pthread_attr_destroy(&attr);
        return Status::Fail("设置入口线程栈失败");
    }
    pthread_t tid;
    const int rc = pthread_create(&tid, &attr, EntryThreadMain, reinterpret_cast<void*>(fn));
    pthread_attr_destroy(&attr);
    if (rc != 0) return Status::Fail("无法创建入口线程（rc=" + std::to_string(rc) + "）");
    pthread_join(tid, nullptr);
    return Status::Ok();
}

/// CoreCLR 启动状态机：`Start()` 里那段会阻塞数秒（跑 chainloader），
/// 只用 bool 会让第二个调用方在这窗口里再跑一次 coreclr_initialize。
enum class State : int { Idle = 0, Starting = 1, Started = 2, Failed = 3 };

std::atomic<int> g_state{static_cast<int>(State::Idle)};

Status StartOnce(const HostConfig& config) {
    if (!FileExists(config.entry_assembly_path)) {
        return Status::Fail("入口程序集不存在：" + config.entry_assembly_path);
    }
    if (!FileExists(config.dotnet_dir)) return Status::Fail("BCL 目录不存在：" + config.dotnet_dir);

    LOGI("dotnet", "=== 启动 CoreCLR ===");

    // libcoreclr.so 要由 Java 侧先 System.loadLibrary 进本 app 的命名空间（Android 的 dlopen 规则）。
    void* coreclr = dlopen("libcoreclr.so", RTLD_NOLOAD | RTLD_NOW);
    if (coreclr == nullptr) {
        const char* err = dlerror();
        return Status::Fail(std::string("libcoreclr.so 未加载：") + (err != nullptr ? err : "未知"));
    }
    auto init_fn = reinterpret_cast<CoreClrInitializeFn>(dlsym(coreclr, "coreclr_initialize"));
    auto delegate_fn =
            reinterpret_cast<CoreClrCreateDelegateFn>(dlsym(coreclr, "coreclr_create_delegate"));
    if (init_fn == nullptr || delegate_fn == nullptr) {
        return Status::Fail("libcoreclr.so 缺少 coreclr_initialize / coreclr_create_delegate");
    }
    // CoreCLR 的报错默认写 stderr（Android 看不到），挂个 error writer 收进 logcat。
    if (auto set_error_writer = reinterpret_cast<void (*)(ErrorWriterFn)>(
                dlsym(coreclr, "coreclr_set_error_writer"));
        set_error_writer != nullptr) {
        set_error_writer([](const char* message) {
            if (message != nullptr && *message != '\0') LOGI("coreclr", "%s", message);
        });
    }

    const auto core_dlls = ScanDlls(config.core_dir);
    const auto interop_dlls = ScanDlls(config.interop_dir);
    const auto bcl_dlls = ScanDlls(config.dotnet_dir);
    if (core_dlls.empty()) return Status::Fail("core 目录里没有 .dll：" + config.core_dir);
    if (bcl_dlls.empty()) return Status::Fail("BCL 目录里没有 .dll：" + config.dotnet_dir);

    std::string tpa;
    for (const auto& dll : core_dlls) tpa += (tpa.empty() ? "" : ":") + dll;
    for (const auto& dll : interop_dlls) tpa += ":" + dll;
    for (const auto& dll : bcl_dlls) tpa += ":" + dll;
    LOGI("dotnet", "TPA：core %zu + 代理 %zu + BCL %zu", core_dlls.size(), interop_dlls.size(),
         bcl_dlls.size());

    // 属性值须 static 生存期（原因见上方注释）。
    g_tpa_list = std::move(tpa);
    g_app_paths = config.core_dir + ":" + config.dotnet_dir;
    g_core_dir = config.core_dir;
    g_dotnet_dir = config.dotnet_dir;
    std::snprintf(g_contract_address, sizeof(g_contract_address), "0x%zx",
                  reinterpret_cast<size_t>(&g_contract));
    g_property_values[0] = g_tpa_list.c_str();
    g_property_values[1] = g_app_paths.c_str();
    g_property_values[2] = g_core_dir.c_str();
    g_property_values[3] = g_dotnet_dir.c_str();
    g_property_values[4] = g_contract_address;

    // 缺 HOST_RUNTIME_CONTRACT 时 CoreCLR 只在 libcoreclr.so 旁边找 System.Private.CoreLib.dll，
    // 报 COR_E_FILENOTFOUND(0x80070002)。
    g_contract_context = {kPropertyKeys, g_property_values,
                          kPropertyCount - 1};
    std::memset(&g_contract, 0, sizeof(g_contract));
    g_contract.size = sizeof(g_contract);
    g_contract.context = &g_contract_context;
    g_contract.get_runtime_property = &GetRuntimeProperty;

    setenv("DOTNET_ReadyToRun", "0", 1);
    setenv("DOTNET_EnableWriteXorExecute", "0", 1);
    setenv("PYLON_LOG_DIR", config.log_dir.c_str(), 1);
    if (!config.mods_dir.empty()) setenv("PYLON_MODS_DIR", config.mods_dir.c_str(), 1);

    int rc = -1;
    for (int attempt = 1; attempt <= 2; ++attempt) {
        void* host = nullptr;
        unsigned int domain = 0;
        rc = init_fn(config.entry_assembly_path.c_str(), config.domain_name.c_str(), kPropertyCount,
                     const_cast<const char**>(kPropertyKeys), g_property_values, &host, &domain);
        LOGI("dotnet", "coreclr_initialize 第 %d 次 rc=0x%x", attempt, rc);
        if (rc >= 0) {
            g_host_handle = host;
            g_domain_id = domain;
            break;
        }
        if (attempt == 1) {
            setenv("DOTNET_EnableWriteXorExecute", "1", 1);  // W^X 相关失败再试一次
            usleep(75 * 1000);
        }
    }
    if (rc < 0) return Status::Fail("coreclr_initialize 失败：0x" + std::to_string(rc));
    LOGI("dotnet", "CoreCLR 已启动（host=%p domain=%u）", g_host_handle, g_domain_id);

    g_create_delegate = delegate_fn;
    g_entry_assembly = config.entry_assembly_name;
    g_entry_type = config.entry_type;

    void* entry = nullptr;
    const int entry_rc = delegate_fn(g_host_handle, g_domain_id, config.entry_assembly_name.c_str(),
                                     config.entry_type.c_str(), config.entry_method.c_str(), &entry);
    if (entry_rc != 0 || entry == nullptr) {
        return Status::Fail("找不到托管入口 " + config.entry_type + "::" + config.entry_method);
    }

    if (!config.log_dir.empty()) {
        std::freopen((config.log_dir + "/coreclr_stdout.log").c_str(), "w", stdout);
        std::freopen((config.log_dir + "/coreclr_stderr.log").c_str(), "w", stderr);
    }

    const Status entry_status =
            RunEntryOnWorker(reinterpret_cast<EntryFn>(entry), config.entry_stack_bytes);
    if (!entry_status) return entry_status;
    LOGI("dotnet", "=== CoreCLR 启动完成 ===");
    return Status::Ok();
}

}  // namespace

bool Started() { return g_state.load() == static_cast<int>(State::Started); }

Status Start(const HostConfig& config) {
    int expected = static_cast<int>(State::Idle);
    if (!g_state.compare_exchange_strong(expected, static_cast<int>(State::Starting))) {
        if (expected == static_cast<int>(State::Started)) return Status::Ok();
        if (expected == static_cast<int>(State::Starting)) return Status::Fail("CoreCLR 正在启动中");
        return Status::Fail("CoreCLR 上次启动失败，不再重试");
    }

    const Status status = StartOnce(config);
    g_state.store(static_cast<int>(status ? State::Started : State::Failed));
    return status;
}

Status CallManagedEntry(const std::string& method) {
    if (!Started() || g_create_delegate == nullptr) {
        return Status::Fail("CoreCLR 未启动，无法调用托管入口 " + method);
    }
    void* fn = nullptr;
    const int rc = g_create_delegate(g_host_handle, g_domain_id, g_entry_assembly.c_str(),
                                     g_entry_type.c_str(), method.c_str(), &fn);
    if (rc != 0 || fn == nullptr) {
        return Status::Fail("找不到托管入口 " + g_entry_type + "::" + method);
    }
    LOGI("dotnet", "调用托管入口 %s（线程 %lu）", method.c_str(), static_cast<unsigned long>(gettid()));
    reinterpret_cast<EntryFn>(fn)();
    return Status::Ok();
}

}  // namespace pylon::dotnet
