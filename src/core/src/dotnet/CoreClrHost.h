#pragma once

#include <cstddef>
#include <string>

#include "pylon/Status.h"

namespace pylon::dotnet {

struct HostConfig {
    std::string entry_assembly_path;
    std::string core_dir;
    std::string dotnet_dir;
    std::string interop_dir;
    std::string entry_assembly_name;
    std::string entry_type;
    std::string entry_method;
    std::string log_dir;
    std::string mods_dir;
    std::string domain_name = "PylonHost";
    size_t entry_stack_bytes = 64u * 1024u * 1024u;
};

/// 起 CoreCLR 并解析入口，但不调用它；与 IL2CPP 无关，可与引擎启动并行。
Status Prepare(const HostConfig& config);

/// 在独立大栈线程上执行入口（入口会建 interop，要求 IL2CPP 已就绪）。
Status RunEntry();

/// 在调用者线程上调用入口类型里的另一个静态无参方法。
Status CallManagedEntry(const std::string& method);

bool Prepared();

}  // namespace pylon::dotnet
