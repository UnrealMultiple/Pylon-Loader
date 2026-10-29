// 层：内核层 / dotnet —— 在游戏进程里起 CoreCLR 并调用托管入口
// 启动序列：coreclr_initialize → coreclr_create_delegate → 在独立线程上调入口。
// 细节与踩过的坑见 docs/10、docs/11。
#pragma once

#include <string>

#include "pylon/Status.h"

namespace pylon::dotnet {

struct HostConfig {
    /// 托管入口程序集（CoreCLR 用它认入口，同时进 TPA）。
    std::string entry_assembly_path;
    /// 我们自己的托管程序集目录（进 TPA，也是 APP_CONTEXT_BASE_DIRECTORY）。
    std::string core_dir;
    /// BCL 目录（.NET 运行时包的 net10.0 那批 dll + PAL）。
    std::string dotnet_dir;
    /// 代理程序集目录（Assembly-CSharp 等 + Il2CppInterop），也要进 TPA。
    std::string interop_dir;
    /// 入口坐标：程序集名 / 类型全名 / 静态无参方法名。
    std::string entry_assembly_name;
    std::string entry_type;
    std::string entry_method;
    /// 托管 stdout/stderr 重定向目录（Android 上看不到 stderr）。
    std::string log_dir;
    /// Mod 目录，通过环境变量 PYLON_MODS_DIR 交给托管层。
    std::string mods_dir;
    std::string domain_name = "PylonHost";
    /// 入口线程栈：入口要生成 interop、跑 chainloader，UnityMain 的栈不够（会 Stack overflow → SIGABRT）。
    size_t entry_stack_bytes = 64u * 1024u * 1024u;
};

/// 起 CoreCLR 并调用入口。幂等。
Status Start(const HostConfig& config);

/// 在调用者线程上调用入口类型里的另一个静态无参方法（Mod 的 Load 要在游戏线程上调）。
Status CallManagedEntry(const std::string& method);


bool Started();

}  // namespace pylon::dotnet
