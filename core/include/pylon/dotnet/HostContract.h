// 层：内核层 / dotnet —— CoreCLR 宿主契约 ABI（dotnet/runtime 的 host_runtime_contract.h，MIT）
// 必须提供它，否则 CoreCLR 只去 libcoreclr.so 旁边找 System.Private.CoreLib.dll，报
// COR_E_FILENOTFOUND(0x80070002)。这里只实现 get_runtime_property，其余回调留空。
#pragma once

#include <cstddef>

#if defined(_WIN32)
#define PYLON_HOST_CONTRACT_CALLTYPE __stdcall
#else
#define PYLON_HOST_CONTRACT_CALLTYPE
#endif

namespace pylon::dotnet {

inline constexpr const char* kPropertyTrustedPlatformAssemblies = "TRUSTED_PLATFORM_ASSEMBLIES";
inline constexpr const char* kPropertyAppPaths = "APP_PATHS";
inline constexpr const char* kPropertyAppContextBaseDirectory = "APP_CONTEXT_BASE_DIRECTORY";
inline constexpr const char* kPropertySystemCorelibDirectory = "SYSTEM_CORELIB_DIRECTORY";
inline constexpr const char* kPropertyHostRuntimeContract = "HOST_RUNTIME_CONTRACT";

struct HostRuntimeContract {
    size_t size;
    void* context;
    /// 返回含结尾 0 的长度；找不到返回 (size_t)-1。
    size_t(PYLON_HOST_CONTRACT_CALLTYPE* get_runtime_property)(const char* key, char* value_buffer,
                                                               size_t value_buffer_size,
                                                               void* contract_context);
    // bundle_probe / pinvoke_override / external_assembly_probe / get_native_code_data：不用，留空
    void* bundle_probe;
    void* pinvoke_override;
    void* external_assembly_probe;
    void* get_native_code_data;
};

}  // namespace pylon::dotnet
