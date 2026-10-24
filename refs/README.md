# refs — 本地游戏 dump（不进 git）

从**你自己安装的正版** `com.and.games505.Terraria` 解出（开发基线 `1.4.5.8.6`，arm64-v8a）。`libil2cpp.so` 与 `global-metadata.dat` 必须来自**同一次安装**。

| 文件 | 用途 |
|---|---|
| `lib/arm64-v8a/libil2cpp.so` | 游戏托管代码的原生产物，生成代理程序集要用 |
| `lib/arm64-v8a/libunity.so` | Unity 2021.3.56f2；`DT_NEEDED` 里没有 `libpairipcore.so` 才可用 |
| `lib/arm64-v8a/libmain.so` | Unity 的 Java 侧装载入口 |
| `assets/bin/Data/Managed/Metadata/global-metadata.dat` | 类型、方法、字段名与字符串字面量 |
| `assets/bin/Data/boot.config`、`ScriptingAssemblies.json` | 启动配置与程序集清单 |
| `classes.dex`、`AndroidManifest.xml` | Java 侧信息（可确认有无 `com/pairip/*`） |
| `dump.cs` | Il2CppDumper 输出，查签名与"某类型是否存在" |

## 用途边界

- 只用于**生成本机代理程序集**与查 API 签名，不参与打包，不随 Release 分发。
- 不提交到 git（见 `.gitignore`），也不要上传到任何公开位置。
- 换游戏版本时整组替换，不要与旧版本文件混用。

生成方式与后续步骤见 [构建](../docs/build.md) 第 2 节。
