# 示例

三个可直接跑的 Mod，从最小壳到完整内容：

| 示例 | 展示什么 | 规模 |
|---|---|---|
| [FrameMod](FrameMod) | 最小可运行 Mod：注册一个 ImGui 页，画一张游戏贴图并接一个输入框 | 单文件级 |
| [XiheMagic](XiheMagic) | 完整物品链路：自定义弹幕、引导控制、着色器激光柱、BGM 与音效 | 中等 |
| [XiheBoss](XiheBoss) | 完整 Boss：NPC + 多种弹幕 + 硬件几何批 + 屏幕后处理 + 音乐切换 | 大 |

## 跑起来

```powershell
pwsh -File src/csharp/tools/pylon.ps1 push -Samples FrameMod        # 编译并推到 Mods/
pwsh -File src/csharp/tools/pylon.ps1 run                           # 启动并抓日志
```

也可以手动把编译产物放进设备的 `/storage/emulated/0/Android/data/dev.pylon.loader/Mods/<id>/`。

示例依赖 `interop/generated` 里的游戏代理程序集，因此**只能用当前游戏版本对应的代理编译**（见 [构建](../../../docs/build.md) 第 2 节）。

## 阅读顺序

1. `FrameMod`：看一个 Mod 最少需要什么（`Mod` 基类 + 一个 `IModImGuiTab`）。
2. `XiheMagic`：看内容注册、弹幕 AI、着色器资源与配置的组织方式。
3. `XiheBoss`：看多文件模型（`Content/` + `Fx/` + `assets/`）与屏幕后处理。
