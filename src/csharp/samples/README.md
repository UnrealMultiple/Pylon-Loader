# 示例

两个可直接跑的 Mod，从最小壳到完整内容：

| 示例 | 展示什么 | 规模 |
|---|---|---|
| [FrameMod](FrameMod) | 最小可运行 Mod：注册一个 ImGui 页，画一张游戏贴图并接一个输入框 | 单文件级 |
| [XiheBoss](XiheBoss) | 多 Boss 合一：羲和（本体+魔杖）+ 史莱姆王 + 克苏鲁之眼（MEAC 一比一，GlobalNpc 接管原版 type 50/4/5）+ 多种弹幕 + 硬件几何批 + 屏幕后处理 | 大 |

> 历史说明：原 `XiheMagic` 示例已并入 `XiheBoss`（`Content/Wand/` + `Fx/Wand/`，namespace `XiheBoss.Wand`），其独立 mod 目录已删除。

## 跑起来

```powershell
pwsh -File src/csharp/tools/pylon.ps1 push -Samples FrameMod        # 编译并推到 Mods/
pwsh -File src/csharp/tools/pylon.ps1 run                           # 启动并抓日志
```

也可以手动把编译产物放进设备的 `/storage/emulated/0/Android/data/dev.pylon.loader/Mods/<id>/`。

示例依赖 `interop/generated` 里的游戏代理程序集，因此**只能用当前游戏版本对应的代理编译**（见 [构建](../../../docs/build.md) 第 2 节）。

## 阅读顺序

1. `FrameMod`：看一个 Mod 最少需要什么（`Mod` 基类 + 一个 `IModImGuiTab`）。
2. `XiheBoss`：看多文件模型（`Content/` + `Fx/` + `assets/`）与屏幕后处理；魔杖部分看 `Content/Wand/`（物品 → 引导控制 → 激光柱的完整链路）。
