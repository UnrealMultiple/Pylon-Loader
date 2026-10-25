# 羲和 Boss（XiheBoss）

MEAC `MEAC.Bosses.EoL.XiHe` 的 **1:1 复刻**，跑在 Pylon Loader 上（Terraria 手机版 1.4.5.8.6）。

- NPC：`XiheBossNpc`（1 只 Boss，无小怪、无召唤物）
- 弹幕：`EoLLance` / `EoLProj` / `EoLLaser1` / `EoLBigLaser` / `CrystalArrow` / `Cycloid` / `EoLLight` / `EoLFire` / `SunProj`
- 视觉：`Fx/XiheFx.cs`（屏幕图元）+ `assets/effects/XihePrims.frag`（orb/beam/ring 加色）+ `XihePost.frag`
  + `XiheBigLaser.frag`（**直接复用 samples/XiheMagic 的 XiheBeam.frag**，大光柱 + MagicRing 3D 投影）
  （灰度/径向模糊/亮度/天空/闪白）；Boss 本体和 EOLEff 残影用原版贴图走 `PreDraw`。
- 声音：原版 `SoundID` + MEAC 的 `Laser.wav`；BGM 用 MEAC 的 `TakeFlight.mp3`（`MusicLoader` 注册）。

## 怎么打

进世界后打开 **Pylon Mod → 羲和 Boss** 页：

- 「召唤羲和」：在玩家上方 500px 生成；
- 「跳到阶段 2 / 3」「快进到终极技 (4450)」「快进到阶段 2 终盘 (9120)」：直接看后半段；
- 「移除羲和」：清场。

Boss 自带场地半径（2000 → 准备阶段 1200/750 → 900/750），玩家跑出圈会被拉回来；
Boss 强制白天、时间随血量推进（`Main.time` 2000 → 53700）。

## 数值（与 MEAC 完全一致）

| 项 | 值 |
|---|---|
| 血量 | 100000（专家 120000 / 大师 150000，再乘 bossAdjustment） |
| 伤害 / 防御 | 50 / 50 |
| 免击退、无重力、穿墙 | 是 |
| 开场 | `timerM` 1650 前无敌 + 名字乱码；1650 进入 Roar |
| 节拍 | `BeatTime(n) = n * 51.5` tick |
| 场地 | `realAreaRadius` 每 tick 向 `setAreaRadius` 插值 0.03 |
| DPS 上限 | 原版 `LimitDPS` 按 14820 tick 线性摊血；Pylon 没有命中改写钩子，**未接**（见差异） |

## 完整时间轴 / 招式

`timerM` 是绝对时钟；状态机是 MEAC 原样的 23 个 State。

| 时间 | 状态 | 行为 |
|---|---|---|
| 0–412 | Spawn | 灰屏；CrystalArrow 雨；919 长枪环；Boss 下移到玩家上方 300/450 |
| 412 | → Roar | |
| 1650 | Roar | 名字变「羲和」、Wave、RadialGlur(25)、Brightness(1.5)、30 发 EoLProj 环 |
| 1750–1802 | Roar 尾 | 每 15 tick RingWarp，震屏 10，镜头拉向玩家与 Boss 中点 |
| 2300 前 | Delay / SunDance1 | 归位；日轮激光环（10/12 发 ×3 轮，`EoLLaser1` maxTime 130/130/150） |
| 3300 线 | → EoLProjShoot1/2 | 弹幕雨：3 阶段分别 3/3/4 列、33/36/45 发环形爆发 |
| 之后 | Dash1 / DashSpin / Lance2 / VLaser / CrossRay / SunDance | 随机循环 |
| 3300（阶段 1） | Prepare | 场地 1200→750；9 发常驻激光（寿命到 3750）；3615 起双拍 EoLProj 雨；4000 RingWarp2；3920+ VLight |
| 4080 | Speacial1 | 终极技 1：EoLBigLaser 光柱（timeLeft 210）、左右激光雨、长枪雨、4330 的 24 发环、4350–4440 的 EoLProj 环、4450 Circle、4500 第二根大光柱（350）、4600 幂次扩散激光 |
| 4850 | Speacial2 | 隐身突进；100 tick 生成 `Cycloid`（阶段 2 变体）；930 后转场 |
| 转场 | Transition | 归位、三圈长枪、Wave、RadialGlur2、阶段 2 |
| 9120（阶段 2） | Prepare2 | 场地 1300；15 发常驻激光（寿命到 9540）；9420–9750 旋转 9 发弹幕雨；9815 18 发长枪环 |
| 9825 | Speacial3 | 场地 1200；60 tick 36 发 EoLProj 环 + `SunProj` 太阳；之后长枪/EoLProj 雨到 10600 |
| 10600 | Speacial2 | 循环 |
| 11451–11500 | 锁血 | 受击锁血 |
| 11501–11529 | OnHit | 触发 Speacial4：灰屏、RadialGlur2(40)、清 Cycloid |
| 11530–12200 | Speacial4 | 隐身→现身；CrystalArrow 雨；70 tick 一组 EoLProj；BeatTime(8..16) 的长枪墙 |
| 12350+ | 终盘 | 每拍长枪墙（13 发 × 325 tick）、BeatTime(8/12/16) 的 Wave + 33 发 EoLProj 环 |
| 血=1 | Leave | 上升、LightDust、EoLProj；结束时 20 发 CrystalArrow，Boss 死亡 |

> 详细的每一条 `Projectile.NewProjectile` 参数都在 `Content/XiheBossNpc.cs` 里，和 MEAC 源码逐行对应
> （文件顶部的状态名与 MEAC 的 `State` 枚举同名）。

## 画面为什么全是着色器

手机版没有 tModLoader 的 `PreDraw + SpriteBatch.End/Begin` 自由换混合模式的环境（项目里实测过
「不要 patch SpriteBatch / 不要中途 Begin」）。所以：

1. **Boss 本体**：`ModNpc.PreDraw` 里用原版贴图（`NPC_636` = `NPCID.HallowBoss`，
   `Extra[157..160]` 翅膀/手臂，`Extra[187/188]` 光点）按 MEAC `DrawEoL` 的顺序与缩放画；
   原版的 `HallowBoss` 滤镜（`XiHe_Wing` 当遮罩）无法在 SpriteBatch 里换 Effect，改成
   wing1/wing2 两层 + 屏幕光晕近似（见差异）。
2. **MEAC 弹幕绘制逐次搬**：不再用“光球/光柱”近似。`XiheSprite.frag` 按 XNA SpriteBatch 的
   位置/旋转/缩放/origin/源矩形采样，`XiheTrail.frag` 是 `MEAC/Effects/Trail.fx` 的 pass 0，
   `XiheRing.frag` 是 DrawArea/Circle 的环带，`XiheLine.frag` 是 Cycloid 的折线；
   颜色变换按 `MEAC/Effects/Colorize.fx`，混合按调用点 Additive/AlphaBlend 逐条复现。
   贴图是 MEAC 原素材 + 原版 `Projectile_919` / `Sun` 等，打进一张 1024 图集（`Fx/XiheTextures.cs`）。
   **EoLBigLaser 单独走 `XiheBigLaser.frag`**：就是 `samples/XiheMagic/assets/effects/XiheBeam.frag` 的
   拷贝，MagicRing 的逐像素 3D 投影反解原样保留；`Fx/XiheBigLaserFx.cs` 只负责喂
   uBeam/uBeamX/uImpact/uRings。
3. **屏幕后处理**：`XihePost.frag` 一个 shader 多个 mode：EoLWarp（Gray.fx 的 0.3/0.6/0.1 +
   EoLWarp.fx 的 Perlin 旋转位移）、RadialGlur/2（9 次缩放采样）、Brightness、EoLSky、闪白、
   Sun.fx（SunProj 的逐像素太阳）。
4. **相机**：MEAC 在 `Player.PreUpdate` 里改 `Main.screenPosition`；这里等价地在
   `Mod.ModifyScreenPosition` 里叠震屏/镜头推拉，世界与光效一起抖。
5. **音效/音乐**：`SoundID.Item159/160/161/163/164` 原样；BGM 是 MEAC 的 `TakeFlight.mp3`。

### 图元参数

| 图元 | 数据 |
|---|---|
| `Sprite` / `SpriteRect` | MEAC 的一次 `SpriteBatch.Draw`：世界中心、源矩形、颜色、旋转、缩放、origin、Additive、Colorize |
| `Trail` | MEAC `Trail.fx` pass 0：折线点（世界、半宽、`sqrt(factor)`、`coord.z`）、shape/color/overlay 贴图、`t`、混合 |
| `Ring2` | MEAC DrawArea/Circle：中心、半径、宽度、Wave 贴图、颜色、Additive |
| `Line` | MEAC Cycloid：折线点、半宽、颜色、Additive |

## 与 MEAC 的差异（都在平台能力边界上）

1. **命中/受伤管线**：Pylon 未接 `StrikeNPC` 改写与 `OldModifyHit*`，因此：
   - Boss 的 `LimitDPS`（14820 tick 摊血）没有生效；
   - 弹幕伤害走游戏原生 hitbox（`ModProjectile.CanDamage/Colliding` 已按源文件实现）。
2. **Boss 本体滤镜**：原版翅膀用 `GameShaders.Misc["HallowBoss"]` + `XiHe_Wing` 遮罩；
   Pylon 里不能在绘制中途换 Effect，改成两层贴图 + 屏幕光晕。
3. **EOLEff 残影**：原版是 3D 旋转 + 投影的翅膀/身体剪影；这里保留「同一帧条的翅膀+身体」精灵，
   3D 投影改成平面螺旋（`VfxKind.EolEff` 里的 `VAngle`/`Dis`）。
4. **MagicRing**（EoLBigLaser 的符文环）：大光柱走 `XiheBigLaser.frag`，就是 XiheMagic 的
   XiheBeam.frag —— 逐像素的 3D 投影反解与原版一致；`XiheFx` 的平面环版本只作为
   着色器加载失败时的 fallback。
5. **Cycloid**：逐点摆线/外圈已按源 `Create` 参数方程画成加色折线；原版的抓屏反色 + Hide
   遮罩在 Pylon 里不能读 `Main.screenTargetSwap`，仍用闪白 + 径向模糊近似这层屏幕效果。
6. **符文/太阳**：`SunProj` 的 `Sun.fx` 已逐像素搬进 `XihePost.frag`；源 `OnSpawn` 的 25 个
   `MagicRing_Sun` 3D 环尚未单独建类型（太阳本体的日珥/光线已由 Sun.fx 表达）。
7. **EoLFire 的节点**：源 `MEAC.Entities.Node` 不在源码提取树里，按调用点
   `new Node(center, velocity*10, 10, 50f, 1){type=2}` 还原为
   `position += velocity; velocity *= 0.9; timeleft--`（文件头标注为最大不确定点）。
8. **EoLLaser1 颜色**：源 `PreDraw` 实际用固定 `HsvToRgb(45,1,0.5)`，这里也固定 45°。
9. **天幕**：MEAC 是自定义 `CustomSky`；Pylon 没有天空钩子，做成屏幕叠加（黑幕 + TSSky1 +
   太阳），不挡世界；入场黑白滤镜在天空之后应用，所以黑白是整屏的。
10. **联机**：AI 在生成端跑，状态用 `SendExtraAI/ReceiveExtraAI` 同步；弹幕靠原生同步。
    手机版一般单机，联机路径未实测。

## 构建 / 安装

```powershell
dotnet build samples\XiheBoss\XiheBoss.csproj -c Debug -m:1 -p:RestoreUseStaticGraphEvaluation=true
```

把 `bin\Debug\net10.0\XiheBoss.dll`、`manifest.json` 和整个 `assets\` 放到：

```text
/storage/emulated/0/Android/data/dev.pylon.loader/Mods/XiheBoss/
    XiheBoss.dll
    manifest.json
    assets/effects/XihePrims.frag
    assets/effects/XihePost.frag
    assets/effects/XiheSprite.frag
    assets/effects/XiheTrail.frag
    assets/effects/XiheRing.frag
    assets/effects/XiheLine.frag
    assets/effects/XiheBigLaser.frag
    assets/textures/*.png
    assets/sounds/Laser.wav
    assets/music/TakeFlight.mp3
```

重启游戏，进世界后从 ImGui 页召唤。


## 验证状态

- `dotnet build samples\XiheBoss\XiheBoss.csproj -c Debug`：**0 警告 / 0 错误**（net10.0，2026-10-25）。
- 所有 `assets/effects/*.frag` 用 `glslangValidator -S frag`（GLSL ES 3.00）验证通过；
  真机本轮已把 DLL/资产推到 Mods/XiheBoss，需重启游戏后看日志：
  `[fx] 首次提交：prims=Ready post=Ready sprite=Ready trail=Ready ring=Ready`、
  `[tex] 图集 ...`、`[music] TakeFlight slot = ...`。若出现 `Failed`，`ScreenShader.Error` 会带 GLSL 行号。
- 复刻依据：`MEAC.dll` 的源码工程（`MEAC.Bosses.EoL.XiHe` 与 9 个弹幕 + VisualEffect 组），
  数值逐行对照；未靠猜测补行为。

## 目录

```text
XiheBoss/
  XiheBossMod.cs            入口：着色器/贴图/音效、屏幕效果提交、相机、菜单
  XiheBossImGuiTab.cs       召唤 / 阶段跳转 / 状态显示
  XiheHelper.cs             MEAC Helper 的数学移植
  Content/
    XiheBossNpc.cs          Boss 本体（23 状态 + 绘制 + 场地）
    Projectiles/*.cs        9 种弹幕（AI 1:1 + PushVisual）
  Fx/
    XiheFx.cs               图元收集 / 分 pass / 后处理 / 相机
    XiheBigLaserFx.cs       EoLBigLaser 专用：复用 XiheBeam.frag 的大光柱 + 3D MagicRing
    XiheVisuals.cs          VisualEffect 组（EOLEff/Wave/RingWarp/VLight/Circle/…）
    XiheTextures.cs         PNG 解码 + 1024 图集
    XiheSounds.cs           Laser.wav
  assets/effects/*.frag     GLSL（XiheSprite / XiheTrail / XiheRing / XiheLine / XihePost / XiheBigLaser）
  assets/textures/*.png     MEAC 原素材 + 原版 Projectile_919
  assets/sounds/Laser.wav   MEAC 原素材
  assets/music/TakeFlight.mp3
```





