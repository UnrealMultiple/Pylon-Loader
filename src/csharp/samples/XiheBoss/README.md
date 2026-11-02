# 羲和 Boss（XiheBoss）

一个完整 Boss：NPC、多种弹幕、硬件几何批、屏幕后处理与音乐切换。规模最大，适合看"一个中型 Mod 怎么组织"。

## 内容

| 类型 | 说明 |
|---|---|
| NPC | `XiheBossNpc`：本体、阶段切换、行动模式 |
| 弹幕 | `EoLLance`、`EoLProj`、`EoLLaser1`、`EoLBigLaser`、`CrystalArrow`、`Cycloid`、`EoLLight`、`EoLFire`、`SunProj` |
| 视觉 | `Fx/XiheFx.cs`（屏幕图元与后处理）+ `Fx/XiheMesh.cs`（硬件几何批：精灵 / 三角形 / 拖尾） |
| 着色器 | `assets/effects/`：`XiheGeo` `XiheTrailFx` `XihePost` `XiheBigLaser` `XihePrims` `XiheRing` `XiheLine` |
| 声音 | 原版 `SoundID` + `assets/sounds/Laser.wav`；BGM 为 `assets/music/TakeFlight.mp3` |

## 结构

```text
XiheBossMod.cs          Mod 入口：注册 NPC/弹幕/贴图/音乐/ImGui 页
XiheHelper.cs           几何与数学工具（顶点批、轨迹、绘制辅助）
XiheBossImGuiTab.cs     调试面板：阶段、弹幕参数、后处理开关
Content/                NPC 与弹幕
Fx/                     效果与几何批
assets/                 effects、music、sounds、textures
```

## 跑起来

```powershell
pwsh -File src/csharp/tools/pylon.ps1 push -Samples XiheBoss
pwsh -File src/csharp/tools/pylon.ps1 run
```

## 值得看的地方

- **几何批**：大量图元不逐个 `SpriteBatch.Draw`，而是攒进顶点批一次提交，这是手机端不掉帧的关键。
- **后处理分层**：屏幕效果按世界层 / 屏幕层分别提交，与 ImGui 覆盖层互不干扰。
- **音乐与音效**：BGM 走 `MusicLoader` 注册，音效走 `SoundStyle`，都不依赖游戏原版资源名。
- **资源全部相对 Mod 目录**，因此可以直接整目录拷到设备或打成 `.pmod`。
