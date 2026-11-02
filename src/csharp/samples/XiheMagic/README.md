# 羲和魔杖（XiheMagic）

一件完整武器的最小完整链路：自定义物品 → 引导控制弹幕 → 激光柱弹幕 → 着色器 → 音效。

## 内容

| 类型 | 文件 | 说明 |
|---|---|---|
| 物品 | `Content/XiHeMagicItem.cs` | 法杖本体：伤害、使用与持有姿态、动作流程 |
| 弹幕 | `Content/XiheMagicControler.cs` | 引导阶段：跟随玩家、按帧推进蓄力状态 |
| 弹幕 | `Content/XiheLaserPillar.cs` | 结算阶段：自天穹落下的激光柱 |
| 效果 | `Fx/XiheFx.cs` + `assets/effects/XiheBeam.frag` | 光柱渲染与屏幕叠加 |
| 资源 | `Fx/XiheTextures.cs` `Fx/XiheSounds.cs` | 贴图与音效注册 |

## 结构

```text
XiheMagicMod.cs         Mod 入口：注册内容、贴图、音效、ImGui 页
XiheImGuiTab.cs         调试面板（参数实时调）
Content/                物品与弹幕
Fx/                     贴图 / 音效 / 效果封装
assets/                 textures、sounds、effects（相对 Mod 目录）
manifest.json           Mod 元信息（id、名字、作者、入口类型）
```

## 跑起来

```powershell
pwsh -File src/csharp/tools/pylon.ps1 push -Samples XiheMagic
pwsh -File src/csharp/tools/pylon.ps1 run
```

进游戏后从物品栏取"羲和魔杖"，按住使用键引导，满蓄力后松手结算。调试面板在 `Pylon Mod` 窗口里。

## 值得看的地方

- **多阶段动作**：一个物品用两个弹幕分工（引导 / 结算），状态通过弹幕字段传递，不依赖静态变量。
- **着色器即资源**：`assets/effects/*.frag` 与贴图同走 Mod 目录，跟着 Mod 走，运行期按名加载。
- **调试面板先于特效**：参数外置到 ImGui，改数值不用重编。
