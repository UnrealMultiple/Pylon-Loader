# 羲和 Boss（XiheBoss）

一个完整 Boss + 魔杖二合一的示例：NPC、多种弹幕、召唤物、可合成武器、硬件几何批、屏幕后处理与音乐切换。规模最大，适合看"一个中型 Mod 怎么组织"。

> 原独立示例 `XiheMagic`（羲和魔杖）已并入本 Mod（`Content/Wand/` + `Fx/Wand/`，namespace `XiheBoss.Wand`）。
> 全部内容 1:1 复刻 MEAC（`Bosses.EoL.XiHe` / `Items.ModWeapons.XiHeMagicItem` / `EoL.Summoner.XiheSummonItem`）。

## 内容

| 类型 | 说明 |
|---|---|
| NPC | `XiheBossNpc`：本体、阶段切换、行动模式 |
| 弹幕 | `EoLLance`、`EoLProj`、`EoLLaser1`、`EoLBigLaser`、`CrystalArrow`、`Cycloid`、`EoLLight`、`EoLFire`、`SunProj`、`SolarProminence`、`XiheSummonProj` |
| 物品 | `XiheSummonItem`（召唤物，外摆线仪式 → Boss）、`Wand/XiHeMagicItem`（魔杖，配方：日耀碎×20 + 金锭×10 @ 月光熔炉） |
| 视觉 | `Fx/XiheFx.cs`（屏幕图元与后处理）+ `Fx/XiheMesh.cs`（硬件几何批）+ `Fx/Wand/XiheWandFx.cs`（魔杖激光柱全屏 shader） |
| 着色器 | `assets/effects/`：`XiheGeo` `XiheTrailFx` `XihePost` `XiheBigLaser` `XihePrims` `XiheRing` `XiheLine` `XiheBeam` |
| 声音 | 原版 `SoundID` + `assets/sounds/Laser.wav`；BGM 为 `assets/music/TakeFlight.mp3` |

## 结构

```text
XiheBossMod.cs          Mod 入口 + ModSystem：装配、钩子、配方、世界卸载
XiheHelper.cs           几何与数学工具（顶点批、轨迹、绘制辅助）
XiheBossImGuiTab.cs     调试面板：召唤 Boss / 发放试玩物品（魔杖、召唤物）
Content/                NPC 与弹幕（含 XiheSummonItem/XiheSummonProj）
Content/Wand/           魔杖：物品 → 引导控制器 → 激光柱
Fx/                     效果与几何批
Fx/Wand/                魔杖激光柱的屏幕效果控制器
assets/                 effects、music、sounds、textures
```

## 跑起来

```powershell
pwsh -File src/csharp/tools/pylon.ps1 push -Samples XiheBoss
pwsh -File src/csharp/tools/pylon.ps1 run
```

> 设备上如果还装着旧 `XiheMagic`，先删掉 `Mods/XiheMagic/`，避免引导阶段出现两套屏幕效果。

## 值得看的地方

- **几何批**：大量图元不逐个 `SpriteBatch.Draw`，而是攒进顶点批一次提交，这是手机端不掉帧的关键。
- **后处理分层**：屏幕效果按世界层 / 屏幕层分别提交，与 ImGui 覆盖层互不干扰。
- **音乐与音效**：BGM 走 `MusicLoader` 注册，音效走 `SoundStyle`，都不依赖游戏原版资源名。
- **资源全部相对 Mod 目录**，因此可以直接整目录拷到设备或打成 `.pmod`。
- **召唤链路**：物品 → 仪式弹幕（`XiheSummonProj`）→ 15 tick 世界倒计时（静音 + 天光收拢）→ Boss 登场，
  和 MEAC 的 `XiheSummonItem → XiheSummonProj → XiHeWorldSystem.spawnTimer` 一一对应。
