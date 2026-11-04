# 内容管线：物品（Item）

## 1. 我们的约束

* 内核只提供 IL2CPP 调用与 detour，**不做内容注册**；发号、写表、写名字都在托管层。
* 内容表长度由游戏在运行期决定，注入必须等表定型（见 [02-install](02-install.md)）。
* 每帧路径要能承受"上千实体 × 每个钩子"的调用量。

## 2. 设计

### 2.1 一个内容种类 = 一个管线类

```
Pipelines/Item/
  ItemPipeline.cs   模块：接线 + 分派 + 射击上下文
  ItemDefaults.cs   SetDefaults 接管
  ItemBinding.cs    Item 实体 ↔ ModItem 实例
```

**为什么按内容种类聚合，而不是像参考实现按关注点拆？**
一条"用物品"的调用链（`CanUseItem → UseItem → UseStyle → Shoot`）在游戏里本来就是连续的；按关注点拆开后，排查"为什么我的物品不发射"要在四个文件之间跳。聚合到一个类、类内按链分段，读一处就能看完整条链。代价是单文件更长——用分区注释和小类（`ItemDefaults`、`ItemBinding`）控制。

### 2.2 三层数据

| 层 | 生命周期 | 存哪 |
|---|---|---|
| 模板 | 进程 | `ContentRegistry` 的槽表，按 `Type` 索引 |
| 实体实例 | 跟随 `Item` 实体 | `EntityBinding<Item, ModItem>`（指针 → 实例） |
| 覆写位集 | 类型 | `ModType.Plan`（`HookMask`） |

**为什么要实体实例？** `Item` 在背包里会被复制、换 type、重排；作者在 `ModItem` 上写的字段属于"这一件物品"，不属于"这个类型"。所以模板只放共享值（`Type`、贴图），实例跟着实体走，换 type 就重建。

### 2.3 分派规则

* 顺序固定：**本物品的 `ModItem` 先，`GlobalItem` 后**（与 tModLoader 一致，作者按这个顺序推理）。
* 门控用位集：`Plan.Has(HookId.X)` 是一次位运算；没覆写就直接跳过，不构造参数。
* 短路语义：`CanUseItem` / `CanShoot` 返回 false 立即终止后续订阅者；`UseItem` / `Shoot` 取"或"。

### 2.4 射击链的上下文

`ItemCheck_Shoot` 与 `NewProjectile` 是两次独立调用，中间隔着原版逻辑。管线在射击门里记下"当前物品 + 玩家"，`NewProjectile` 消费一次后**立即清空**：

* 作者在 `Shoot` 里自己发弹时，不能再次命中同一份上下文（否则递归到栈溢出）；
* 上下文是管线实例字段，不是 static——同一进程只有一个物品管线，但状态归属明确。

### 2.5 与安装管线的边界

物品管线**不写表**：贴图、名字、样例由安装管线（[02-install](02-install.md)）在表定型后统一写。物品管线只负责"游戏调到我时，把调用转给作者"。

## 3. 失败语义

| 情况 | 行为 |
|---|---|
| `ModItem.SetDefaults` 抛错 | 记一条，物品保持原版默认值 |
| `CanUseItem` / `CanShoot` 抛错 | 视为放行 |
| `UseItem` / `Shoot` 抛错 | 视为未处理，走原版 |
| 实例绑定失败 | 跳过 mod 钩子，原版路径照走 |

原则：**作者代码出错不能让游戏不可玩**，只能让这个物品表现降级。

## 4. 扩展点

新增物品钩子：

1. `HookId` 末尾追加；
2. `ModItem` 加 `virtual`；
3. 在 `ItemPipeline` 对应游戏钩子里分派（本物品 → 全局）；
4. 需要跨物品统一行为时，写 `GlobalItem`，由 `GlobalItemRegistry` 统一取订阅者。
