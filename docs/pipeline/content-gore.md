# 内容管线：血腥粒子（Gore）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Gore.NewGore` | `OnSpawn`（粒子已经生成、位置与速度都定了） |
| `Gore.Update` | `Update`（返回 false = 作者接管这一粒，游戏不再跑原版更新） |
| `Gore.GetAlpha` | `GetAlpha`（返回 null = 用原版算出来的颜色） |
| 安装期（`InstallPlan` 的 `Static` 阶段） | 扩 `TextureAssets.Gore` 与 `GoreID.Sets` 的几张大表；逐类型 `SetStaticDefaults` |
| 安装期（`Surface` 阶段） | 贴图写进 `TextureAssets.Gore[类型号]`（由安装管线统一做） |

**不接**：`DrawBehind` / `PreDraw` / `PostDraw`（本版没有逐粒绘制入口）、`GoreID.Sets` 的逐类型取值（只扩容不写值）、`UpdateType`（"借用原版 AI"）、网络同步（纯表现，两端各自跑）、`Gores/*.png` 的自动发现。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/Gore/` 下两个文件：`GorePipeline.cs`（三条游戏入口的分派、安装期扩表、SetStaticDefaults）与 `GoreTable.cs`（类型号与位集，按类型号偏移索引）。

内容类型 `ModGore` 在 `Content/ModGore.cs`：它本来就是作者 API（今天是一个只有 `GetTypeByName` 的静态类），这一版改成 `ModType` 的派生类，钩子以 `virtual` 挂在上面。

### 2.2 类型号从原版表长接

原版 `GoreID` 里没有编译期常量 `Count`，所以起点只能运行期读：`TypeTables.GoreVanilla` 捕获 **`TextureAssets.Gore` 第一次可读时的长度**，之后一直用这个值。缓存只在读到正数时才写，避免"表还没建就把 0 当起点"。

类型号 = `GoreVanilla + 注册序`。发号在 `Refresh`（每次 Mod 注册后）做，与方块实体同一套理由：作者的 `SetupContent` 里就要能拿到 `Type`。

### 2.3 扩表

`TextureAssets.Gore` 与 `GoreID.Sets.*` 是**同长度的平行表**，游戏在 `Gore.Update` 里直接按 `gore.type` 下标读它们。少扩一张，mod gore 一动就抛出界。

所以这一版把 gore 加进 `TypeTables` 的种类清单（`TableKind.Gore`）：清单里写 `TextureAssets` / `GoreID.Sets` / `ChildSafety`，扩表器把这三个类里**长度等于原版表长**的静态数组统一扩到新长度。实例照旧注册进 `TypeTables.Active`，于是"游戏重建表后补扩"与"补扩后重写贴图"两条维护路径不用另写。

扩表在 `InstallPlan` 的 `Static` 阶段、`Build` 里同步做一次：gore 的数组只有几张，一次 memcpy 是微秒级，不值得像物品表那样分帧。

### 2.4 分派

* `NewGore`：先 `orig` 拿到下标，再核 `Main.gore[index]` 还活着且 `type` 没被改（原版可能因为粒子槽满而没生成），最后调作者的 `OnSpawn`。
* `Update`：本类型且覆写了 `Update` 才调；返回 false 就整段跳过 `orig`（作者接管）。抛异常按"返回 true"处理——让原版接着更新，粒子至少会自己消失，不会在屏幕上定住。
* `GetAlpha`：本类型且覆写了才调；返回 null（或抛异常）就走原版。

判定一律是 `GoreTable.MasksOf(type).Has(HookId.X)`：位集在发号时按 `ModGore` 为基类算好，热路径上没有字典、没有字符串。

### 2.5 贴图

不自己写 `TextureAssets.Gore`：`ModGore.TexturePath` 默认 `assets/gores/<类名>.png`，安装管线的 `LoadTextures` / `WriteTextures` 负责上传与写槽，`InstallSteps.TableFor` 里加一条 `ModGore => TextureTable.Gore` 即可。好处是"扩表后重写贴图"这条维护路径已经存在（`TypeTables` 的 `regrown` 回调就是 `WriteTextures`），不用为 gore 再写一遍。

### 2.6 状态归属

`GoreTable` 是静态表（按类型号索引、随进程存活，与 `WallTable` 同一约定）；`GorePipeline` 实例只放分派去重集合。

## 3. 取舍理由

* **为什么复用 `TypeTables` 而不是自己扩 `TextureAssets.Gore`？** 平行表不止一张：只扩贴图表、不扩 `GoreID.Sets.DisappearSpeed`，第一帧 `Gore.Update` 就会越界抛异常。`TypeTables` 的"扫同类静态数组"本来就是为了这种"一扩必须全扩"的表设计的，gore 只是再多一个种类。
* **为什么起点要运行期捕获？** 原版 `GoreID` 没有 `Count` 常量；写死一个数字（比如 1000）在版本升级后会与真表长错位——错位比越界更糟，它会**悄悄覆盖**原版的某一粒 gore。
* **为什么 `Update` 抛异常按"返回 true"？** 这里的 true 意味着"继续走原版"，粒子会自然淡出；按 false 处理会让粒子永久停在屏幕上（每次更新都抛、每次都吞）。宁可让作者的动作没生效，也不要在屏幕上留垃圾。
* **为什么贴图交给安装管线，而不是像参考实现那样自己写？** 自己写就要自己管"扩表之后重写"与"贴图还没上传"两个时序；安装管线已经有这两条路径（`Maintain` 里的 `regrown` 回调），复用它就少一份会漂移的实现。
* **为什么逐粒绘制仍不接**：原版的粒子绘制是 `Main.DrawGore` / `Main.DrawGoreBehind` 两个整批循环（`for` 一遍 `Main.gore`，自己 `spriteBatch.Begin/End`），没有"画某一粒"的可挂钩入口。要逐粒分派就得把整段批量循环连同取色、帧矩形、旋转、缩放、SpriteEffects 一起搬进我们的代码；参考实现的 `GoreHooks.cs` 里也没有这一段（它只有 `OnSpawn` / `GetAlpha` / `Update`），没有可抄的写法。搬一份就等于把原版绘制逻辑固定在我们这边，原版一改就漂移。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| `OnSpawn` 抛错 | 记一条（同类型同钩子只报一次），粒子照原版继续飞 |
| `Update` 抛错 | 记一条，按"返回 true"处理，原版更新继续 |
| `GetAlpha` 抛错 | 记一条，用原版颜色 |
| `UpdateType` 指到一个不存在的编号 | 原版更新按该编号查表：查不到就当普通粒子跑完（不崩，表现为这粒不动）；编号只在本次更新内有效，之后换回自己的 |
| 贴图没上传 / 路径写错 | 该类型的槽位是空的；生成这种粒子时游戏绘制会走空贴图路径（不崩，但不显示）——安装日志里有"贴图跳过" |
| 扩表失败（表还没建） | `Build` 记一条，类型号照发；下一轮 `Maintain` 还会试（`TypeTables` 的 pending 机制） |
| 类型号溢出 `short` | 不会：gore 类型以 `int` 传递，扩表按实际数量 |
| `GetTypeByName` 找不到 / 重名 | 返回 0（与原静态类语义一致），不抛异常 |

## 5. 扩展方式

新增一个 gore 钩子：`HookId` 末尾追加 → `ModGore` 加同名 `virtual` → 在 `GorePipeline` 对应入口分派。类型表与位集不用改结构。

新增一类"和游戏表等长、一扩必须全扩"的内容：在 `TypeTables` 里加一个 `TableKind` 与类清单，然后在自己的管线里 `new TableGrower(kind, 新长度)`——扩表与维护都不用另写。

## 6. 与其它管线的边界

* **方块/NPC 管线**：`KillTile` / `HitEffect` 里生成 gore 是作者的调用（`Gore.NewGore`），本管线不替谁生成粒子。
* **墙壁管线**：`ModWall.CreateDust` 走尘土管线；gore 与 dust 是两套编号空间，互不引用。
* **安装管线**：撑表、上传贴图、写槽位都在 `Install`；本管线只负责"扩到多长"与 `SetStaticDefaults`。
* **世界/网络管线**：粒子不同步，存档里也没有 gore。

### 这一版没接

1. **`DrawBehind` / `PreDraw` / `PostDraw`**：本版绘制是 `Main.DrawGore` / `Main.DrawGoreBehind` 里的整批循环，没有逐粒入口；参考实现同样没接，见 4 节的"为什么逐粒绘制仍不接"。
2. **`GoreID.Sets` 的逐类型取值**：只扩容、不写值。作者要让自定义 gore 更快消失，得在 `SetStaticDefaults` 里自己写 `GoreID.Sets.DisappearSpeed[Type]`。
3. ~~**`UpdateType`（借用原版 AI）**~~：**已接**。`ModGore.UpdateType` 返回原版编号时，`GorePipeline.Update` 在作者 `Update` 返回 true（没接管）之后把 `gore.type` 临时换成目标编号、跑原版更新、再换回来；换回来放在 `finally` 里，原版更新抛异常也不会把编号留在别人身上。0 = 不借。
4. **`Gores/*.png` 自动发现**：必须写一个 `ModGore` 派生类；只放图片不会产生类型。
5. **`ChildSafety.SafeGore` 的取值**：跟着扩长，但不开"儿童安全"过滤（原版默认值）。
6. **网络同步与 `ModGore` 的存档**：粒子是纯表现、不落盘。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| `ModGore` 由静态类改为 `abstract class ModGore : ModType` | 作者从"没法派生"变成可以写 `class MyGore : ModGore`；`GetTypeByName(string)` 仍是静态方法、签名不变，老的调用点照旧编译 |
| 新增 `virtual void OnSpawn(Gore gore)` | 粒子生成之后一次 |
| 新增 `virtual bool Update(Gore gore)` | 每帧；返回 false = 接管 |
| 新增 `virtual Color? GetAlpha(Gore gore, Color lightColor)` | null = 用原版颜色 |
| 新增 `virtual int UpdateType` | 0 = 不借；非 0 = `Update` 返回 true 时借那个原版 gore 的每帧动作（换编号 → 原版更新 → 换回来） |
| 新增 `override string TexturePath` 默认 `assets/gores/<类名>.png` | 与 tiles / hairs / emotebubbles 的路径约定一致 |
| 不变 | `ModGore.GetTypeByName(string name)` 的名字、参数与"没有就返回 0"的语义 |
