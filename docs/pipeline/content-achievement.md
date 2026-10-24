# 内容管线：成就（Achievement）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（Static） | 建 Achievement（分类默认 Slayer，None 门）→ 作者 SetStaticDefaults 里加条件 / SetCategory → Register / RegisterIconIndex → 全部注册完重读一次成就存档 |
| 条件全部完成那一下 | OnCompleted(achievement) |
| 游戏成就通知 | AchievementsHelper 静态事件：OnItemPickup ×2 / OnItemCraft / OnTileDestroyed（门 gameMenu+_isMining）/ OnNPCKilled / OnProgressionEvent（门 netMode!=2） |
| 菜单首开 | GUIAchievementsMenu.Open 清一次成就列表缓存（每菜单实例一次） |
| 每帧绘制 | 成就菜单图标 + 解锁弹窗图标换作者贴图；宽 ≤132 的贴图逐帧重映射源矩形；未完成按 Hidden 显示 "???" |

**不接**：成就存档的读写与云同步（全归游戏成就管理器，管线只在注册完后重读一次入口）；图标贴图不做尺寸校验拒绝——宽 ≤132 与 >132 的二选一就是全部规则，约定外的尺寸按宽度就近处理，不会因此拒绝安装。

## 2. 我们的设计

### 2.1 关键发现：Register 在这个构建里是公开的

之前把成就判成"接不上"是错的。dump.cs 里 AchievementManager 的公开方法包含：

```
public void Register(Achievement achievement)
public void RegisterIconIndex(string achievementName, int iconIndex)
public void RegisterAchievementCategory(string achievementName, AchievementCategory category)
public Achievement GetAchievement(string achievementName)
```

也就是说**不需要动私有字段**就能注册成就。这一条是本管线能交付的全部原因。

### 2.2 两个文件

Pipelines/Achievement/AchievementTable.cs（表 + 键）、Pipelines/Achievement/AchievementPipeline.cs（建、注册、完成回调、菜单与弹窗图标、重映射）。

### 2.3 键就是存档键

成就用 <mod 名>/<类名> 作为 Achievement.Name。游戏的成就存档按这个名字存进度，所以：

* 改类名 = 老进度丢失（和物品/方块改名是同一类代价）；
* 不写 Achievement.Id（游戏自己发号），避免和原版号段冲突。

### 2.4 条件在作者手里

SetStaticDefaults 在 Achievement 建好（含分类门）之后、Register 之前调用，作者在里面用 AddCondition（含泛型重载）/ AddIntCondition / AddItemCraftCondition / AddItemPickupCondition / AddNPCKilledCondition / AddTileDestroyedCondition（参数是 int，管线内部收窄到游戏的 short/ushort）；条件不在 SetStaticDefaults 期加会抛 InvalidOperationException。

顺序是硬要求：**先建成就、再加条件、最后注册**。反过来的话，条件不会参与追踪器选择。

### 2.5 完成回调

挂 Achievement.OnConditionComplete：进函数前记 IsCompleted，调 orig 之后再读一次，从"没完成"变成"完成"的那一下才派 OnCompleted。这样每次条件更新不会重复派。

### 2.6 存档重读

全部成就注册完后 manager.Load(_savePath, _isCloudSave) 重读一次存档，让 mod 成就接上已保存的进度；重读前把每条原版成就的 _completedCount 记下来、读完写回，避免原版计数翻倍。

### 2.7 图标贴图：两种布局与逐帧重映射

换贴图有两条路径：菜单在 `GUIAchievementsMenu.AchievementDraw` 里把 `AchievementsIcons` 换成作者贴图、orig 后还原；弹窗在 `DrawInGame` / `DrawInNotificationsArea` 里把弹窗的 `_achievementTexture` 换成作者 Asset（换上后一直有效，直到下一条弹窗再换）。贴图本身按**宽度**自动走两种布局之一：

* **宽 >132（含 592×64 单行表）**：不做任何拦截。游戏按原版网格数学取格（x=Index×66 解锁、x=528 锁定），作者的格子按同样的数学摆就自然对齐，Index 可用 0..7。
* **宽 ≤132（2列×N行）**：逐帧重映射源矩形。约定：第 Index 行左格 (0, Index×66) 是该成就的解锁图标，锁定图标固定在 (66, 0)；行数随 Index 走，不受 8 格上限约束。

重映射的机械：SpriteBatch.Draw 的四个带源矩形重载挂 On 钩，只在「本格绘制窗口」内生效——菜单/弹窗钩子在调 orig 前记下作者贴图指针，finally 里清除；钩内先比贴图指针，不是窗口贴图就 orig 直通。窗口内把游戏算好的源矩形 X/Y 改写到 2列×N行：

```
列 = X / 66；行 = Y / 66；icon = 行×8 + 列%8
新 X = (列 ≥ 8 ? 66 : 0) + X % 66
新 Y = icon × 66 + Y % 66
```

三条守卫：源矩形为 null 的绘制（整图绘制）原样放行；ref 重载改写前存快照、orig 后恢复，避免改写经引用回写到游戏的字段；去重守卫记录最近一次改写的输出坐标，输入与它相同就直接放行——四个重载之间存在链式委托（外层重载的原生实现内部会再调另一个已挂钩的重载），没有这条守卫，已改写的矩形会被内层再算一遍、推到错误的下一格。同一窗口里输入相同的两次独立绘制不受去重影响，仍各自改写。

**按需武装**：四个 On 钩不随成就安装挂。SpriteBatch.Draw 是全游戏最热的路径，全局常驻的 interop 钩子会让每一笔绘制都付一次代理开销，而重映射只对「存在宽 ≤132 作者贴图」的 mod 有意义。所以钩子在第一次遇到这种贴图时才挂（挂载发生在 orig 之前，当次绘制即被覆盖），之后常驻；没有这种贴图的存档，绘制路径一根钩子都不装、零变化。ref 重载在 orig 后恢复源矩形，是为了不让改写值经引用参数泄漏回游戏持有者（弹窗的源矩形是它的字段，被污染后下一帧会从错误的输入继续算）。

## 3. 取舍理由

* **为什么不用私有字段？** 注册走公开 API（见 2.1）；菜单首开清 `_achievements` 缓存是唯一的私有字段写（列表是缓存，置空让游戏重建）。
* **为什么按宽度二选一？** 游戏只会按原版网格数学算源矩形。宽 >132 的贴图按同样的数学摆格子就能对齐，不需要拦截；宽 ≤132 的贴图摆不下这个数学，只能把每一笔的源矩形改写到行列表上。两条路互不干扰：宽贴图的绘制路径一个字节都没变，窄贴图的改写只在菜单/弹窗的本格窗口内发生。
* **为什么改写只动 X/Y？** 游戏给出的源矩形宽高（64×64）就是图标的目标尺寸，作者布局的格子也是 66 步进，宽高不需要动；改得越多，与游戏后续版本的网格数学耦合越深。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| new Achievement 失败 | 这一条跳过，其余照常注册 |
| SetStaticDefaults 抛异常 | 记一次，仍然注册（只是没有条件） |
| 某个条件挂不上 | 记一次，跳过该条件 |
| Main.Achievements 还没建好 | 记警告，这条这局不注册（不崩） |
| Register 抛异常（重名等） | 记一次，这条不生效 |
| OnCompleted 抛异常 | 记一次，成就算完成 |
| 重映射钩子安装失败 | 记一次；图标按游戏原版网格取格（宽 ≤132 的贴图可能错格），成就与进度不受影响 |
| 弹窗贴图解析抛异常 | 记一次；本格不重映射（换贴图照旧，游戏按原版网格取格） |

## 5. 扩展方式

接菜单图标：`GUIAchievementsMenu.AchievementDraw` 已挂；要加显示逻辑（例如完成/未完成换图）在同一个格子里加绘制即可。
接隐藏文本：菜单文本在 AchievementDraw 里按 Hidden 换 "???"（`Language.GetText("???")`），画完恢复。
选图标布局：宽 >132 的贴图用 592×64 单行表（x=Index×66 解锁、x=528 锁定，Index 0..7）；宽 ≤132 的贴图用 2列×N行（第 Index 行左格解锁、(66, 0) 锁定），两种布局自动识别，不需要任何注册。

## 6. 与其它管线的边界

* **物品 / NPC / 方块**：它们的通知由游戏发到 AchievementsHelper；本管线转发事件，不重复拦截游戏内部。
* **本地化**：成就名字/说明包成带默认文本的 LocalizedText（键 `Mods.<mod>.Achievements.<名>.FriendlyName/.Description`），有语言条目就用条目。
* **玩家**：成就进度是账号级的，和玩家存档无关。
* **场景效果 / 菜单**：无关。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 生效 | Achievement 属性在 SetStaticDefaults 之前就已建好，条件可以直接挂 |
| 生效 | AddCondition 系列方法真的会进游戏的条件表；分类改用作者 SetStaticDefaults 里 `Achievement.SetCategory(...)` |
| 生效 | OnCompleted 在从未完成变成完成的那一下调用 |
| 生效 | 菜单图标：换作者贴图；宽 ≤132 按 2列×N行 取格（Index 行数无上限），宽 >132 按 592×64 单行表取格（Index 0..7）；未完成画锁定格 + Hidden "???" |
| 生效 | 解锁弹窗（DrawInGame / DrawInNotificationsArea）：换作者贴图，重映射与菜单同一套规则 |
| 生效 | 成就存档重读：mod 成就进度跨启动保留 |
| 生效 | 五条通知走 AchievementsHelper 静态事件（击杀/拾取/制作/拆方块/进度事件） |
| 生效 | 成就菜单首开清一次列表缓存，mod 成就会出现在菜单里 |
