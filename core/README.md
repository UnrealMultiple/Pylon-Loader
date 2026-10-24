# Pylon 内核（libpylon.so）

游戏进程里的 C++20 内核。职责四块：**绑定 IL2CPP 导出 → 等引擎稳定 → 按名字解析并校验引用 → 接上原生 hook、CoreCLR 与 ImGui 覆盖层**。

Mod 能力可以没有，游戏必须照常跑：任何必需引用解析失败，内核只让 Mod 停用并留下原因，不把进程带崩。完整的补齐与构建顺序见 [`docs/构建-从零开始.md`](../docs/构建-从零开始.md)。

## 目录

| 路径 | 职责 |
|---|---|
| `build.ps1` | **受支持的构建入口**：用 NDK 的 clang++ 直接编译，产物 `app/src/main/jniLibs/arm64-v8a/libpylon.so` |
| `include/pylon/Log.h` | 统一日志（`__android_log_print`，tag = `PylonLoader`，与 Kotlin 侧一致） |
| `include/pylon/Status.h` | `Status` / `Outcome<T>`：显式错误原因，**不用异常**（JNI 边界抛异常代价太高） |
| `include/pylon/mod/ModApi.h` | 给 Mod 的导出 ABI（`PylonMod_*`）：解析、读字段、调方法、hook |
| `include/pylon/dotnet/HostContract.h` | C++ 与托管入口之间的契约 |
| `include/pylon/imgui/ImguiApi.h` | 覆盖层的 `PylonImgui_*` 出口 |
| `src/jni/PylonJni.cpp` | 唯一的 JNI 边界（`Java_dev_pylon_loader_game_core_PylonCore_*`） |
| `src/boot/*` | 阶段状态机（KernelLoaded → EngineReady → RefsValidated / Failed）、就绪探测、引用校验与报告 |
| `src/il2cpp/Il2CppApi.*` | `il2cpp_*` 函数指针表（`dlsym` 绑定，不认识具体游戏） |
| `src/il2cpp/Il2CppRuntime.*` | 域/线程 attach、按名解析、缓存；**唯一允许触碰 IL2CPP 的地方** |
| `src/il2cpp/Call.*` | 跨 C++/托管边界的调用与转换（一律走 `il2cpp_runtime_invoke`） |
| `src/hook/HookApi.cpp` | Dobby 的 `DobbyHook` / `DobbyDestroy` 出口，托管侧的 detour provider 用它 |
| `src/dotnet/CoreClrHost.cpp` | 在游戏进程里起 CoreCLR、调入口、把入口跑在独立大栈线程上 |
| `src/mod/ModApi.cpp` | `PylonMod_*` 的实现（线程内错误缓冲、按名解析、字段读写、调用） |
| `src/imgui/*` | ImGui 源码、Vulkan 后端、触摸/输入桥与字体数据 |

## 构建

```powershell
pwsh -File core/build.ps1              # 自动找 NDK（也可 -Ndk D:\SDK\ndk\27.1.12297006）
pwsh -File core/build.ps1 -Rebuild     # 清掉中间产物重编
```

**为什么不用 CMake / AGP 的 externalNativeBuild**：本机沙箱里 cmake 的“编译器 ABI 探测”要用管道捕获 ninja 的输出，而沙箱禁止子进程用管道交换数据，结果是 cmake 与 ninja 双双 0% CPU 永久阻塞。直接调 `clang++` 只需要文件重定向，实测 0.47s 出结果。

编译契约（改动等于改动内核的对外承诺）：`--target=aarch64-linux-android28`、`-std=c++20`、`-O2 -fPIC -fvisibility=hidden`、`-fno-exceptions -fno-rtti`、`-static-libstdc++`、`-Wl,--exclude-libs,ALL`、`-Wl,-z,max-page-size=16384`（Android 15+ 的 16KB 页对齐）；链接 `-llog -ldl -lvulkan -landroid`。

- **`-fno-exceptions` / `-fno-rtti`**：C# 异常在 IL2CPP 里是真正的 C++ 异常，由 `libil2cpp` 的展开器抛出；我们这侧是 NDK 的 libunwind。自己的帧只要带 personality，两边就会打架把进程带崩。内核调托管方法一律走 `il2cpp_runtime_invoke`（它在自己的帧里捕获异常）。
- **`-static-libstdc++` + `--exclude-libs,ALL`**：不依赖游戏那份 `libc++_shared.so`，也不把静态库里的 libc++ 符号导出去顶替游戏的同名符号。
- 脚本末尾会做导出自检：`PylonMod_*` 必须可见，libc++ 残留必须为 0。

`.so` 不进 git（`.gitignore` 的 `*.so` 与 `app/src/main/jniLibs/`），构建前先跑一次脚本。

## 硬约束（改代码前先读）

1. **`il2cpp_init` 执行期间调任何 IL2CPP API 都会 SIGSEGV**（真机复现）。所以内核里没有任何“试探性调用”：`nativeStart()` 只 `dlsym` 绑定；第一次真正调 API 由 `boot/Probe` 在**静置期之后**、且**稀疏轮询**里做。
2. **外来线程必须先 `il2cpp_thread_attach()`**，否则同样会崩。域指针只取一次，但 attach 是每线程一次。
3. **不写死任何偏移**。全部按名字/符号解析；解析不到就报明确原因并让 Mod 停用，不要静默。
4. **少一个必需符号即整体绑定失败**：宁可不启用 Mod，也不要半可用状态下随机崩。
5. **IL2CPP 对象/方法只在游戏线程碰**。宿主侧由 `PylonCore.markGameThread()` 登记，托管侧经内核转投；跨线程调用会被拒绝并写日志。
6. **不要 patch Unity 的绘制入口**（`SpriteBatch.Draw` / `Main.EntitySpriteDraw`）：手机版实测会渲染错乱。`Main.DrawProjDirect` 这类游戏方法可以挂；ImGui 走自己的 Vulkan 覆盖层，不碰游戏的 SpriteBatch。

## 排查

- 日志：`adb logcat -s PylonLoader`，阶段名与引用报告都在里面。
- 启动失败：`files/crash-last.txt`（Kotlin 侧的 `CrashLog` 写）与 `logcat` 同看。
- 引用校验：`PylonCore.report()` 会逐条列出解析结果；失败原因就是停用 Mod 的依据。
- 内核构建异常：先看 `core/build-arm64/*.log`，再按 `docs/构建-从零开始.md` 第 6 节对照缺件。
