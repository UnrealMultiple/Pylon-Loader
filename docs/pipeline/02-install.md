# 管线二：内容安装（Install）

## 职责

把"已经占好号的内容"变成"游戏真的认识的内容"：扩表、写贴图、写名字、跑静态默认值、建样例、注册配方。

输入：注册完成的内容表。
输出：游戏侧可用的内容；`InstallPipeline.State == Complete`。

与管线一的边界：注册**不碰**游戏对象，安装**只碰**游戏对象。安装可以失败、可以分帧、可以重入；注册不会。

## 原理

安装有三个硬约束，管线形状由它们决定：

1. **时机不可预测**：游戏要等异步加载完成、原版配方建好、ContentSamples 就绪，才允许扩表。早了会被游戏重建覆盖。
2. **单帧预算有限**：真机上一次性做完约 1.5s，会卡在主菜单之前。必须摊帧。
3. **步骤之间强依赖**：buff 静态默认值必须在写贴图之前；扩表必须在写槽之前；NPC 静态默认值必须在 NPC 样例之前。

所以安装必须是**有序、可续、带预算**的步骤流，而不是"一堆回调"。

## 设计

### 步骤

```csharp
internal interface IInstallStep
{
    string Name { get; }
    InstallStage Stage { get; }
    /// 返回 true = 这一步做完了；false = 还没做完，下一帧接着来。
    bool Run(InstallFrame frame);
}
```

* `return false` 是"可续"，不是"失败"。可续步骤（扩表分帧）自己保存游标，天然支持任意预算。
* 异常在 `Run` 外围捕获：记一条日志，**标记该步骤完成**，继续下一步。一个 Mod 的内容坏掉不能拖死整条管线。
* `InstallStage` 是阶段枚举，只用于日志分组与断言（步骤不得跨阶段回退）。

### 执行器

```
每帧 On.Terraria.Main.DoUpdate（只装一次）
  ├── 未就绪 → 判据不成立就退出（判据只读游戏状态，不写）
  ├── 已就绪 → 从游标处按预算执行步骤
  │     ├── 每步计时；≥ 阈值单独记一条
  │     ├── 预算耗尽 → 本帧退出，游标保留
  │     └── 全部完成 → State = Complete，转入维护模式
  └── 维护模式：表被游戏重建时补扩、重写槽
```

预算同时有两条：**时间**（默认 8ms）与**步数**（防止空转）。两者任一触发即让出本帧。

### 阶段

| 阶段 | 内容 |
|---|---|
| `Layout` | 扩内容表（可续，按种类分批） |
| `Static` | buff 静态默认值、buffImmune 扩长、坐骑 / 前缀 / 稀有度的静态初始化 |
| `Surface` | 写贴图槽、写名字与 tooltip |
| `Samples` | ContentSamples 样例（物品、NPC），持久 ID 登记 |
| `Tiles` | 墙壁 / 方块的实例表、`TileObjectData`、合并规则 |
| `Mods` | 每个 Mod 的 `SetupContent`、每个内容的 `PostSetupContent` |
| `Recipes` | `AddRecipeGroups` → `AddRecipes` → `PostAddRecipes` → 收尾 |

顺序写在 `InstallPlan.Build()` 一处，纯代码，不用配置、不用反射。

### 与 Mod 回调的关系

`Mod.ResizeArrays / AddRecipeGroups / AddRecipes / PostAddRecipes / PostSetupRecipes` 这类"作者要参与的节点"由管线在对应阶段调用，只对**确实覆写了**的 Mod 调用（`HookMask` 判定，见管线三）。作者没覆写就不会有这次调用成本。

### 维护模式

游戏会在切世界、重建 ContentSamples 时把表还原成原版长度。管线完成后不停机：每 60 帧检查一次表长度，短了补扩并重写槽；`TileLoader` / `WallLoader` 的实例表同理。

## 失败语义

| 情况 | 行为 |
|---|---|
| 就绪判据长时间不成立 | 超时（默认 120 帧）后强制进入安装，日志写明哪条判据没过 |
| 某步抛异常 | 记日志 + 跳过该步，继续 |
| 单步超预算 | 记"单步太慢"，提示拆分 |
| 扩表被游戏截断 | 维护模式补扩，不重跑整条管线 |

## 扩展方式

新增一种内容种类的安装：实现一个 `IInstallStep`，在 `InstallPlan.Build()` 的对应阶段插入一行。不需要改执行器、不需要注册钩子。
