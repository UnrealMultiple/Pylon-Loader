# 排障

按"先看日志，再定位层"的顺序来。所有本项目日志的 tag 都是 `PylonLoader`。

## 1. 日志层次

```powershell
adb logcat -s PylonLoader                       # 只看本项目
adb logcat -s PylonLoader:V AndroidRuntime:E    # 加上 Java 崩溃
adb logcat -v time > log.txt                    # 全量（`pylon.ps1 run` 已经会做）
```

内核与宿主共用同一个 tag，阶段行的格式是 `[stage] +Nms`，其中毫秒是相对宿主进程创建的偏移。看到 `[boot] +Nms` 与 `[managed]` 两串，就知道走到哪一步了。

| 前缀 | 来源 |
|---|---|
| `[launcher]`、`[import]` | 启动器进程 |
| `[host]`、`[boot]` | 宿主进程 |
| `[dotnet]` | 内核里的 CoreCLR 托管 |
| `[mod]`、`[hook]`、`[imgui]` | 内核 + 托管层 |

## 2. 崩溃

| 现象 | 先看 | 说明 |
|---|---|---|
| 启动即闪退，无本项目日志 | `adb logcat -s AndroidRuntime:E` | 宿主在建立进程时就失败了，通常是 dex 注入或 `.so` 装载 |
| 有 `[boot]` 但没到 `RefsValidated` | 同上，注意 `Fatal signal` | IL2CPP 引用校验或探测崩了 |
| 进入游戏后若干秒崩溃 | `files/crash-last.txt`、`[hook] applied` 日志 | 托管层异常，或 hook 目标签名不符 |
| 游戏正常、Mod 全部失效 | `[boot]` 里的引用报告 | 引用校验失败即降级，属预期行为 |

内核与托管层都按"Mod 可以没有、游戏必须能跑"设计，所以**游戏还能玩但 Mod 不生效**通常是引用校验失败，日志里会写清缺哪一条。

## 3. 常见症状

| 症状 | 原因 | 动作 |
|---|---|---|
| 启动器显示"运行时版本不匹配" | 游戏更新或换包 | 重新导入（见 [运行时导入](runtime-import.md)） |
| 启动器显示"运行时不支持该构建" | Play 版 PairIP 构建 | 换无校验构建 |
| 导入进度卡住不动 | 存储空间不足 | 清出约 175 MB |
| `UnsatisfiedLinkError` | 运行时 native 与 ABI 不匹配 | `pylon.ps1 deploy` 后重装 |
| Mod 目录有 dll 但列表为空 | dll 与当前游戏版本不匹配（代理程序集不同代） | 用同版本 `interop/generated` 重编 Mod |
| 钩子挂上但游戏行为没变 | 目标方法在该版本里签名不同 | 看 `[hook]` 日志里的 slot 与方法名，核对 `refs/dump.cs` |
| ImGui 窗口不出现 | 覆盖层在等游戏首次 `eglSwapBuffers` | 进游戏主界面后再看；仍无则查 `[imgui]` 日志 |
| 屏幕效果报着色器编译失败 | GLSL 写错 | 报错里带 Mod 文件与行号 |

## 4. 快速自检

```powershell
pwsh -File src/csharp/tools/pylon.ps1 check      # 环境与缺件
pwsh -File src/csharp/tools/pylon.ps1 run         # 启动并抓日志
  adb shell run-as dev.pylon.loader ls files/runtime         # 导入结果
  adb shell run-as dev.pylon.loader cat files/crash-last.txt # 上次崩溃
```

`check` 报的缺件几乎覆盖了所有"编不过"与"跑不起来"的前置条件，先跑它再排查。

## 5. 提 issue 时请附上

- `pylon.ps1 check` 的输出
- `.work/logs/<时间>.txt` 里从 `[launcher]` 到失败点的一段
- 游戏包名与 `versionName`、设备型号与 Android 版本
- 若是内核崩溃：tombstone 的 backtrace（`/data/tombstones/`）
