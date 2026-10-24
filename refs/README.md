# refs — 本地游戏 dump（游戏文件不进 git）

来源：从已安装的 `com.and.games505.Terraria` **1.4.5.8.6** 的 `base.apk` 解出（arm64-v8a）。
`libil2cpp.so` 与 `global-metadata.dat` **必须来自同一构建**，不要混版本。

| 文件 | 说明 |
|---|---|
| `lib/arm64-v8a/libil2cpp.so` | 游戏托管代码的原生产物 |
| `lib/arm64-v8a/libunity.so` | Unity 2021.3.56f2；`DT_NEEDED` 无 `libpairipcore.so` |
| `lib/arm64-v8a/libmain.so` | 原始未注入（`DT_NEEDED` = liblog/libm/libdl/libc） |
| `assets/bin/Data/Managed/Metadata/global-metadata.dat` | 类型/方法/字段名与字符串字面量 |
| `assets/bin/Data/boot.config`、`ScriptingAssemblies.json` | 启动配置与程序集清单 |
| `classes.dex`、`AndroidManifest.xml` | Java 侧（确认无 `com/pairip/*`） |
| `dump.cs` | Il2CppDumper 输出（放在 `refs/dump.cs`）。翻译 API 类时用它查精确签名与"某个类型是否存在"（移动版与桌面版命名空间/成员经常不同，如 `ContentSamples` 在 `Terraria.ID`） |

重新拉取：

```bash
adb shell pm path com.and.games505.Terraria
adb shell "cd /data/local/tmp && unzip -o <base.apk> 'lib/arm64-v8a/*' 'classes.dex' 'AndroidManifest.xml' 'assets/bin/Data/*' -d probe"
adb pull /data/local/tmp/probe .
```

这些文件**不属于本仓库的授权范围**（Terraria 版权归 Re-Logic/505），只作本地分析，永不提交、永不随 Release 分发。`refs/` 里的游戏文件都被 `.gitignore` 排除；只有本说明（`refs/README.md`）进 git。

本机开发时，这里最重要的是生成 `interop/generated/` 与查精确签名。Gradle **默认不再**把
`assets/bin/Data` 与 `lib/arm64-v8a/*.so` 打进 APK；运行时由启动器从设备上已安装的游戏导入，
宿主从导入目录加载 `.so` 并挂载所选游戏 APK 的 assets。只有做“不经过导入”的本地回退时才用
`-PpylonStageRefs=true` 预置 debug 变体；release 构建图里不应出现任何游戏二进制。
