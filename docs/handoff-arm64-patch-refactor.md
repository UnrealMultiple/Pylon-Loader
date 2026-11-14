# ARM64 补丁模块重构交接文档

更新时间：2026-11-15（Asia/Shanghai）
仓库：`C:\Users\admin\Desktop\Pylon-Loader`
分支：`main`
已推送提交：`ee20dab Refactor ARM64 patch strategy into managed AsmArm64`
设备：`serial 3b5f101`

## 已完成

本次重构把补丁策略放到 C#，native 保留内存和写入机制。

- AsmArm64 固定 NuGet 版本：`2.4.0`。
- BSD-2-Clause 文本：`THIRDPARTY/AsmArm64-LICENSE.txt`。
- C# API：
  - `CodeSite` / `CodeSiteProfile`
  - `CodeImage`
  - `Arm64Decoder`
  - `Arm64PatchBuilder`
  - `Arm64PatchPlan`
  - `Arm64PatchTransaction`
  - `CompareLimitPolicy`
- C# 使用 AsmArm64 进行指令解码、PC-relative 目标计算、强类型汇编和 round-trip 验证。
- native 新增严格 ABI v2：完整 BuildID、静态 fingerprint、owner/site 冲突、重复安装、页权限、原子单条写入、cache flush、结构化诊断。
- native 旧 ARM64 mask 解码实现已移除；旧导出保留但返回 `UnsupportedInstruction`，供兼容调用。
- 静态 LimitPatch profile：
  - 文件：`src/csharp/tools/limit-profile/terraria-1.4.5.8.6-arm64-v8a.json`
  - 基线 BuildID：`8c93a41873c778364a4f963965124b260dd2feb6`
  - 20/20 方法，21 个站点，包含 `lsl #12` shifted CMP。
- ItemSorting 已有的样例字典、重入保护、重复分类保护和回归测试保留。
- 固定托管程序集版本为 `1.0.0.0`，避免破坏既有 Mod 引用。
- SystemBridges 注明 profile 专用、x16 尾跳约束和通用中体拦截未完成。

## 构建

### native

```powershell
pwsh -File src/core/build.ps1
```

产物：`src/app/src/main/jniLibs/arm64-v8a/libpylon.so`

### Managed

```powershell
dotnet build src/csharp/managed/Pylon.Managed/Pylon.Managed.csproj --no-restore
dotnet build src/csharp/samples/RegressionTest/RegressionTest.csproj --no-restore
```

### APK

仓库内既有构建环境：

```powershell
$env:JAVA_HOME='C:\Users\admin\.jdks\jbr-17.0.14'
$env:GRADLE_USER_HOME='C:\Users\admin\Desktop\Pylon-Loader\.gradle-home'
$env:TEMP=Join-Path $env:GRADLE_USER_HOME 'tmp'
$env:TMP=$env:TEMP
& '.\.gradle-home\wrapper\dists\gradle-8.10.2-bin\a04bxjujx95o3nb99gddekhwo\gradle-8.10.2\bin\gradle.bat' --offline --console=plain :app:assembleDebug
```

## 测试

### C# ARM64 测试

```powershell
dotnet run --project src/csharp/tests/Arm64Patch.Tests/Arm64Patch.Tests.csproj --no-restore
```

当前结果：`ALL 52 CHECKS PASSED`。

覆盖：B/BL/B.cond、CBZ/CBNZ、TBZ/TBNZ、ADR/ADRP/LDR literal、未对齐、越界、BuildID mismatch、完整 fingerprint、强类型汇编、round-trip、ABI layout、multiword 拒绝。

### profile 工具

```powershell
dotnet run --project src/csharp/tools/limit-profile -- --self-test
dotnet run --project src/csharp/tools/limit-profile -- --verify-profile src/csharp/tools/limit-profile/terraria-1.4.5.8.6-arm64-v8a.json
```

结果：`20/20 supported methods, 21 validated 16-byte sites`。

### 已有测试

```powershell
dotnet run --project src/csharp/tests/GuardedInvoke.Tests/GuardedInvoke.Tests.csproj --no-build
dotnet run --project src/csharp/tests/HookGen.Tests/HookGen.Tests.csproj --no-build
dotnet run --project src/csharp/tests/RuntimeDetour.Tests/RuntimeDetour.Tests.csproj --no-build
```

结果：GuardedInvoke 49 项、HookGen 24 项、RuntimeDetour 全部通过。

### native 真机测试

ARM64 测试源码：`src/core/tests/Arm64PatchTests.cpp`

交叉编译后推送：

```powershell
D:\SDK\ndk\29.0.14206865\toolchains\llvm\prebuilt\windows-x86_64\bin\clang++.exe `
  --target=aarch64-linux-android28 -std=c++20 -O2 -static-libstdc++ `
  -I src/core/include src/core/src/mod/Arm64Patch.cpp `
  src/core/tests/Arm64PatchTests.cpp -o src/core/build-arm64/arm64-patch-tests -ldl

D:\SDK\platform-tools\adb.exe -s 3b5f101 push src/core/build-arm64/arm64-patch-tests /data/local/tmp/pylon-arm64-patch-tests
D:\SDK\platform-tools\adb.exe -s 3b5f101 shell chmod 755 /data/local/tmp/pylon-arm64-patch-tests
D:\SDK\platform-tools\adb.exe -s 3b5f101 shell /data/local/tmp/pylon-arm64-patch-tests
```

结果：`ARM64 patch tests passed`。

## 最新真机证据

日志：`.work/logs/20261115-013942-final-apk-runtime.txt`

已确认：

- 4 个 Mod 加载成功；
- `AsmArm64 static limit profile ... methods=20 failed=0`；
- 内容安装完成；
- GLES 后端初始化；
- 首帧上屏；
- 无 `FATAL EXCEPTION`、`Fatal signal`、`SIGSEGV`、`SIGABRT`。

最终 APK SHA256：

`1BFA42A76155D6E2E2A8D93077CF1FC16117B919CE5106FCDECD6F12938376D1`

## 必须继续关注的限制

1. **通用中体 intercept 尚未完成。** 当前只有安全 CodeSite、ARM64 解码/汇编规划、单条写入和已有 profile 专用桥。
2. `SystemBridges` 保存 GPR/NZCV/SIMD，但未保存 FPCR/FPSR；尾跳使用 x16，因此不能宣称全寄存器安全。
3. SystemBridges 没有通用卸载等待在途 callback 的协议，当前生命周期按进程存活处理。
4. `MethodCode` 的 ChatTag/水体旧 live-scan wrapper 仍存在，明确标为 legacy，仍有实时 expected 和启发式方法边界。
5. Buff/Loot/Npc 里还有旧 `PatchNative.CompareLimit` 调用，尚未全部迁移成静态 profile。
6. `CodeSite.TryCreate(raw)` 和 v1 `TryWrite` 仍是兼容 API；新业务应使用 `CodeSiteProfile` + `Arm64PatchTransaction` + native ABI v2。
7. `restore.log` 是空的未跟踪文件，交接时不要擅自删除，以免覆盖用户状态。

## 变更与推送

本次提交已经推送：

```text
ee20dab Refactor ARM64 patch strategy into managed AsmArm64
```

后续提交前先执行：

```powershell
git status --short
git diff --stat
git diff --check
git log -5 --oneline
```

只允许操作 `Pylon-Loader`，不要访问 `Bunny-Loader`，不要执行 `reset`、`clean`、`checkout --`、`revert`。
