# 内容管线：敌人旗子（Banner）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| `BannerSystem.NPCtoBanner` | `ModNpc.Banner`（这个 NPC 算到哪面旗子上） |
| `BannerSystem.BannerToItem` | `ModNpc.BannerItem`（这面旗子对应哪个物品） |
| `BannerSystem.BannerToNPC` | 反向：旗子 → 代表 NPC |
| `BannerSystem.GetKillCount` | 旗子击杀数（mod 旗子读侧表） |
| `BannerSystem.AddNPCKillBy` / `AddKill` | 击杀累计（mod NPC 记到侧表，不碰游戏数组） |
| `BannerSystem.IsNewBanner` / `SetNewBanner` / `ClearNewBanner` | "新旗子"标记 |
| `BannerSystem.Clear` | 清空侧表 |
| `WorldFile.LoadWorld` / `InternalSaveWorld` | 侧车存档 `<世界>.wld.banners.json` |
| `BannerNetPipeline`（通道） | 击杀数 + 可领取面数同步：服务器推增量、新客户端连上补全量；领取请求与结算 |

**已接**：从横幅 UI 领取 mod 旗子（`GetClaimableBannerCounts` 合并副本 + `RequestBannerClaim` 本地结算，见 3.7）。

**已接**：插在地上获得减伤 buff（`SceneMetrics.NPCBannerBuff` 按需扩长 + 附近扫描点亮，减伤数值走原版 `ItemID.Sets.BannerStrength`，见 3.9）。

**不接**：图鉴里的旗帜项。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/Banner/` 下两个文件：`BannerPipeline.cs`（十条游戏入口的分派 + 读档存档）、`BannerTable.cs`（三张映射表 + 击杀侧表）。

### 2.2 不扩游戏数组，改拦访问器

`BannerSystem` 的三张平行表（`killCount` / `claimableBanners` / `newBanner`）都是**静态**数组，长度等于原版 NPC 数。这三张**不扩**：它们的长度语义被 `Save`/`Load` 绑死（见第 2 节）。`SceneMetrics.NPCBannerBuff` 是另一回事——本版**扩了**（见 3.9），因为它是玩家侧的运行时表，不参与存档，换成更长的数组没有兼容风险。

这一版的选择是：**一张都不扩**。mod 旗子的编号（= 代表 NPC 的类型号）永远大于原版长度，所以本管线把**所有会按下标访问这些表的入口**都拦下来：

| 入口 | mod 编号时 |
|---|---|
| `GetKillCount` | 读侧表 |
| `AddKill` / `AddNPCKillBy` | 写侧表，不调 `orig` |
| `IsNewBanner` / `SetNewBanner` / `ClearNewBanner` | 读写侧表 |
| `NPCtoBanner` / `BannerToItem` / `BannerToNPC` | 读映射表 |

这套拦截对**静态三张表**是可枚举验证的：`BannerSystem` 里带旗子/NPC 编号的公开入口一共 10 个（`GetKillCount`、`AddKill`、`AddNPCKillBy`、`IsNewBanner`、`SetNewBanner`、`ClearNewBanner`、`NPCtoBanner`、`BannerToItem`、`BannerToNPC`、`AddClaimableBanner`），前九个全部拦下；`AddClaimableBanner` 只能从 `AddKill` 内部到达，而 `AddKill` 对 mod 编号根本不调 `orig`，所以也到不了。网络侧 `NetBannersModule.WriteKillCountUpdate(bannerId)` 同理——它由 `AddKill` 触发，mod 编号走不到那里，不会把 mod id 发给别的端。

**但 `SceneMetrics.NPCBannerBuff` 是另一回事**：它是**玩家侧的实例表**，按 NPC / 旗子编号读。我们同样不扩它，改成在它的三个公开读取入口上挡：

```
Player.HasNPCBannerBuff(int bannerType)
Player.GetBannerBuffEffect(NPC npc, out ItemID.BannerEffect effect)
Player.GetBannerBuffEffect(int bannerType, out ItemID.BannerEffect effect)
```

编号 `>= VanillaCounts.Npc` 一律直接答"没有 buff"（`effect = BannerEffect.None`）、不调 `orig`。

**这条拦截的边界要说清楚**：它保证的是"**这三个入口**不会被 mod 编号带到越界读"，不是"任何代码路径都不可能内联访问这张表"。方法体从 `refs/dump.cs` 里看不到，所以"某个内联读取点"这种可能性无法从证据上排除；真正闭死它只有一条路——把这张实例表也扩长（见第 2 节最后一条）。

### 2.3 数据布局

`BannerTable` 里三张 `Dictionary<int,int>`（NPC → 旗子、旗子 → 物品、物品 → 旗子）加一张 `Dictionary<int,string>`（旗子 → 存档键）。旗子数量少（每个 mod 几面），字典比稠密数组更省；热路径只有击杀那几次，不在每帧。

击杀侧表是 `int[]`，下标 = `旗子编号 - 原版 NPC 数`；`bool[]` 存"新旗子"标记。两者按最大旗子编号定长，安装期建一次。

### 2.4 建表时机

`Build` 在 `InstallPlan` 的 `Samples` 阶段、NPC 静态默认值与城镇 NPC 商店之后跑：`ModNpc.Banner` / `BannerItem` 通常写在 `SetStaticDefaults` 里，早于这个时机读只会读到 0。

建表时**作者错误直接抛**（不降级成警告）：`BannerItem > 0` 而 `Banner <= 0`、同一个旗子号被两个 NPC 指到不同物品、同一个物品被两面旗子抢——都指名到 `Mod/类名` 与键名。

### 2.5 存档

侧车存档 `<世界>.wld.banners.json`：`{"banners": {"Mod/类名": {"c": 击杀数, "f": 是否是新旗}}}`。只写非零项。读档时键找不到（mod 没装）就跳过；不清空已有计数，避免"读档把世界里的计数抹掉"。

**为什么另开文件而不塞进游戏存档**：`BannerSystem.Save` 的长度是原版 NPC 数，塞进去要么截断、要么改长度把原版读档器撑坏；参考实现的"临时缩表再换回来"能工作，但它在 `Save` 里改的是**全局静态数组**，一旦 `orig` 抛异常，数组就留在缩表状态——这是"用一次异常换来整局游戏的计数错位"。

### 2.6 状态归属

`BannerTable` 是静态表（按游戏编号索引、随进程存活）；`BannerPipeline` 实例只放分派去重集合。

## 3. 取舍理由

* **为什么不扩 `BannerSystem.killCount`？** 它是静态 `int[]`，扩表要在运行期替换静态字段；而 `BannerSystem.Save` / `Load` 的长度语义由游戏自己决定，表一长，存下来的世界档就可能与原版读档器对不上。相比之下"不动游戏表"只付出一次字典查找，却把这一类风险整体消掉。
* **为什么 `NPCBannerBuff` 可以扩，`killCount` 不行？** 前者是运行时表（每次场景扫描都会重填），换成长数组只影响本帧的读取；后者进存档，长度一变成品世界的读档就可能对不上。`SceneMetrics` 每个玩家一个实例，但真正被读的只有 `Main.SceneMetrics`，我们在 `Scan` 后置里当场补长，用完即走。

* **为什么击杀侧表按"旗子编号"而不是"NPC 类型"索引？** 多个 NPC 可能共用一面旗子（boss 的多个部位）；旗子才是计数单位，NPC 只是入口。
* **为什么 `Build` 放在 Samples 阶段而不是 Static？** `ModNpc.Banner` 的默认实现在基类里返回 0，作者通常要在 `SetStaticDefaults` 里赋值；而 NPC 的静态默认值在 `Samples` 阶段才跑完。

### 3.7 从横幅 UI 领取 mod 旗子

原版的领取是一条本地闭环：杀满 50 个 → `AddClaimableBanner` 记一面 → UI 的"领取"按钮读 `GetClaimableBannerCounts()` → 点了走 `RequestBannerClaim(banner, amount)` 发物品。mod 编号进不了那张静态 `ushort[]`，所以我们接两头：

* **计数侧**：`BannerTable.AddKill` 里跨过 50 的整数倍（`ClaimStep`）就把侧表 `_claimable` +1，并把 `BannerSystem.AnyNewClaimableBanners` 置 true——那个静态 bool 是公开字段，直接写，不用挂 `RefreshHasBannerAvailable`。
* **读取侧**：`GetClaimableBannerCounts` 后置——原版数组照抄进一份更长的副本（长度 = max(原版, 最大 mod 旗子号 + 1)），mod 旗子的格子填侧表里的可领数。**不换游戏的静态表**，只换这一次的返回值；UI 按数组长度扫，多出来的格自然就是 mod 旗子。
* **结算侧**：`RequestBannerClaim` 遇 mod 编号：单机/服务器当场 `Claim(banner, amount)`（不超过现有可领数）→ `QuickSpawnItem(旗子物品 × 领到的面数)` → 清"新旗"标；**多人客户端只发请求**（`[ActionClaimRequest, banner, amount]`），由服务器扣数、`Main.player[from].QuickSpawnItem` 发物品（物品本身走原版消息同步），再推一条 `[ActionClaimable, banner, 剩余数]` 给所有人。原版编号照走 `orig`。

**为什么客户端不能自己结算**：可领数在服务器手上（击杀数由服务器推），客户端本地扣数会和服务器对不上，硬发物品就是复制。请求里只有旗子号与面数，`from` 由收包方给——客户端没法替别人领。领到的面数与击杀数一样走侧车存档吗？不——可领数是击杀数的**衍生值**（击杀 ÷ 50 − 已领），重进世界按击杀数重算，不单独存。

**为什么放 `AddKill` 里算而不挂 `AddClaimableBanner`**：那个方法是 private，且只被原版 `AddKill` 内部调到——而我们对 mod 编号根本不调 `orig`，挂上也永远收不到。

### 3.8 击杀数同步

服务器是唯一权威：`AddKill` / `AddNPCKillBy` 的 mod 分支改完侧表就把 `BannerTable.Kills(banner)` **与同面旗子的可领取面数**一起推给所有客户端（`[ActionKill, banner, kills, claimable]`）；客户端的侧表被 `SetKills` / `SetClaimable` 直接覆盖（不做加法——重复包不会把计数算两遍）。

**为什么可领取面数也要推**：客户端自己的 `AddKill` 只在它本地击杀时加侧表，而击杀数是服务器覆盖式写下来的，两边不同步就会出现"界面显示可领、点了什么也没有"（服务器处 `Claim` 返回 0）。

`BannerNetPipeline` 单独成一个模块、装在 `NetPipeline` 之后：通道字节按模块安装顺序分配，跨设备一致，装早了拿不到 `NetPipeline.Instance`。

**为什么只推数字不推"谁杀的"**：计数是旗子维度的（多个 NPC 共用一面旗），和原版一样不记来源；新客户端补全量时也只搬"旗子号 + 计数"。

### 3.9 插在地上的旗子：减伤 buff

原版的链路是：场景扫描时方块自己点亮 `SceneMetrics.NPCBannerBuff[banner]`，`Player.HasNPCBannerBuff` / `GetBannerBuffEffect` 读它，减伤强度从 `ItemID.Sets.BannerStrength[npcType]` 取。mod 旗子方块没有原版那套 `NearbyEffects` 分发，所以本管线补三件事：

* **补表长度**：`NPCBannerBuff`（实例字段）与 `ItemID.Sets.BannerStrength`（公开静态数组）都换成长度 ≥ 最大旗子号的数组，原有内容照抄，新格子填 `BannerEffect.Reduced`（与原版普通旗子一致）。
* **找附近的旗子**：`Main.Update` 里每 20 帧扫一次玩家周围 40 格，按 `(方块类型, 样式)` 反查旗子号（建表时从物品的 `createTile` + `placeStyle` 记的），结果存成小数组。
* **点亮**：`SceneMetrics.Scan` 后置把这一批旗子号写成 true——必须是后置，原版每次扫描都会先清空这张表。

**两个读取入口**随后改回"直接问原版"：表已经够长，减伤数值不必我们复刻；只在表还没补上（越界）时兜底答 false。

**代价**：那次扫描是 81×81 格 = 6561 次方块读取，每 20 帧一次；对比原版自己每帧的扫描窗口，这个量级可以忽略。**没做的**：把 `ModBannerTile.NearbyEffects` 接到方块管线上（那需要逐格分发，而原版根本不调 mod 方块的 `NearbyEffects`）。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| `Banner` 与 `BannerItem` 只填了一个 | 安装期抛异常（指名 `Mod/类名` 与字段），世界还没进 |
| 两面旗子抢一个物品 | 安装期抛异常 |
| mod NPC 的旗子号与实际类型不一致 | 允许：`NPCtoBanner` 会把它折算到声明的旗子上 |
| 击杀 mod NPC | 侧表 +1，游戏数组不动 |
| 读档键对应不上（mod 没了） | 跳过那一条，其余照读 |
| 侧车存档损坏 | `TagJson` 记一次，按"没有存档"处理 |
| `GetKillCount` 被问到一个未登记的 mod 编号 | 返回 0 |
| 玩家在 mod 旗子附近 | 表补长后按原版点亮，减伤走 `BannerStrength`（默认 `Reduced`） |
| 表还没补上就有人问 mod 旗子号 | 兜底答 false（不崩、不越界） |
| `GetBannerBuffEffect` 收到 mod NPC / 旗子编号 | 表够长就交给原版；不够长返回 `None` |
| 领取请求超过现有可领数 | 只发现有的那几面（`Claim` 封顶） |
| 多人客户端点领取 | 发请求给服务器；服务器按现有可领数封顶结算、发物品，客户端只收新的可领数 |
| 客户端伪造领取请求（不存在的旗子号） | 服务器按 `IsMod` 丢弃 |
| 服务器可领数为 0 时收到请求 | 不发物品、不推包（`Claim` 返回 0） |
| 收到非 mod 旗子的同步包 | 丢弃 |
| 服务器重发同一个计数 | 覆盖式写入，不会重复累加 |
| 可领数与击杀数的关系 | 击杀 ÷ 50 − 已领；不单独存档，重进世界自动重算 |

## 5. 扩展方式

新增一个旗帜相关入口（例如将来要做"领取"）：在 `BannerPipeline` 里加一个钩子、在 `BannerTable` 里加一个访问器，把"mod 编号 → 侧表"的判断写在分派最前面即可；不需要动任何游戏数组。

## 6. 与其它管线的边界

* **NPC 管线**：NPC 类型号、`SetStaticDefaults`、掉落都归 `NpcPipeline`；本管线只读 `ModNpc.Banner` / `BannerItem`。
* **方块管线**：`ModBannerTile` 的贴图与多格形状归方块管线（它是 `ModTile` 的派生类）；本管线不碰 tile 字节。
* **网络管线**：击杀数不同步（本版没有横幅的同步通道）。
* **安装管线**：建表是 `InstallPlan` 里的一个 `Samples` 步骤。

### 这一版没接

1. ~~**从横幅 UI 领取 mod 旗子**~~：**已接（含多人）**——单机/服务器本地结算，客户端发 `ActionClaimRequest` 由服务器扣数发物品并回推可领数（见 3.7、3.8）；`RefreshHasBannerAvailable` 没挂——`AnyNewClaimableBanners` 在跨过 50 杀时由我们直接置 true，UI 的"有可领"提示由此点亮。
2. ~~**插在地上获得减伤 buff**~~：**已接**（见 3.9）——`SceneMetrics.NPCBannerBuff` 是实例字段、`ItemID.Sets.BannerStrength` 是公开静态数组，两者都直接换成更长的数组；附近扫描由本管线自己做。
3. ~~**多人同步**~~：**已接**——新模块 `BannerNetPipeline`（装在 `NetPipeline` 之后，独立通道），服务器每次改动推增量 `[1, banner, kills, claimable]`，新客户端连上时搭原版「世界数据」消息 7 的车补全量 `[0, banner, kills, claimable]`；领取走 `[2, banner, amount]` → 服务器 `[3, banner, claimable]`。客户端只收不报（`SetKills` / `SetClaimable`），消息里的旗子号不是 mod 旗子就丢。详见 3.7 / 3.8。
4. **图鉴（Bestiary）里的旗帜项**：**核对为接不上**——`refs/dump.cs` 里 11 个 Banner 相关类型没有一个进图鉴，图鉴元素只有 `NPCStatsReportInfoElement` / `ItemDropBestiaryInfoElement` 等几类，没有旗帜元素或过滤器；没有可挂的入口。
5. **`ModBannerTile` 的专属绘制细节**（`SetDrawPositions` 的 +2 偏移、藤蔓挂点）：方块管线本版没有逐格绘制入口。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 不变 | `ModNpc.Banner` / `ModNpc.BannerItem` 的签名与语义（0 = 关）不变，只是注释里的"未迁移"改成"由旗帜管线读取" |
| 新增约束 | 两个字段必须成对填写，否则安装期抛异常 |
| 新增 | mod 旗子会出现在横幅领取界面里（每 50 杀一面），点"领取"发对应物品 |
