# 内核（libpylon.so）

游戏进程里的 C++20 内核。职责：绑定 `il2cpp_*` → 等引擎稳定 → 按名解析并校验引用 → 接上 ShadowHook detour、CoreCLR 与图形层。

设计原则一句话：**Mod 可以没有，游戏必须能跑**。任何必需引用解析失败，内核只让 Mod 停用并写清原因，不把进程带崩。

整体位置与时序见 [架构](../../docs/architecture.md)，构建流程见 [构建](../../docs/build.md)。

## 目录

| 路径 | 职责 |
|---|---|
| `build.ps1` | 受支持的构建入口：NDK clang++ 直编，产物 `src/app/src/main/jniLibs/arm64-v8a/libpylon.so` |
| `include/pylon/Log.h` | 统一日志（tag `PylonLoader`，与 Kotlin 侧一致） |
| `include/pylon/Status.h` | `Status` / `Outcome<T>`：显式错误原因，不用异常 |
| `include/pylon/mod/ModApi.h` | 给 Mod 的导出 ABI（`PylonMod_*`） |
| `include/pylon/dotnet/HostContract.h` | C++ 与托管入口之间的契约 |
| `src/jni/PylonJni.cpp` | 唯一的 JNI 边界（`PylonCore` 的 native 方法） |
| `src/boot/*` | 阶段状态机、就绪探测、引用校验与报告 |
| `src/il2cpp/Symbols.*` | `il2cpp_*` 符号表：声明、绑定、计数由同一份清单生成 |
| `src/il2cpp/Domain.*` `Session.cpp` `Metadata.*` `Invoke.*` | 域与线程票据、按名找类、字段读写、方法调用 |
| `src/hook/HookApi.cpp` | ShadowHook 的 hook / unhook 出口（`shadowhook_hook_func_addr` / `shadowhook_unhook`） |
| `src/dotnet/CoreClrHost.cpp` | CoreCLR 托管：`Prepare` / `RunEntry` |
| `src/mod/ModApi.cpp` | `PylonMod_*` 的实现 |
| `src/graphics/*` | GLES/EGL 入口表、GL 状态镜像、帧编排、屏幕效果 |
| `src/imgui/*` | ImGui 源码、GLES3 后端、触摸桥、跨线程快照与上屏 |
| `src/il2cpp/Guard.*` | 跨回游戏代码的调用 trampoline（`Guard.S` + 自定义 personality） |

## 构建

```powershell
pwsh -File src/core/build.ps1             # 自动找 NDK，也可 -Ndk D:\SDK\ndk\27.1.12297006
pwsh -File src/core/build.ps1 -Rebuild    # 清中间产物重编
```

不走 CMake 与 AGP 的 `externalNativeBuild`：受限环境下 cmake 的编译器探测要用管道捕获子进程输出，会永久阻塞；直接调 clang++ 只需文件重定向，0.5s 级出结果。

## 编译契约

改动这些等于改动对外承诺：

```text
--target=aarch64-linux-android28   -std=c++20   -O2   -fPIC   -fvisibility=hidden
-fno-exceptions -fno-rtti -static-libstdc++ -Wl,--exclude-libs,ALL
-Wl,-z,max-page-size=16384        -llog -ldl -lGLESv3 -landroid
```

- `-fno-exceptions` / `-fno-rtti`：C# 异常在 IL2CPP 里是真正的 C++ 异常，由 `libil2cpp` 的展开器抛出；我们使用 NDK libunwind，两侧混用会把进程带崩。因此内核调托管方法一律走 `il2cpp_runtime_invoke`（它在自己的帧里捕获异常）。
- `-static-libstdc++` + `--exclude-libs,ALL`：不依赖游戏那份 `libc++_shared.so`，也不让静态库里的 libc++ 符号顶替游戏的同名符号。
- `-Wl,-z,max-page-size=16384`：Android 15+ 的 16 KB 页对齐。
- 脚本末尾做导出自检：`PylonMod_*` 必须可见，libc++ 残留必须为 0。

`.so` 不进 git（`.gitignore` 的 `*.so` 与 `src/app/src/main/jniLibs/`），构建前先跑一次脚本。

## 硬约束

1. **`il2cpp_init` 执行期间调任何 IL2CPP API 都会 SIGSEGV**。内核没有"试探性调用"：`nativeStart()` 只做 `dlsym` 绑定，第一次真正调用发生在静置之后、且是稀疏轮询。
2. **外来线程必须先 `il2cpp_thread_attach()`**。域指针只取一次，attach 是每线程一次。
3. **不写死偏移**。全部按名解析；解析不到就报明确原因并让 Mod 停用，不静默。
4. **少一个必需符号即整体判定失败**：宁可不启用 Mod，也不要半可用状态下随机崩。
5. **IL2CPP 对象与方法只在游戏线程碰**。跨线程调用被拒绝并记日志。
6. **不 patch Unity 的绘制入口**（`SpriteBatch.Draw` / `Main.EntitySpriteDraw`）：手机版实测会渲染错乱。ImGui 画在游戏自己的 GLES 上下文，帧末 `eglSwapBuffers` 之前落到默认 framebuffer。

## 排障

- 阶段与引用报告：`adb logcat -s PylonLoader`，看 `[boot] +Nms` 与 `RefsValidated`。
- 崩溃：`files/crash-last.txt` 与 logcat 同看；内核崩溃另取 `/data/tombstones/`。
- 构建异常：先看 `src/core/build-arm64/*.log`，再对照 [构建](../../docs/build.md) 的缺件表。
