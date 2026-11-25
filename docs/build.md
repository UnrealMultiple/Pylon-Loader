# 构建

从空 clone 到设备上跑起来。日常只需要一条 `pylon.ps1`，其余是它背后的规则。

## 1. 前置

| 项 | 版本 | 说明 |
|---|---|---|
| 系统 | Windows 10/11 + PowerShell 7 | 脚本与 Gradle 均按 Windows 验证 |
| .NET SDK | 10.x | 托管层、生成器、示例 Mod |
| JDK | 17 | 启动器与宿主，推荐 Android Studio 自带 JBR |
| Android SDK | 含 platform 36 / build-tools | 通过 `local.properties` 的 `sdk.dir` 指定 |
| Android NDK | r27（`27.1.12297006` 验证过） | 内核用 clang++ 直编 |
| 设备 | Android arm64，开启 USB 调试 | 可选，只在真机验证时需要 |

```powershell
git clone https://github.com/Controllerdestiny/Pylon-Loader.git
cd Pylon-Loader
git submodule update --init --recursive   # external/shadowhook（hook 后端，随内核直编）
pwsh -File src/csharp/tools/pylon.ps1 check
```

`check` 全绿就是可以开工的状态；缺什么它会给出补齐命令。

## 2. 仓库里没有的两类文件

要么体积大、要么由你的正版游戏派生，因此不进 git：

| 目录 | 内容 | 怎么来 |
|---|---|---|
| `refs/` | 游戏的 `libil2cpp.so`、`global-metadata.dat`、`dump.cs` | 从**你自己安装的正版** APK 解出，再用 Il2CppDumper 生成 `dump.cs` |
| `interop/generated/` | Il2CppInterop 代理程序集与 `On.*` 钩子 | `pylon.ps1 interop -DummyDir <Il2CppDumper 的 DummyDll>` |

两者都与**游戏版本**绑定：换版本必须整组重做，混用会在运行期以引用校验失败或崩溃暴露。

运行时（`runtime/`）同样不在仓库里，但它**不依赖任何外部发行物**：CoreCLR native + BCL
按 pin（版本 + SHA-256）直接从 nuget.org 抓取，见 2.2。hook 后端 ShadowHook 的源码随
子模块进仓库，由内核构建脚本静态编进 `libpylon.so`（见 2.4）——构建自闭环，除了你的游戏
dump 之外没有第三个需要人手准备的输入。

### 2.1 refs（游戏 dump）

```powershell
adb shell pm path com.and.games505.Terraria    # 拿到 base.apk 路径
```

解包后需要三个文件：

```text
refs/lib/arm64-v8a/libil2cpp.so
refs/lib/arm64-v8a/libGNS_Wrapper.so          # 1.4.6 新增的联网后端（P/Invoke 目标）
refs/assets/bin/Data/Managed/Metadata/global-metadata.dat
refs/dump.cs                                  # Il2CppDumper 输出
```

`libil2cpp.so` 与 `global-metadata.dat` 必须来自同一次安装；`dump.cs` 用于查名字，不参与构建。

### 2.2 runtime

```powershell
pwsh -File src/csharp/tools/pylon.ps1 runtime
```

运行时就是**一个微软官方 NuGet 包**：`microsoft.netcore.app.runtime.android-arm64`。
版本与 SHA-256 锚在 `src/csharp/tools/dotnet-runtime.env`（10.0.12），
`fetch-dotnet-runtime.ps1` 下载 → 校验哈希 → 摊成 deploy 需要的布局：

```text
runtime/bepinex-android/
  lib/        libcoreclr.so、libclrjit.so、libmscordaccore.so …（8 个部署件）
  dotnet/     BCL + PAL 副本（PAL 必须与 BCL 同目录树，见 fetch 脚本内注释）
```

幂等：已就绪就跳过，`-Force` 重抓。BepInEx 托管栈里唯一被 APK 装载的 6 个
`Microsoft.Extensions.*.dll` 随仓库放在 `vendor/ms-extensions/`（出处见该目录 README），
与运行时抓取无关。BepInEx 栈里被修补的源码树按需随仓库收编
（`src/csharp/MonoMod`，见其 `VENDOR.md`；Pylon 运行时自身不装载 BepInEx/Harmony/MonoMod）。

### 2.3 interop/generated

```powershell
pwsh -File src/csharp/tools/pylon.ps1 interop -DummyDir refs\DummyDll
```

生成器会先编译 `src/csharp/il2cppinterop`（仓库内自带源码），然后产出：

- 游戏程序集的强类型代理（`Assembly-CSharp.dll`、`Il2Cppmscorlib.dll` 等）
- `On.*` 钩子事件（写进代理程序集，不额外产出程序集）
- `MethodAddressToToken.db`（运行期按名定位方法的索引）

### 2.4 shadowhook

hook 后端以**公开上游子模块**随仓库提供（`external/shadowhook`，钉 v1.0.10，
`git submodule update --init --recursive` 即就绪）。内核构建脚本
`src/core/build.ps1` 把它的 21 个 C 源文件静态编进 `libpylon.so`：

- `src/core/src/hook/HookApi.cpp` 直接 `#include <shadowhook.h>` 调函数——
  没有 dlopen/dlsym 的运行期间接层，也没有独立的 `libshadowhook.so`；
- shadowhook 符号全部 hidden（`-fvisibility=hidden`），内核对外只导出自己的 ABI；
- `sh_jni.c` 不参与编译（它定义的 JNI_OnLoad 归内核），其 Java 侧入口是
  "独立 .so + System.loadLibrary" 形态专用的。

不再需要从 Maven AAR 手动解压任何东西。

## 3. 一条命令构建

```powershell
pwsh -File src/csharp/tools/pylon.ps1 all -DummyDir refs\DummyDll
```

依次执行 runtime → interop → build → install → run。每一步都幂等：已就绪的跳过，缺件的直接说明缺什么、要传哪个参数。

## 4. `pylon.ps1` 命令参考

| 命令 | 作用 | 主要参数 |
|---|---|---|
| `check` | 工具链、`refs/`、`runtime/`、派生件体检 | — |
| `runtime` | 按 pin 从 nuget.org 抓取运行时，摊开 `runtime/bepinex-android/` | `-Force` |
| `interop` | 生成代理程序集与 `On.*` | `-DummyDir`、`-Force` |
| `deploy` | 运行时 native → `debug/jniLibs`，BCL / 托管件 / 代理 → `debug/assets` | — |
| `build` | 内核（有变动才编）→ deploy → APK | `-Force` |
| `install` | 安装 debug APK | — |
| `run` | 启动，命中日志标记后返回 | `-Apk`、`-WaitFor`、`-Seconds` |
| `managed` | 编托管件并用 `run-as` 推进 `files/pylon-managed` | `-NoBuild` |
| `push` | 编示例 Mod 并推到 `Mods/` | `-Samples`、`-NoBuild` |
| `all` | 上面全流程 | 同 runtime + interop |

所有命令共用 `-Serial`（默认 `3b5f101`）与 `-Configuration`（默认 `Release`）。adb 路径优先取 `PYLON_ADB`，其次 `D:\SDK\platform-tools\adb.exe`，最后 PATH。

常用的三条：

```powershell
pwsh -File src/csharp/tools/pylon.ps1 build      # 只重打 APK
pwsh -File src/csharp/tools/pylon.ps1 run -Apk    # 装 + 启 + 等日志
pwsh -File src/csharp/tools/pylon.ps1 managed     # 只改托管件，不重装
```

`run` 把 logcat 原始输出落到 `.work/logs/<时间>.txt`（不进 git），并在终端打印本项目日志的尾部。

## 5. 单独构建内核

```powershell
pwsh -File src/core/build.ps1             # 自动找 NDK，也可 -Ndk D:\SDK\ndk\27.1.12297006
pwsh -File src/core/build.ps1 -Rebuild    # 清中间产物重编
```

产物：`src/app/src/main/jniLibs/arm64-v8a/libpylon.so`。

不用 CMake 与 AGP 的 `externalNativeBuild`，原因写在 [src/core/README.md](../src/core/README.md)：受限环境下 cmake 的编译器探测要经管道捕获子进程输出，会永久阻塞；直接调 clang++ 只需文件重定向，0.5s 级出结果。编译契约同样在那份 README。

## 6. 打包

```powershell
gradlew.bat --offline :app:assembleDebug
```

- `GRADLE_USER_HOME` 指向仓库内 `.gradle-home`（由 `pylon.ps1` 设置），保证离线可重复。
- debug 变体默认**不**预置 `refs/` 里的游戏文件；加 `-PpylonStageRefs=true` 才会把本地回退副本打进 APK。
- 首次使用需要 `debug.keystore`；缺失时按 `check` 给出的 `keytool` 命令生成。

## 7. 真机回路

| 改了什么 | 怎么做 |
|---|---|
| Mod 源码 / 托管层 | `pylon.ps1 managed` 或 `pylon.ps1 push`，重启游戏即可，不重装 |
| 内核 / 宿主 / 启动器 / 资源 | `pylon.ps1 build` → `pylon.ps1 run -Apk` |
| 游戏版本 | 重做 `refs/`、`runtime/`、`interop/generated/`，再重新导入 |

日志统一 tag 为 `PylonLoader`，阶段行带 `[stage] +Nms` 偏移：

```powershell
adb logcat -s PylonLoader
```

## 8. CI

| workflow | artifact | 内容 | 用途 |
|---|---|---|---|
| `build-kernel.yml` | `libpylon` | `libpylon.so`（arm64-v8a，shadowhook 静态内嵌）与导出符号清单 | 放到 `src/app/src/main/jniLibs/arm64-v8a/` |
| `build-runtime.yml` | —（无 artifact） | runtime pin 回归门：跑 `fetch-dotnet-runtime.ps1` 验证 pin | pin 漂移在这里先红 |

CI 不产出游戏文件，也不会代为分发。

## 9. 缺件症状对照

| 症状 | 缺什么 | 动作 |
|---|---|---|
| `check` 报 `refs/…` 缺失 | 游戏 dump | 见 2.1 |
| 报 `缺少 runtime/bepinex-android/lib` | 运行时 | `pylon.ps1 runtime`（见 2.2） |
| MSBuild 报找不到 `Il2Cppmscorlib` | 代理程序集 | 见 2.3 |
| Gradle 报 `libpylon.so` 不存在 | 内核没编 | `pwsh -File src/core/build.ps1` |
| 运行期 `UnsatisfiedLinkError: libc++_shared.so` | 运行时 native 未进 `jniLibs` | `pylon.ps1 deploy` |
| 导入报"安装包不在 1.4.6 线" | 游戏版本不符 | 见 [运行时导入](runtime-import.md) |
| 导入报 PairIP 拒绝 | Google Play 版构建 | 换无校验构建（亚马逊版 / 整合版） |

运行期问题见 [排障](troubleshooting.md)。
