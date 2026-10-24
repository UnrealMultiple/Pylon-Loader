# 运行时导入

游戏本体不随 APK 分发。启动器把用户**已安装游戏**里的关键文件复制进自己的私有目录，逐文件校验后交给宿主。本文说明这份"导入"的布局、清单与门禁。

## 1. 为什么要导入

| 方案 | 问题 |
|---|---|
| 把游戏 `.so` 与 `Data` 打进我们的 APK | 分发他人版权文件，不可接受 |
| 运行期直接从游戏 APK 路径加载 | 游戏私有目录不可读，`mmap` 与 assets 挂载都要额外处理 |
| **复制进自己的私有目录再加载** | 只依赖用户自己的安装，路径稳定、可校验、失效可重导 |

导入只在用户点"导入运行时"时发生，而且只读：不改动游戏 APK，也不修改游戏数据。

## 2. 目录布局

```text
/data/user/0/dev.pylon.loader/files/
  runtime/
    lib/                        libc++_shared.so、libmain.so、libunity.so、libil2cpp.so
    Data/                       游戏 assets/bin/Data（约 170 MB）
    import-manifest.properties  清单：来源包 / 版本 / ABI + 每个文件的大小与 sha256
    .deployed                   宿主解包版本与指纹标记
```

`lib/` 与 `Data/` 的名字由 `RuntimeLayout` 统一定义，启动器、宿主、内核三侧共用同一份常量，避免各写一份字符串。

## 3. 清单

`import-manifest.properties`（`ImportManifest`）：

| 键 | 含义 |
|---|---|
| `format` | 清单格式版本 |
| `game.package` / `game.versionCode` / `game.versionName` | 来源游戏的包与版本 |
| `game.abi` | 导入时的 ABI（当前只有 `arm64-v8a`） |
| `game.sourceDir` | 来源 APK 路径 |
| `game.pairip` | 该构建是否链接 `libpairipcore.so`（命中即拒绝，见第 5 节） |
| `imported.at` | 导入时间 |
| `native.<name>` / `data.<path>` | `大小\|sha256` |

清单是宿主的放行依据。清单缺失、格式错误、包不符、ABI 不符、版本不在支持线、文件大小不符，任一条都算"未就绪"：启动器会要求重新导入，而不是带着半套文件启动。

## 4. 导入与校验流程

1. 读取已安装游戏的 `PackageInfo`（包名、`versionName`、`sourceDir`）。
2. 版本门禁：`versionName` 前三段必须是 `1.4.5`。小版本放开，游戏打补丁不用改代码。
3. 打开 APK：解出 `lib/arm64-v8a` 下的四个库与 `assets/bin/Data`。
4. **PairIP 门禁**：解析解出的 `libunity.so` 的 `DT_NEEDED`，依赖 `libpairipcore.so` 即中止（见第 5 节）。
5. 边写边算 sha256（单遍），进度按已写字节回调。
6. 写清单，落 `.deployed` 标记。

之后每次启动，只有"清单与文件比对通过"才放行；`.deployed` 的指纹（游戏包 `lastUpdateTime` + 资产版本）一致时跳过重复解包，这是 warm start 快的主因。

## 5. PairIP：不绕过，直接拒绝

Google Play 版 `libunity.so` 链接 `libpairipcore.so`，重要字符串与代码在运行期由它解密，并强绑定包名与签名。搬进我们的进程既跑不起来，也不该去解。

判据是**链接关系**，不是"APK 里有没有这个文件"：无校验构建的 APK 里也可能躺着 `libpairipcore.so`，没人链接它就无所谓。内核取的是 `DT_NEEDED`：

```text
libunity.so 的 DT_NEEDED（无校验构建）
  libmain.so libandroid.so liblog.so libz.so libEGL.so libm.so libdl.so libc.so
```

拦截点三处，覆盖"新导入""旧清单""直接启动"：

| 层 | 时机 | 行为 |
|---|---|---|
| 导入 | 解出原生库后、写 `Data` 之前 | 中止，提示换构建 |
| 宿主 | 装载运行时之前 | 拒绝启动，不再注入 dex、不再装载 `.so` |
| 启动器 | 状态展示 | 显示"运行时不支持该构建" |

被拒绝时的建议固定为：换无校验构建（亚马逊版 / 整合版）后重新导入。

## 6. 版本策略

| 情况 | 结果 |
|---|---|
| `1.4.5.x`（含开发基线 `1.4.5.8.6`） | 支持 |
| `1.4.5.8.6` 的 Play 版（PairIP） | 拒绝，见第 5 节 |
| `1.4.6.x` / 中文版 `1.4.56002` | 拒绝（不在 `1.4.5` 线） |

导入结果与版本强绑定：`libil2cpp.so`、`global-metadata.dat` 与代理程序集必须来自同一版本，换版本请整组重做（见 [构建](build.md) 第 2 节）。

## 7. 失效与恢复

| 症状 | 原因 | 处理 |
|---|---|---|
| "运行时版本不匹配" | 游戏更新过 | 重新导入 |
| "运行时来自另一个游戏" | 换过目标包 | 重新导入 |
| 导入中途失败 | 存储不足（需约 175 MB 空闲） | 清理后重试 |
| 启动即回桌面、无 Mod 日志 | 清单与文件不一致 | 重新导入 |

导入是唯一会写 `files/runtime` 的路径，重新导入先清空目标目录，不会叠加残留。
