# 弹幕移植约定（Subagent 必读）

目标：把 MEAC 的 Boss 弹幕 **逐条**移植到 Pylon Loader 的 `XiheBoss` Mod。行为（AI 数值/时序/伤害）
必须与 MEAC 完全一致；画面统一交给屏幕着色器（不自己画精灵）。

## 参考文件（先读）

- 已完成范例：`samples/XiheBoss/Content/Projectiles/EoLLance.cs`
- 图元 API：`samples/XiheBoss/Fx/XiheFx.cs`（`Orb` / `Beam` / `Ring`、`Tex` 枚举在 `Fx/XiheTextures.cs`）
- 源实现（已去掉 IL 注释）：
  `C:\Users\admin\Desktop\Pylon-Loader\.work\meac\MEAC.Bosses.EoL.Projectiles\<名字>.cs`
- Boss 本体：`samples/XiheBoss/Content/XiheBossNpc.cs`（看它怎么传 ai）

## 硬性规则

1. 类继承 `Pylon.ModLoader.ModProjectile`，放在 `namespace XiheBoss`，文件名 = 类名。
2. `public override string TexturePath => null;`，`PreDraw` 返回 `false`（画面全在 `PushVisual`）。
3. **不要**用 `Projectile.NewProjectileDirect`（手机版没有）；用
   `Projectile.NewProjectile(null, pos, vel, type, dmg, 0f, Main.myPlayer, ai0, ai1, ai2)`，
   需要拿实例时用返回的下标查 `Main.projectile[idx]`（见 EoLLance 的 `NewProjectile`）。
4. **不要**用 `proj.ModProjectile as Xxx`、`ModContent.GetInstance<>`、`Sets.DrawScreenCheckFluff`、
   `ModProjectile.Texture`、`Terraria.Audio.SoundEngine.PlaySound(ref ...)`。
   音效用 `Pylon.ModLoader.SoundEngine.PlaySound(SoundID.Item159, pos)`。
5. 不要给 `Projectile.ai` 单独赋值（IL2CPP 结构体属性会丢写）：整体读出来、改、整体写回：
   ```csharp
   var ai = proj.ai; ai[2] = 5f; proj.ai = ai;
   ```
   `localAI` 同理（可参考 `XiheHelper.WriteLocalAi`）。
6. 数学/工具一律用 `XiheHelper`（`Vxr`/`RotatedBy`/`Normalize2`/`HsvToRgb`/`Gaussian`/`NextVector2Unit`/`MoveTo` …）。
7. 允许的 Terraria 类型：`Main.player/npc/projectile`、`Dust.NewDustDirect`、`Lighting.AddLight`、
   `Collision.CheckAABBvLineCollision`、`Utils.*`、`SoundID.*`、`ProjectileID.*`、`Main.rand`。
8. 不要写 `using Terraria.Audio;`（会和 Pylon 的 SoundEngine 冲突）。
9. `Projectile.Kill()` / `proj.active` / `proj.timeLeft` 直接用；`ShouldUpdatePosition` 按源文件返回。
10. 每个类必须有 `public static void PushVisual(Projectile p)`；`XiheBossMod.SetupContent` 已经登记好类型。

## ai 约定（Boss 侧已经这么传，必须照收）

| 类 | ai0 | ai1 | ai2 |
|---|---|---|---|
| EoLProj | 源文件的 `ai[0]`（目标下标 / npc 下标 / 6 相位等） | 源文件的 `ai[1]`（行为 0..9、114514） | 备用 |
| EoLLaser1 | 色相 或 宿主 NPC 下标 | 模式（0/1/-1/2/3/4） | **maxTime**（源文件的 `maxTime` 字段改由 ai[2] 读） |
| EoLBigLaser | 宿主 NPC 下标（模式 1） | 模式（0/1） | — |
| CrystalArrow | 源文件 ai[0] | 源文件 ai[1] | — |
| Cycloid | 宿主 NPC 下标 | 1 = p2 | — |
| EoLLight | 宿主 NPC 下标 | 旋转速度 | — |
| EoLFire | 宿主 NPC 下标 | 旋转速度 | — |
| SunProj | — | — | — |

源文件里“外部直接改字段”的地方（例如 `maxTime`、`p2`、`ownerIndex`、`localAI[0]`）按上表换成 ai，
必要时在 `OnSpawn` 里把 ai 抄进实例字段。`timeLeft`、`rotation`、`localAI[0]` 由 Boss 直接写，照读。

## 视觉约定（PushVisual）

- 只读 `proj` 的字段，不要改状态。
- 单位：世界像素；`XiheFx` 内部换算屏幕 uv。
- 常用组合：
  - 光束：`XiheFx.Beam(start, dir, lengthPx, halfWidthPx, tex, color, alpha, scroll, repeatLenPx, core)`
  - 光球/精灵：`XiheFx.Orb(world, radiusPx, tex, color, alpha, rot, yScale, core)`
  - 环：`XiheFx.Ring(world, radiusPx, widthPx, tex, color, alpha, rot, arc)`
- 贴图：`Tex.Tex0..Tex4`（MEAC 光束条）、`Tex.Laser`/`Tex.Laser0`（端点辉光）、`Tex.Ball`/`Ball2`/`Ball3`、
  `Tex.Trail`/`Tex.TrailShape`/`Tex.Trail209`、`Tex.Wave`/`Wave1`/`Wave2`、`Tex.Extra89`、
  `Tex.WarpRing`/`WarpRing2`/`WarpBall`、`Tex.DustLight`/`LightDust`、`Tex.Flame`、`Tex.Explosion`、
  `Tex.EolProj`/`EolProjColor`、`Tex.MagicRing`、`Tex.Perlin`、`Tex.KnifeExtra1`、`Tex.Elip`。
- 源文件 `PreDraw` 里的几何/颜色/透明度照抄；无法 1:1 的（原版 3D 顶点、原生 Effect）用等价图元近似，
  在注释里写清楚“近似点”。

## 交付

- 只写指定的 2~3 个 `.cs` 文件（不要动 csproj/其它文件）。
- 每个文件顶部写中文注释：源文件对应关系 + 近似点。
- 完成后在回复里列出：文件名、源文件、ai 约定、视觉实现要点、你没能 1:1 的地方。
