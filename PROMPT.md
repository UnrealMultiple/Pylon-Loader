# 新会话提示词（复制粘贴用）

## 项目一句话

Pylon Loader 是一个**非官方的 Terraria 移动版 Mod 加载器**：外部 Kotlin 启动器、独立的 `:game`
宿主进程、C++ 内核 `libpylon.so`，把游戏运行时装进自己的进程，再由进程内 CoreCLR 跑 C# Mod
（API 参照 tModLoader 的使用习惯）。游戏运行时由用户从自己的正版导入；仓库与发行产物不含游戏二进制，
debug 也不再把 `refs/` 预置进 APK（`-PpylonStageRefs=true` 仅本地回退）。

## 环境事实（这台机器）

- 设备：`adb` 在 `D:\SDK\platform-tools\adb.exe`，序列号 `3b5f101`（arm64-v8a 真机，已连接）
- 目标游戏：`com.and.games505.Terraria` **1.4.5.8.6**（Unity 2021.3.56f2，无 PairIP，代码未混淆）
- 另一个已安装版本：`com.xd.terraria` 1.4.56002（国服，暂不作目标）
- 工具链：.NET SDK `10.0.401`；JDK 用 Android Studio 自带的 `D:\Android Studio\jbr`（`JAVA_HOME` 未设）；
  Android SDK/NDK 在 `D:\SDK`（`local.properties` 已写）；Il2CppDumper 在 `D:\INT\Il2CppDumper`
- 无可用 Python（`python` 是应用商店占位，直接跑会失败）
- 上游参考（只读）：本机桌面上的参考实现目录 —— **只借鉴技术路线与实测结论，不复制代码**
  （PolyForm Noncommercial 与本项目不兼容）

## 先读这些（按顺序）

1. `README.md` —— 项目现状、边界与构建入口
2. `docs/构建-从零开始.md` —— 三类二进制怎么补、报错对照、CI artifact
3. `docs/migration-map.md` —— 哪些管线已接、哪些没接、踩过的坑
4. `core/README.md` —— 内核目录与硬约束（改内核前必读）
5. `refs/README.md` —— 游戏 dump 的来源与版本纪律

代码注释里的 `docs/NN` 是早期设计笔记的编号，未随仓库发布；以这五份为准。

## 第一步提示词

```text
先按顺序读 README.md、docs/构建-从零开始.md、docs/migration-map.md、core/README.md、
refs/README.md，读完用三句话复述关键约束（加载形态、内核纪律、Mod 隔离），确认无误再动手。

背景：Pylon Loader 是非官方的 Terraria 移动版 Mod 加载器。技术路线是
Kotlin 启动器（外部 UI）→ :game 宿主进程（把游戏运行时加载进我们自己的进程）
→ C++ 内核 libpylon.so（按名解析 + Dobby detour + CoreCLR + ImGui）
→ C# Mod（Pylon.ModLoader，API 参照 tModLoader 使用习惯）。

当前状态（不要重复做已完成的）：
- 宿主链路、内核引用校验、CoreCLR、Harmony、ImGui 菜单、游戏原生配置屏已跑通；
- 已接内容管线：Item / Projectile / NPC / Buff / Mount / Prefix / Rarity / Recipe /
  Tile / Wall / Player / Mod 世界生命周期 / 网络 / 城镇 NPC / 音乐音效 / Config / Tooltip /
  TouchButton / GlobalItem / GlobalProjectile / GlobalNPC / GlobalBuff / GlobalTile；
- 未接：战斗命中/受伤、自然生成、Biome/SceneEffect 激活、Tile 自定义绘制 pass、地图颜色等；
- "用户导入即可运行"的 M5 debug 链路已跑通：启动器选已安装游戏并导入，宿主从导入目录加载 `.so`、挂载所选 APK 的 assets；release 打包与未来 Android 的可写 `.so` 执行限制仍未完成。

现在的任务：（在这里写本次要做的，例如"接 TileLoader，按 migration-map 的坑逐条实现"）
1. 只做这个任务，不顺手重构无关模块。
2. 先给设计（改哪些文件、hook 哪些游戏方法、表怎么扩、失败怎么隔离），再动手。
3. 表扩容一律走 `TypeTables` 的原生指针路径（`il2cpp_array_new` + 一次 memcpy）；
   不要逐元素写 il2cpp 数组，也不要用 interop 的数组包装去扩 `Nullable<T>[]`（会 SIGSEGV / TargetInvocationException）。
4. 一切按名字解析；解析失败停用对应 Mod 并写日志，不要静默。
5. 改动后跑 tools/check-env.ps1，再用 tools/pipeline.ps1 -Stages apk 打包；真机日志看
   adb logcat -s PylonLoader。
6. 不改 interop/harmonysupport，不引入第三方加载器代码。
```

## 后续阶段的提示词

### Tile / Wall

```text
开始接 TileLoader / WallLoader。目标：ModTile / ModWall 的占号、贴图、表扩容、框架绘制、
挖掘/相邻判定与存档 ID 在真机上跑通。
先给我：要 hook 的游戏方法清单（从 refs/dump.cs 抄精确签名）、每一处需要 cloneResized 的表、
以及"mod tile 被原版编译期上限挡住"时的补画方案。不要先写代码。
```

### Player / 世界生命周期

```text
Player 已接每玩家懒实例与 ResetEffects/Update/存读档侧车；世界生命周期已并入 Mod。
目标：补齐 ModPlayer 的命中/射击/绘制/跳跃分组，以及 Mod 的世界生成任务（ModifyWorldGenTasks/GenPass）。
先给我：每组的 hook 清单（refs 精确签名）、联机客户端要跳过的分支、失败隔离点。
```

### 网络 / 存档

```text
开始接网络与持久化。目标：ModPacket（自定义通道）、Item/NPC 的 SendExtraAI/ReceiveExtraAI、
ModPlayer/Mod 的侧车存档，以及断线/重连时的清理。
手机版约束：数据包先于原生消息到达同一条 TCP 流；PvP 只传 type/prefix；先看 migration-map 第 4 节。
先给协议与失败降级设计，不要直接改代码。
```

### M5 发行形态（用户导入）收尾

```text
M5 的 debug 链路已经跑通：启动器选已安装游戏并导入关键文件；宿主从 filesDir/runtime 加载 .so，
把目录追加进 classloader 的 native 搜索路径，并用 AssetManager.addAssetPath 挂载所选游戏 APK 的
assets；debug APK 已不含游戏 .so / assets/bin/Data。现在做 release 收尾：把 CoreCLR/BCL/代理程序集
也放进 release 构建图、正式签名，并评估 Android 未来版本对“从可写目录执行 .so”和隐藏 addAssetPath
的限制。先给构建图与回退方案，再改代码。
```
