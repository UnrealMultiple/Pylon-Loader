# 内容管线：表情气泡（EmoteBubble）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
| 表情菜单的分类列表 | `AddToCategory` 注册的分类 → 追加进对应分类（`IsUnlocked` 为真才加） |
| 菜单里取帧 | `GetFrameInEmoteMenu` |
|---|---|
| `EmoteBubble.NewBubble` | `ModEmoteBubble.OnSpawn`（mod 编号走自己的建泡路径） |
| `EmoteBubble.Update` | `ModEmoteBubble.UpdateFrame`（每帧一次；返回 false 表示帧动画由 `GetFrame` 决定） |
| `EmoteBubble.Draw` | `ModEmoteBubble.PreDraw` / `PostDraw`（mod 编号自己画：原版的白泡泡外壳 + 作者贴图，原版那张"编号→图集位置"的表不查） |
| `EmoteBubble.CheckForNPCsToReactToEmoteBubble` | mod 编号直接跳过（原版按编号查 NPC 反应表） |
| `EmoteBubble.ShouldFilterPlayerEmote` | mod 编号一律不滤 |

**已接**：多人同步（`EmoteNetPipeline`：客户端发 / 服务器转发并自建，锚用游戏自己的序列化，见对应小节）。

**已接**：NPC 对 mod 表情的反应（`ModEmoteBubble.NPCReaction`，见 2.8）。

**不接**：表情选择菜单的条目登记（`AddToCategory` / `IsUnlocked` / `GetFrameInEmoteMenu`）、白泡泡外壳。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/EmoteBubble/` 下：`EmoteBubblePipeline.cs`（六条游戏入口 + 自绘）、`EmoteBubbleTable.cs`（稠密模板表 + 编号分配 + 名字查找）。

### 2.2 不造假泡泡，只接管"这个编号怎么活"

建泡、寿命倒计时、`byID` 登记、清理，全交给游戏自己的 `EmoteBubble` 类：mod 编号的泡泡就是**真的** `EmoteBubble` 对象，由我们那条 `NewBubble` 分派 `new EmoteBubble(emote, anchor, time)` 建出来。

只接管三件游戏不知道怎么办的事：

1. **画**：`Draw` 里 mod 编号整段自己画（作者贴图 + 作者帧），不调 `orig`——原版绘制会把编号折进那张固定布局的表情图集。
2. **帧**：`Update` 里先调 `orig`（寿命、`frameCounter`、`frame` 照旧推进），再分派一次 `UpdateFrame`；作者返回 false 时，绘制阶段改用 `GetFrame()` 的返回帧。
3. **两个会把编号当数组下标的入口**：NPC 反应与玩家表情过滤，mod 编号直接短路。

绘制那一步的细节（都在 `Draw` 里）：

* **位置与翻转**用游戏自己的 `GetPosition(out SpriteEffects)`——它算锚点偏移、水平翻转，锚在实体上时还带实体朝向。私有方法照调不误：interop 代理把它生成成公开的。取不到时才退回"按锚点估算"的兜底。
* **白泡泡外壳**从原版表情图集取（`TextureAssets.Extra[48]`，8 列 × `EMOTE_SHEET_VERTICAL_FRAMES` 行），先画壳再画作者内容，两者共用同一个原点，所以作者只要给 34×28 的图标就会正正好坐在壳里。
* **淡入淡出**照原版：寿命头 6 帧与最后 6 帧只画壳（第 0 列）不画内容，其余画实心壳（第 1 列）。
* **图集校验**：槽位号不是编译期常量，画之前先验"宽度能被 8 整除、高度能被行数整除"——槽位错了就只是没有壳，不会把一张不相干的图当壳画出去。

### 2.3 贴图从哪来

`ModType.Texture`（`TexturePath` → `assets/emotebubbles/<Name>.png`）在安装期的 `Surface` 阶段已经上传好了，本管线直接读，不另建资产表。贴图约定：并排两个 34×28 的图标，默认帧就是"左半 / 右半"。

### 2.4 编号与状态

`EmoteBubbleTable` 是静态稠密表，编号 = `EmoteID.Count`（151；在 interop 里是静态只读而不是 `const`，所以表的起点也是运行期读一次）+ 注册序。发号在 `Refresh()` 里做（每次 Mod 注册之后、作者 `SetupContent` 之前），建表后立即对每个模板调一次 `SetStaticDefaults`。

管线实例只放去重集合；模板表不带每帧状态。

### 2.5 位置

位置按锚点自己算：锚在实体上就取实体中心上方，锚在坐标上就用锚点坐标。**不查游戏那张"每种表情的偏移"表**——mod 编号进不去那张表，查了也只能拿到越界值。

### 2.6 错误边界

绘制与每帧回调都包在单条泡泡的 `try/catch` 里：作者抛异常 → `ContentErrors.Report` 记一次、这次绘制跳过，游戏继续跑。`Refresh` 与钩子注册里**没有** `try/catch`。

### 2.6 多人同步

原版的表情包只带原版编号，mod 编号到了对端就是"不认识的表情"。本管线新建 `EmoteNetPipeline`（装在 `NetPipeline` 之后，独立通道）：

* **发**：mod 表情建泡后把 `[表情号, 锚类型, 锚编号, 时长]` 发出去。锚用游戏自带的 `EmoteBubble.SerializeNetAnchor` / `DeserializeNetAnchor`——玩家/NPC/方块/坐标四种锚它都处理了。
* **走向**：客户端 `toClient = 255`（服务器）；服务器收到后先转发给除发送者外的所有人（`ignoreClient = from`），再自己建一个泡（服务器也要看得见，逻辑与客户端一致）。
* **收**：`ApplyRemote` 置一个 `OnReceive` 标记再调 `EmoteBubble.NewBubble`，建泡路径看到标记就不再转发，避免回声。
* **只认 mod 号**：`emoticon < EmoteBubbleTable.Base` 的包直接丢——原版表情由原版自己的网络路径负责，我们不要重复建。

**没做的**：断线重连后的补发（表情是短命的 180 帧，重连时早过期了，补发没有意义）。

### 2.7 悬停预览

原版表情的名字表按原版编号索引，mod 编号查不到，所以悬停时什么都不显示。本管线挂 `GUIEmotesWindow.EmoteOver`：是 mod 表情就写 `Main.hoverItemName`（游戏在光标附近画它），原版编号照旧交给 `orig`。

名字来源是 `ModEmoteBubble.DisplayName`（默认类名，作者可覆盖）。**没做本地化**：要给表情接 `EmojiName.*` 这类本地化键，得先有一套名字注册流程，现在没有真实需求。

### 2.8 NPC 反应

```csharp
public override int NPCReaction => EmoteID.Heart;
```

原版那套"哪个 NPC 用哪号表情回应"的表只认原版编号，mod 表情进去查不到，所以本管线自己挑：玩家做 mod 表情时，取 **40 格内最近的城镇 NPC**，用作者给的原版表情号在它头上建一个泡（`EmoteBubble.NewBubble(reaction, new WorldUIAnchor(npc), 180)`）。

* 默认 `-1` = 不回应（opt-in，避免所有 mod 表情都触发反应）；
* 只挑城镇 NPC——原版的表情反应也主要发生在城镇 NPC 上，且它们才有"心情"语义；
* 一次只让一个 NPC 回应：原版也是挑一个，避免整条街刷屏。

## 3. 取舍理由

* **为什么用真 `EmoteBubble` 对象？** 寿命倒计时、`byID`、`UpdateAll` 里的清理、`DrawAll` 的排序，都是游戏写好的；自己攒一个泡泡列表等于把这几段重写一遍，还要跟着版本改。用真对象只接管绘制与帧，改动面最小。
* **为什么 `Update` 里先调 `orig` 再分派？** 反过来（先分派、作者返回 false 就不调 `orig`）听着更"接管"，但那会把寿命推进也一起停掉——作者只要写了帧动画，泡泡就永远不消失。宁可让原版帧先走一步、绘制时再覆盖。
* **为什么 `Draw` 不调 `orig`？** 原版绘制按编号算图集里的行列，mod 编号算出来的位置在贴图之外；调了也画不出东西，反而可能越界取样。
* **为什么不做菜单？** 菜单是一条独立的数据流（`_availableEmotes` + 分类列表 + `GetFrameInEmoteMenu`），把它接上要做三件事：往私有静态列表里塞编号、接管菜单绘制、接管菜单的解锁判定。这一版先把"泡泡能显示、能消失、能分派"做对，菜单留成显式缺口。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 作者贴图缺失 | 这次绘制跳过，泡泡寿命照常走 |
| `PreDraw` 返回 false | 不画这一帧，`PostDraw` 照常调 |
| `UpdateFrame` 抛异常 | 记一次，按"用原版帧"处理 |
| `GetFrame` 返回超出贴图的区域 | 照画（采样出界由贴图采样器处理），文档里要求作者自己保证 |
| mod 编号的泡泡被原版 NPC 反应逻辑问到 | 直接跳过，不进原版数组 |
| `MonoMod` 钩子里拿到 null 泡泡 | 不做任何事，转 `orig` |
| `GetPosition` 抛异常 | 记一条，用按锚点估算的位置继续画 |
| 表情图集槽位不对 / 图集不在 | 只画作者内容（没有白壳），不崩 |

## 5. 扩展方式

要接菜单：在 `EmoteBubblePipeline` 里加 `On.Terraria.GameContent.UI.EmotesMenu.EmoteMenu` 那几条钩子、在 `EmoteBubbleTable` 里加"分类 → 模板"的索引即可；`AddToCategory` 与 `GetFrameInEmoteMenu` 这两个作者声明已经在基类里，接上就能直接用，不改作者代码。

## 6. 与其它管线的边界

* **贴图管线**：`ModType.Texture` 的加载与失败处理归安装管线（`Surface` 阶段的 `LoadTextures`），本管线只读。
* **玩家/NPC 管线**：表情的触发（`player.emoteTime` / 表情按钮）不在本管线，本管线只处理"泡泡已经存在"之后的事。
* **网络管线**：mod 表情走 `EmoteNetPipeline`（独立通道，装在 `NetPipeline` 之后）。

### 这一版没接

1. **表情选择菜单的分类滚动与分页**：mod 表情已经进菜单（分类与帧都接上了），但菜单自己的滚动/分页/每页数量是游戏算的，本管线不参与。
2. ~~**NPC 对表情的反应**~~：**已接**（`ModEmoteBubble.NPCReaction`，见 2.8）。
3. ~~**多人同步**~~：**已接**（见对应小节）——建泡时把"表情号 + 锚 + 时长"发出去，客户端发服务器、服务器转发并自建。
4. ~~**白泡泡外壳**~~：**已接**（见 2.2 的绘制细节）。**悬停预览仍不接**：`DrawTemporaryBubble` 那条路径不走 `EmoteBubble.Draw`，mod 编号的临时泡泡目前不会显示。
5. ~~**`GlobalEmoteBubble`**~~：**已接**——新增 `GlobalEmoteBubble : ModType` + `GlobalEmoteBubbleRegistry`（按覆写建位集，与 `GlobalBuff` 同形）。分派点：`OnSpawn`（`NewBubble` 之后用游戏自己的 `EmoteBubble.byID` 取实例，游戏表情与 mod 表情都派）、`UpdateFrame`（每个气泡每帧）、`GetFrame`（mod 气泡取帧：作者优先、其次全局）、`PreDraw`/`PostDraw`（mod 气泡的绘制几何已知；游戏表情暂不派）、`GetFrameInEmoteMenu`（菜单里两种表情都问）。`UpdateFrameInEmoteMenu` 只有声明、没有分派（本构建没有菜单帧计数入口）。
6. **点击菜单按钮时的额外判定**（是否已解锁、冷却等）：`EmoteButton.LeftClick` 没接，建泡仍然走 `NewBubble`。
7. **`EmoteBubble.NewLocalOnlyBubble`**：这条生成器没给钩子（`On.` 里只有 `NewBubble` 与 `NewBubbleNPC`），所以 mod 编号**只能**通过 `NewBubble` 建泡；从别处（本地专用建泡）进来的 mod 编号会走原版路径。本版不提供这类入口，因此不会主动踩到，但这是一条"没挡住"的边界。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义变更 | `ModEmoteBubble.EmoteBubble` 现在真的指向游戏对象（之前恒为 null）；只在钩子方法里有效 |
| 本版生效 | 菜单分类（`AddToCategory` / `IsUnlocked`）与菜单帧（`GetFrameInEmoteMenu`）都接通；泡泡自带白壳与淡入淡出 |
| 生效 | 多人同步（`EmoteNetPipeline`，锚走游戏 `SerializeNetAnchor`/`DeserializeNetAnchor`） |
| 生效 | 悬停预览（`GUIEmotesWindow.EmoteOver` → `Main.hoverItemName = ModEmoteBubble.DisplayName`） |
| 生效 | NPC 反应（`NPCReaction`：最近的城镇 NPC 用指定原版表情回应） |
| 生效 | `GlobalEmoteBubble`：`OnSpawn` / `UpdateFrame` / `GetFrame` / `PreDraw` / `PostDraw` / `GetFrameInEmoteMenu`（`PreDraw`/`PostDraw` 只对 mod 表情；`UpdateFrameInEmoteMenu` 未接） |
| 新增约束 | `TexturePath` 指向的贴图必须是并排两个 34×28 图标；`GetFrame` 返回的区域必须落在贴图内 |
