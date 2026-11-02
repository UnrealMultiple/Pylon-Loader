#include "dotnet/CoreClrHost.h"

#include <dirent.h>
#include <dlfcn.h>
#include <pthread.h>
#include <unistd.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <vector>

#include "pylon/Log.h"
#include "pylon/dotnet/HostContract.h"

namespace pylon::dotnet {
namespace {

constexpr int kPropertyCount = 5;

constexpr const char* kPropertyKeys[kPropertyCount] = {
        kPropertyTrustedPlatformAssemblies, kPropertyAppPaths, kPropertyAppContextBaseDirectory,
        kPropertySystemCorelibDirectory,    kPropertyHostRuntimeContract,
};

using CoreClrInitializeFn = int (*)(const char*, const char*, int, const char**, const char**, void**,
                                   unsigned int*);
using CoreClrCreateDelegateFn = int (*)(void*, unsigned int, const char*, const char*, const char*, void**);
using ErrorWriterFn = void (*)(const char*);
using EntryFn = void (*)();

class Stopwatch {
public:
    long Ms() const {
        const auto now = std::chrono::steady_clock::now();
        return std::chrono::duration_cast<std::chrono::milliseconds>(now - start_).count();
    }

private:
    std::chrono::steady_clock::time_point start_ = std::chrono::steady_clock::now();
};

bool FileExists(const std::string& path) { return access(path.c_str(), F_OK) == 0; }

std::vector<std::string> ScanDlls(const std::string& dir) {
    std::vector<std::string> result;
    DIR* handle = opendir(dir.c_str());
    if (handle == nullptr) return result;

    constexpr const char* kSuffix = ".dll";
    const size_t suffix_length = std::strlen(kSuffix);
    while (const dirent* entry = readdir(handle)) {
        const size_t length = std::strlen(entry->d_name);
        if (length <= suffix_length) continue;
        if (std::strcmp(entry->d_name + length - suffix_length, kSuffix) != 0) continue;
        result.emplace_back(dir + '/' + entry->d_name);
    }
    closedir(handle);
    std::sort(result.begin(), result.end());
    return result;
}

size_t TpaBytes(const std::vector<std::string>& dlls) {
    size_t bytes = 0;
    for (const std::string& dll : dlls) bytes += dll.size() + 1;
    return bytes;
}

void AppendTpa(std::string* tpa, const std::vector<std::string>& dlls) {
    for (const std::string& dll : dlls) {
        if (!tpa->empty()) tpa->push_back(':');
        tpa->append(dll);
    }
}

/// 属性与契约必须是 static 生存期：coreclr_initialize 返回之后运行时还会回调。
struct HostStorage {
    std::string tpa;
    std::string app_paths;
    std::string core_dir;
    std::string bcl_dir;
    char contract_address[64] = {};
    const char* values[kPropertyCount] = {};
    HostRuntimeContract contract{};

    void Build(const HostConfig& config, const std::vector<std::string>& core_dlls,
               const std::vector<std::string>& interop_dlls, const std::vector<std::string>& bcl_dlls) {
        tpa.clear();
        tpa.reserve(TpaBytes(core_dlls) + TpaBytes(interop_dlls) + TpaBytes(bcl_dlls));
        AppendTpa(&tpa, core_dlls);
        AppendTpa(&tpa, interop_dlls);
        AppendTpa(&tpa, bcl_dlls);

        app_paths = config.core_dir + ':' + config.dotnet_dir;
        core_dir = config.core_dir;
        bcl_dir = config.dotnet_dir;
        std::snprintf(contract_address, sizeof(contract_address), "0x%zx",
                      reinterpret_cast<size_t>(&contract));

        values[0] = tpa.c_str();
        values[1] = app_paths.c_str();
        values[2] = core_dir.c_str();
        values[3] = bcl_dir.c_str();
        values[4] = contract_address;

        contract = {};
        contract.size = sizeof(contract);
        contract.context = this;
        contract.get_runtime_property = &GetRuntimeProperty;
    }

private:
    static size_t PYLON_HOST_CONTRACT_CALLTYPE GetRuntimeProperty(const char* key, char* value_buffer,
                                                                size_t value_buffer_size,
                                                                void* contract_context) {
        auto* storage = static_cast<HostStorage*>(contract_context);
        if (storage == nullptr) return static_cast<size_t>(-1);
        for (int i = 0; i < kPropertyCount - 1; ++i) {
            if (std::strcmp(kPropertyKeys[i], key) != 0) continue;
            const size_t length = std::strlen(storage->values[i]) + 1;
            if (value_buffer_size < length) return length;
            std::memcpy(value_buffer, storage->values[i], length);
            return length;
        }
        return static_cast<size_t>(-1);
    }
};

void RedirectStandardStreams(const std::string& log_dir) {
    if (log_dir.empty()) return;
    std::freopen((log_dir + "/coreclr_stdout.log").c_str(), "w", stdout);
    std::freopen((log_dir + "/coreclr_stderr.log").c_str(), "w", stderr);
}

struct RuntimeImage {
    CoreClrInitializeFn initialize = nullptr;
    CoreClrCreateDelegateFn create_delegate = nullptr;
};

Status OpenRuntimeImage(RuntimeImage* image) {
    void* handle = dlopen("libcoreclr.so", RTLD_NOLOAD | RTLD_NOW);
    if (handle == nullptr) {
        const char* error = dlerror();
        return Status::Fail(std::string("libcoreclr.so 未加载：") + (error != nullptr ? error : "未知"));
    }
    image->initialize = reinterpret_cast<CoreClrInitializeFn>(dlsym(handle, "coreclr_initialize"));
    image->create_delegate =
            reinterpret_cast<CoreClrCreateDelegateFn>(dlsym(handle, "coreclr_create_delegate"));
    if (image->initialize == nullptr || image->create_delegate == nullptr) {
        return Status::Fail("libcoreclr.so 缺少 coreclr_initialize / coreclr_create_delegate");
    }
    if (auto set_error_writer = reinterpret_cast<void (*)(ErrorWriterFn)>(
                dlsym(handle, "coreclr_set_error_writer"));
        set_error_writer != nullptr) {
        set_error_writer([](const char* message) {
            if (message != nullptr && *message != '\0') LOGI("coreclr", "%s", message);
        });
    }
    return Status::Ok();
}

void* EntryThreadMain(void* arg) {
    LOGI("dotnet", "托管入口开始（线程 %lu）", static_cast<unsigned long>(gettid()));
    reinterpret_cast<EntryFn>(arg)();
    LOGI("dotnet", "托管入口返回");
    return nullptr;
}

Status RunOnWorker(EntryFn fn, size_t stack_bytes) {
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

enum class State : int { Idle = 0, Preparing = 1, Prepared = 2, Failed = 3 };

std::atomic<int> g_state{static_cast<int>(State::Idle)};
std::atomic<bool> g_entry_ran{false};
std::mutex g_entry_mutex;

RuntimeImage g_image;
HostStorage g_storage;
void* g_host_handle = nullptr;
unsigned int g_domain_id = 0;
EntryFn g_entry = nullptr;
size_t g_entry_stack_bytes = 64u * 1024u * 1024u;
std::string g_entry_assembly;
std::string g_entry_type;

Status PrepareOnce(const HostConfig& config) {
    Stopwatch total;
    if (!FileExists(config.entry_assembly_path)) {
        return Status::Fail("入口程序集不存在：" + config.entry_assembly_path);
    }
    if (!FileExists(config.dotnet_dir)) return Status::Fail("BCL 目录不存在：" + config.dotnet_dir);

    LOGI("dotnet", "=== 准备 CoreCLR ===");
    const Status opened = OpenRuntimeImage(&g_image);
    if (!opened) return opened;

    const Stopwatch scan;
    const auto core_dlls = ScanDlls(config.core_dir);
    const auto interop_dlls = ScanDlls(config.interop_dir);
    const auto bcl_dlls = ScanDlls(config.dotnet_dir);
    if (core_dlls.empty()) return Status::Fail("core 目录里没有 .dll：" + config.core_dir);
    if (bcl_dlls.empty()) return Status::Fail("BCL 目录里没有 .dll：" + config.dotnet_dir);

    g_storage.Build(config, core_dlls, interop_dlls, bcl_dlls);
    LOGI("dotnet", "TPA：core %zu + 代理 %zu + BCL %zu（%zu 字节，扫描 %ldms）", core_dlls.size(),
         interop_dlls.size(), bcl_dlls.size(), g_storage.tpa.size(), scan.Ms());

    RedirectStandardStreams(config.log_dir);

    setenv("DOTNET_ReadyToRun", "0", 1);
    setenv("DOTNET_EnableWriteXorExecute", "0", 1);
    setenv("PYLON_LOG_DIR", config.log_dir.c_str(), 1);
    if (!config.mods_dir.empty()) setenv("PYLON_MODS_DIR", config.mods_dir.c_str(), 1);

    const Stopwatch init;
    int rc = -1;
    for (int attempt = 1; attempt <= 2; ++attempt) {
        void* host = nullptr;
        unsigned int domain = 0;
        rc = g_image.initialize(config.entry_assembly_path.c_str(), config.domain_name.c_str(),
                                kPropertyCount, const_cast<const char**>(kPropertyKeys),
                                g_storage.values, &host, &domain);
        LOGI("dotnet", "coreclr_initialize 第 %d 次 rc=0x%x（%ldms）", attempt, rc, init.Ms());
        if (rc >= 0) {
            g_host_handle = host;
            g_domain_id = domain;
            break;
        }
        if (attempt == 1) {
            setenv("DOTNET_EnableWriteXorExecute", "1", 1);
            usleep(75 * 1000);
        }
    }
    if (rc < 0) return Status::Fail("coreclr_initialize 失败：0x" + std::to_string(rc));
    LOGI("dotnet", "CoreCLR 已初始化（host=%p domain=%u，%ldms）", g_host_handle, g_domain_id, init.Ms());

    void* entry = nullptr;
    const int entry_rc =
            g_image.create_delegate(g_host_handle, g_domain_id, config.entry_assembly_name.c_str(),
                                    config.entry_type.c_str(), config.entry_method.c_str(), &entry);
    if (entry_rc != 0 || entry == nullptr) {
        return Status::Fail("找不到托管入口 " + config.entry_type + "::" + config.entry_method);
    }

    g_entry = reinterpret_cast<EntryFn>(entry);
    g_entry_assembly = config.entry_assembly_name;
    g_entry_type = config.entry_type;
    g_entry_stack_bytes = config.entry_stack_bytes;
    LOGI("dotnet", "入口已解析：%s::%s（准备共 %ldms）", g_entry_type.c_str(),
         config.entry_method.c_str(), total.Ms());
    return Status::Ok();
}

}  // namespace

bool Prepared() { return g_state.load() == static_cast<int>(State::Prepared); }

Status Prepare(const HostConfig& config) {
    int expected = static_cast<int>(State::Idle);
    if (!g_state.compare_exchange_strong(expected, static_cast<int>(State::Preparing))) {
        if (expected == static_cast<int>(State::Prepared)) return Status::Ok();
        if (expected == static_cast<int>(State::Preparing)) return Status::Fail("CoreCLR 正在启动中");
        return Status::Fail("CoreCLR 上次启动失败，不再重试");
    }

    const Status status = PrepareOnce(config);
    g_state.store(static_cast<int>(status ? State::Prepared : State::Failed));
    return status;
}

Status RunEntry() {
    std::lock_guard<std::mutex> lock(g_entry_mutex);
    if (g_entry_ran.load()) return Status::Ok();
    if (!Prepared()) return Status::Fail("CoreCLR 尚未就绪，无法执行托管入口");

    LOGI("dotnet", "=== 执行托管入口 ===");
    const Stopwatch elapsed;
    const Status status = RunOnWorker(g_entry, g_entry_stack_bytes);
    if (!status) {
        g_state.store(static_cast<int>(State::Failed));
        return status;
    }
    g_entry_ran.store(true);
    LOGI("dotnet", "=== 托管入口执行完毕（%ldms）===", elapsed.Ms());
    return Status::Ok();
}

Status CallManagedEntry(const std::string& method) {
    if (!Prepared()) return Status::Fail("CoreCLR 未启动，无法调用托管入口 " + method);

    void* fn = nullptr;
    const int rc = g_image.create_delegate(g_host_handle, g_domain_id, g_entry_assembly.c_str(),
                                           g_entry_type.c_str(), method.c_str(), &fn);
    if (rc != 0 || fn == nullptr) return Status::Fail("找不到托管入口 " + g_entry_type + "::" + method);

    LOGI("dotnet", "调用托管入口 %s（线程 %lu）", method.c_str(), static_cast<unsigned long>(gettid()));
    reinterpret_cast<EntryFn>(fn)();
    return Status::Ok();
}

}  // namespace pylon::dotnet
