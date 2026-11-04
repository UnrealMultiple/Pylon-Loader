# 内容管线：成就（Achievement）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（Static） | 建 Achievement → 作者 SetStaticDefaults 里加条件 → Register / RegisterIconIndex / RegisterAchievementCategory |
| 条件全部完成那一下 | OnCompleted(achievement) |
| 每帧 | 无 |

**不接**：成就菜单的图标绘制与"???"隐藏文本、OnNPCKilled / OnItemPickup / OnItemCraft / OnTileDestroyed 四条自动通知、成就存档的读写与迁移、AchievementCategory 的分页排序。

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

Pipelines/Achievement/AchievementTable.cs（表 + 键）、Pipelines/Achievement/AchievementPipeline.cs（建、注册、完成回调）。

### 2.3 键就是存档键

成就用 <mod 名>/<类名> 作为 Achievement.Name。游戏的成就存档按这个名字存进度，所以：

* 改类名 = 老进度丢失（和物品/方块改名是同一类代价）；
* 不写 Achievement.Id（游戏自己发号），避免和原版号段冲突。

### 2.4 条件在作者手里

SetStaticDefaults 在 Achievement 建好之后、Register 之前调用，作者在里面用契约已经提供的 AddCondition / AddIntCondition / AddItemCraftCondition 等方法往条件表里塞条件；管线把它们挂到 Achievement.AddCondition 上。

顺序是硬要求：**先建成就、再加条件、最后注册**。反过来的话，条件不会参与追踪器选择。

### 2.5 完成回调

挂 Achievement.OnConditionComplete：进函数前记 IsCompleted，调 orig 之后再读一次，从"没完成"变成"完成"的那一下才派 OnCompleted。这样每次条件更新不会重复派。

## 3. 取舍理由

* **为什么不用私有字段？** 见 2.1：公开 API 够用，没必要碰 _achievements。
* **为什么不做菜单绘制？** 菜单里 mod 成就的图标要按作者给的 592×64 图标表取块（Index 列/行），还要处理 Hidden 的 "???"。这需要挂 GUIAchievementsMenu 的绘制入口并改写贴图来源，属于单独一块；本版先保证"成就真的存在、条件真的在跑、完成真的有回调"。
* **为什么不做存档迁移？** 参考实现那套（重 Load 后恢复原版计数）要碰成就文件的读写，风险是双写；本版交给游戏自己的存档，mod 成就的进度也会跟着存，只是没有"mod 卸载后清理"。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| new Achievement 失败 | 这一条跳过，其余照常注册 |
| SetStaticDefaults 抛异常 | 记一次，仍然注册（只是没有条件） |
| 某个条件挂不上 | 记一次，跳过该条件 |
| Main.Achievements 还没建好 | 记警告，这条这局不注册（不崩） |
| Register 抛异常（重名等） | 记一次，这条不生效 |
| OnCompleted 抛异常 | 记一次，成就算完成 |

## 5. 扩展方式

接菜单图标：`GUIAchievementsMenu.AchievementDraw` 已挂（见没接第 1 条）；要加显示逻辑（例如完成/未完成换图）在同一个格子里加绘制即可。
接隐藏文本：`UIAchievementListItem` / `GUIAchievementsMenu` 的文本绘制入口，`Hidden` 为真时把名字与说明换成 "???"。

## 6. 与其它管线的边界

* **物品 / NPC / 方块**：它们的通知由游戏发；本管线只挂完成回调，不重复拦截。
* **本地化**：成就的名字与说明走游戏自带的 Achievements.<name> 键，本管线不写文本。
* **玩家**：成就进度是账号级的，和玩家存档无关。
* **场景效果 / 菜单**：无关。

### 这一版没接

1. ~~**菜单图标绘制**~~：**已接**——`GUIAchievementsMenu.AchievementDraw` 后置，按公开的 `SortedAchievementsData[index]` 认出 mod 成就后，用作者贴图（`ModType.Texture`，592×64：解锁 x = `Index × 66`，锁定 x = 528）盖掉原版图集那一格；位置与缩放用游戏给的参数，不自己算布局。**仍不接**：`Hidden` 的 "???" 文案（归 `UIAchievementListItem` 的文本绘制，本版不改）。
2. ~~**四条自动通知**~~：**四条全接**——`OnNPCKilled`（`NPC.NPCLoot` 后置）、`OnTileDestroyed`（`WorldGen.KillTile` 后置，只在真的挖掉时）、`OnItemPickup`（`Player.GetItem` 后置，只服务 `Main.myPlayer`——成就进度各人各的）、`OnItemCraft`（`Main.CraftItem_GrantItem` 后置，成品已确定的那一步；比在配方表里找结果可靠）。
3. **成就存档迁移**（卸载 mod 后清理进度）。
4. **分类分页排序**（RegisterAchievementCategory 只写分类，不改菜单顺序）。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 生效 | Achievement 属性在 SetStaticDefaults 之前就已建好，条件可以直接挂 |
| 生效 | AddCondition 系列方法现在真的会进游戏的条件表 |
| 生效 | OnCompleted 在从未完成变成完成的那一下调用 |
| 生效 | 菜单图标（作者贴图替换原版图集格，未完成画锁定格） |
| 本版不生效 | `Hidden` 的 "???" 文本 / 存档迁移 |
| 生效 | 四条自动通知（击杀、挖掘、拾取、制作）都会调，且只在作者覆写时才有实际工作 |
