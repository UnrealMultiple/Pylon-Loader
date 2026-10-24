# C# Mod 管线

本目录描述 `Pylon.ModLoader` 的管线框架与已实现的内容管线：每段负责什么、为什么这样切、怎么扩展。

## 框架

| 文档 | 内容 |
|---|---|
| [00-analysis](00-analysis.md) | 现状问题清单与改造目标 |
| [01-registration](01-registration.md) | 内容注册：类型发现、确定性发号、槽表 |
| [02-install](02-install.md) | 内容安装：阶段、预算、可续步骤、维护模式 |
| [03-hooks](03-hooks.md) | 钩子分派：`HookId` + 位集 + 稠密订阅表 |
| [04-loader-modules](04-loader-modules.md) | 模块装配：显式数组、模块契约 |

## 共同前提

1. **一个实例**：`LoaderHost` 持有内容表、钩子总线、安装管线、Mod 集合。静态类只是门面，不存状态。
2. **顺序即约定**：注册顺序 = `(种类, Mod 加载顺序, 类型全名)`；安装顺序 = 步骤数组；模块顺序 = 数组顺序。三处都不靠魔法数字或反射。
3. **热路径形态固定**：位集判覆写、数组按下标取、`ref` 返回描述符；不在每帧路径上做字符串、字典、闭包、分配。
4. **大表用 struct + ref**：`ContentDescriptor`、`HookMask`、槽表都是值类型 + 下标访问。
5. **失败隔离到最小单位**：一个类型、一个步骤、一个订阅者，坏了只停它自己。

## 阶段总览

```
Mod.Load(context)
   │
   ├─ 注册管线：发现类型 → 发号 → 建表（不碰游戏）
   │
   ├─ 模块装配：LoaderRegistry 顺序 Install()
   │
   ├─ 钩子总线：按 HookMask 建订阅表
   │
   └─ 返回；作者 Load() 跑自己的注册代码

游戏就绪（每帧探测）
   │
   └─ 安装管线：Layout → Static → Surface → Samples → Tiles → Mods → Recipes
        │
        └─ 完成 → 维护模式
```

## 已实现的内容管线

每条链路由「作者基类 + 内容表 + 钩子分派」三件套组成：作者只写虚方法，表在安装期建好，钩子在游戏对应时机分派。

| 文档 | 已实现 |
|---|---|
| [content-item](content-item.md) | 物品创建/默认值/贴图/名字/提示/研究/掉落/右键；`GlobalItem` 全实体分派 |
| [content-npc](content-npc.md) | NPC 生命周期、绑定、AI/绘制、城镇幸福度与商店、Boss 头图、掉落 |
| [content-projectile](content-projectile.md) | 弹幕创建、AI、绘制、命中、附加数据同步 |
| [content-buff](content-buff.md) | buff 注册、图标、文本、玩家/NPC 每帧、右键取消、`GlobalBuff` |
| [content-tile](content-tile.md) | 方块注册、贴图槽、锚点数据、放置/破坏、随机更新、方块实体 |
| [content-wall](content-wall.md) | 墙壁注册、贴图槽、绘制 |
| [content-tileentity](content-tileentity.md) | 方块实体建/存/载/网络同步、定时重发、`ByID`/`ByPosition` |
| [content-tree](content-tree.md) | 树的风格号、生长、摇树、掉落与橡果、叶子、种苗放置与长大、背景补画 |
| [content-rarity](content-rarity.md) | 稀有度注册与颜色、提示绘制、拾取提示 |
| [content-prefix](content-prefix.md) | 前缀注册、可滚动前缀、提示行、数值修正 |
| [content-recipe](content-recipe.md) | 配方注册、分页、条件、`GlobalRecipe` |
| [content-loot](content-loot.md) | 掉落规则表、NPC/方块/Boss 袋掉落、图鉴合并 |
| [content-shop](content-shop.md) | NPC 商店条目、价格浮动、幸福度折扣 |
| [content-banner](content-banner.md) | 旗子击杀计数、领取、插地减伤、网络同步 |
| [content-biome](content-biome.md) | 群落判定、归属端权威同步、场景效果接入 |
| [content-sceneeffect](content-sceneeffect.md) | 场景效果优先级、音乐/水/背景的写入口 |
| [content-water](content-water.md) | 水风格注册与绘制参数 |
| [content-background](content-background.md) | 表层/地下背景风格、远山图层、淡入 |
| [content-music](content-music.md) | 音乐槽注册与切换、菜单音乐 |
| [content-sound](content-sound.md) | 音效槽注册与播放 |
| [content-dust](content-dust.md) | 尘土类型注册、生成、每帧、绘制 |
| [content-gore](content-gore.md) | 血腥粒子类型、生成、每帧、透明度 |
| [content-cloud](content-cloud.md) | 云类型注册与贴图 |
| [content-hair](content-hair.md) | 发型注册、菜单两段式、解锁态 |
| [content-mount](content-mount.md) | 坐骑注册、贴图、层、每帧 |
| [content-equiptexture](content-equiptexture.md) | 装备贴图槽、翅膀/悬停/时装判定 |
| [content-damageclass](content-damageclass.md) | 伤害类别、系数、显示名 |
| [content-player](content-player.md) | `ModPlayer` 生命周期、存档、同步、绘制数据链 |
| [content-playerdrawlayer](content-playerdrawlayer.md) | 两个层位 + 插入原版逐层链、隐藏层、`PlayerDrawSet` 五钩子、缩放 |
| [content-tooltip](content-tooltip.md) | 提示行重建、`ModifyTooltips` 全局链、非悬停路径 |
| [content-interface](content-interface.md) | 界面层插入/换序/隐藏、`PostDrawTiles` / `PostDrawInterface` |
| [content-resourceoverlay](content-resourceoverlay.md) | 生命/魔力条覆盖、单颗资源、三种条样式 |
| [content-menu](content-menu.md) | 菜单主题、logo/太阳/月亮、切换与持久化、菜单背景 |
| [content-achievement](content-achievement.md) | 成就注册、条件、图标、完成通知、隐藏文本 |
| [content-emotebubble](content-emotebubble.md) | 表情注册、菜单分类、绘制、网络同步、全局钩子 |
| [content-touchbutton](content-touchbutton.md) | 触屏按钮注册、布局保存、图标、快捷条、原生动作表 |
| [content-keybind](content-keybind.md) | 键位注册与轮询 |
| [content-command](content-command.md) | 聊天命令注册与解析 |
| [content-config](content-config.md) | 配置声明、屏幕、保存、服务端裁决、`NeedsReload` 门 |
| [content-pylon](content-pylon.md) | 传送晶塔注册与网络同步 |
| [content-bossbar](content-bossbar.md) | Boss 血条、精确目标、多节 |
| [content-net](content-net.md) | 子通道协议、物品/世界/NPC/弹幕附加同步 |

## 加一条新内容管线的固定动作

1. 在 `Content/` 加作者基类（一个内容一个类，只声明虚方法）；
2. 在 `Pipelines/<Name>/` 加内容表（`ModContent.GetContent<T>()` 收表、按类型全名排序）与管线（`ILoaderModule`：`Name` + `Install()`，需要重建时实现 `ILoaderModuleRefresh`）；
3. 钩子注册用 `On.*`，分派前先判表为空；每个作者回调单独 `try/catch`，失败走 `ContentErrors.Report`；
4. 钩子方法名与 `HookId` 一一对应，`HookId` 只追加；热路径不放分配；
5. 在 `docs/pipeline/` 加 `content-<name>.md`：职责、实现原理、失败语义、作者 API 影响；
6. 在 [docs/testing/02-case-matrix.md](../testing/02-case-matrix.md) 补该管线的用例，并在测试 Mod 里加一个组。

## 测试

42 条管线目前零测试。测试策略、用例矩阵与测试 Mod 规格见 [docs/testing](../testing/README.md)。
