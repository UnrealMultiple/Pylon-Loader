# 羲和魔杖（XiheMagic）

MEAC `XiHeMagicItem`（羲和魔杖）的 1:1 复刻，跑在 Pylon Loader 上（Terraria 手机版 1.4.5.8.6）。

## 使用效果

与 MEAC 的原实现同一条链路、同一组数值：

| 阶段 | MEAC | 这里 |
|---|---|---|
| 物品 | `useStyle=14`（RaiseLamp）、`holdStyle=6`（HoldLamp）、魔法 200 伤害、30 tick | 相同 |
| 引导 | `XiheMagicControler` 隐藏弹幕跟随玩家；alpha 每 tick +0.01 | 相同 |
| 蓄力 | 准星 X 以 0.01 插值跟随鼠标，天穹目标在玩家上方 1500 | 相同 |
| 降柱 | 引导 200 tick（timeLeft 500→300）后在鼠标上方 300 处生成激光柱 | 相同 |
| 激光柱 | `timeLeft=360`；340→260 缓慢变粗；250 点火；250→230 快速变粗 | 相同 |
| 持续 | timeLeft<230 时只要控制器还活着就 `timeLeft++`，并跟随鼠标 X（0.005） | 相同 |
| 断引导 | 控制器消失 → 激光柱 `timeLeft=20` → 每 tick 缩 0.15 | 相同 |
| 魔力 | timeLeft<190 后每 tick `CheckMana(10, true, false)`（= 扣 `10 * manaCost`），扣不动就断 | 相同（见下） |
| 判定 | `|目标中心X - 柱心X| < scale*90`；`CanDamage = scale>0.2`；局部无敌 3 tick | 相同 |
| 音效 | `MEAC/Sounds/Customs/Laser.wav` | 直接使用该文件 |

## 为什么画面是着色器

手机版没有 tModLoader 的 `PreDraw` + `SpriteBatch.End/Begin` 自由换混合模式的环境，项目里也已经实测过
「不要 patch SpriteBatch / 不要在绘制中途乱 Begin」。所以激光柱、蓄力半圆、三个 MagicRing、天幕、震屏、闪白
都放进 **Pylon 的屏幕效果管线**（`World` 层：世界之后、界面之前）：

- `assets/effects/XiheBeam.frag`：一次全屏 pass，先采样 `uScreen`，再把光效加回去；
- 贴图全部是 **MEAC 原素材**（从本机 MEAC 文件夹复制，不是新画的）：
  - `tex3.png` / `tex0.png`：MEAC `EoLBigLaser_Friendly` 的外层/内层柱贴图；
  - `Arc.png`：蓄力期角色上下的半圆只取它的亮度细节（不再出现矩形边）；
  - `Extra_89.png`：十字辉光；
  - `MagicCircle/tex1.png`：三个 MagicRing 的环带（600x70，sX=3，原图直接用）；
  - `Laser.wav`：点火音效。
- 落点柔光是 shader 里算的高斯衰减（`exp(-ir * k)`），不再用贴图；
- 颜色映射复刻 `MEAC/Effects/Colorize.fx` 的 `colorize()`（饱和度 0.8、`uOffset=0`）；
- 天幕是屏幕空间的近似（MEAC 的 `AmazingBugSky` 是自绘天空，Pylon 没有自定义天空钩子）。

### MagicRing 的 3D 投影（与 MEAC 逐顶点一致）

MEAC 的环不是画一个椭圆：`MagicRing.Draw` 自己拼三角带，顶点走的是

```text
rot    = RotY(UnitX, θ) * 110 * scale * ai0          // Helper.Vec3RotBy 的 Y 矩阵（行向量口径）
vertex = center + RotZ(rot + (0, ∓width), ai1)        // ai1 = 0，width = 10 * scale * ai0
screen = Projection(vertex, scCenter, viewZ = 1000)   // p = S + k*(D + q.xy)，k = 1000/(1000 - q.z)
```

所以固定 θ 时环带在屏幕上是**一条竖线**：`x = k(Dx + R cosθ)`、`y = k(Dy ± w)`；
近侧 `k>1` 环带被放大、远侧 `k<1` 被压窄，环心离屏幕中心越远"椭圆"越高——立体感全来自这里。

这里把这个投影**逐像素反解**（`R cosθ + (X R / 1000) sinθ = X - Dx`，最多两个解，前后两段），
再按 `k` 取环带的 v 去采样 tex1，所以形状、近粗远细、透视位移都和原版一致，而不是屏幕空间的扁平近似。
环带亮度也照原版：加色混合下 `tex.rgb * tex.a * kc²`（`kc = 顶点色 = 白 * k`，上边那个顶点再 +0.05）。
环带**外**只补一层很窄的 `HsvToRgb(50)` 金色 bloom；注意 bloom 绝不能把纹理自带的 4px 白描边再刷一遍
（那样 8px 的细线会被糊成 20~40px 的白带，看起来就是「白色粗边」）。环带中间的黑底保持透明，
所以整圈只有两条细描边 + 符文，和 MEAC 一致。

### 环的时序（与 MEAC 的状态机一致）

环是**独立视觉特效**，不是柱子画面的一部分：柱子只在固定时刻 `CreateRing`，
之后每个环自己按 `MagicRing.AI` 的 `SmoothStep(scale, 4/5/0, 0.2/0.35)` 展开、收尾，柱子断了还会自己收完。
`Fx/XiheFx.cs` 里的 `RingFx` 就是这套状态机（每 tick 一步，只有 `age` 用来触发生成）：

| 柱子 timeLeft / age | CreateRing(偏移, ai0, 寿命) |
|---|---|
| 360 / 0 | (0, 0.7, 110) |
| 340 / 20 | (200, 0.4, 90) |
| 320 / 40 | (400, 0.9, 70) |
| 250 / 110 | (0, 0.85, 250)、(200, 0.6, 250)、(400, 1.1, 250)，laserOwner = 柱子 |

> 注意 `MagicRing.AI` 的 SmoothStep 每 tick 只走 0.2（Hermite 后约 10.4%），所以 20 tick 后
> scale 停在 **≈3.556**（不是 4）；柱子续命时 `timeLeft` 会一直卡在 229/230，环就保持 3.556。
> 柱子死后环再涨到 5 然后缩掉，这段在柱子的 `timeLeft` 里看不到，所以必须独立模拟。

### 时序（与 MEAC 对齐）

| 时间 | 画面 |
|---|---|
| 引导中 | 角色上下各一道彩色半圆 + 十字辉光 |
| 柱生成 age 0 | 第 1 个 MagicRing 在柱心（mouse−300）展开 |
| age 20 | 第 2 个环（柱心下方 200px）+ 细柱开始出现（scale 0.002→0.16） |
| age 40 | 第 3 个环（下方 400px） |
| age 110（timeLeft 250） | 点火：闪白 + Laser.wav，再补三个大 MagicRing，柱快速变粗到 ~3.16（判定半宽 scale×90） |
| 之后 | 持续粗柱 + 落点柔光 + 震屏；松手后 20 tick 内收缩消失 |
| 柱死后 | 三个大环再涨到 5 并缩掉（约 15 tick），这段柱子已经不存在，由环自己播完 |

震屏照 MEAC `EffectPlayer.ScreenShake`：柱子的 `timeLeft` 在 245..20 期间每 tick 调
`ScreenShake(8f, 柱心)`，强度按「柱心到屏幕中心距离 / 2000」线性衰减，`screenShake` 每 tick 减 1；
应用方式是给相机（`Main.screenPosition`）加一个**随机方向**的位移，所以世界、光柱、环**一起抖**，
每 tick 换一次方向。这里在 shader 里把同一个随机位移加到所有 uv 上（不是只抖背景）。

| 阶段 | 行为 |
|---|---|
| 245..20 | 每 tick 随机方向抖动，幅度 `8 * (1 - dist/2000)` |
| 其他时间 | 不抖（闪白不再额外加抖，原版没有） |

扣蓝：原版是 `player.CheckMana(10, true, false)`（`pay=true` 扣 `10 * manaCost`）。
这条在 IL2CPP 的托管侧调过去时 `pay` 没生效（只扣了第一次），所以这里**照原版公式自己扣**：
`cost = (int)(10 * player.manaCost)`，`statMana < cost` 才断；也不调 `ManaEffect`
（原版没有，每 tick 调会一直刷蓝量特效）。魔力花自动喝药暂无（Pylon 侧要走 `CheckMana`，等真机能过再补）。

> 手机版 `ScreenGraphics.Texture` 会调 `GetRawTextureData<T>` 的泛型实例，真机 AOT 没有生成、直接抛
> `ExecutionEngineException`；所以 `Fx/XiheTextures.cs` 自己用 `System.IO.Compression` 解 PNG 成 RGBA，
> 再走内核的 `PylonFx_TextureLoad`（Bunny 的做法是内核直接按路径解码）。

## 物品图标

MEAC 里这件武器直接借用原版 **Item_4952（光之女皇魔法武器）** 的贴图。这里不复制游戏美术：
`PostSetupContent` 把本物品的贴图槽指向 `TextureAssets.Item[ItemID.FairyQueenMagicItem]`，
所以进游戏后图标与 MEAC 完全一致；只有原版贴图拿不到时才退回 `Arc.png`。

## 获得方式

- 配方：`1× 阳炎之怒（Sunfury） + 20× 日耀碎片 + 10× 金锭`，在远古操纵机制作；
- 或者打开游戏里的 **Pylon Mod → 羲和魔杖** 页，点「添加到背包」。

## 构建 / 安装

```powershell
dotnet build src\csharp\samples\XiheMagic\XiheMagic.csproj -c Debug -m:1 -p:RestoreUseStaticGraphEvaluation=true
```

把 `bin\Debug\net10.0\XiheMagic.dll` 和整个 `assets\` 放到设备：

```text
/storage/emulated/0/Android/data/dev.pylon.loader/Mods/XiheMagic/
    XiheMagic.dll
    manifest.json
    assets/...
```

重启游戏，在 Mod 的 ImGui 页点按钮拿一把即可。

## 说明 / 与 MEAC 的差异

1. **画面实现**：XNA `.fx` + 自绘 SpriteBatch → Pylon ScreenShader + 原贴图；数值与几何按原参数对齐
   （MagicRing 是逐像素反解原版 3D 投影，见上），只有天幕是屏幕空间近似（原版是自定义天空）。
2. **按住引导**：手机版没有 `useLimitPerAnimation`，改用 `channel=true + autoReuse=true`；
   控制器每帧检查 `player.controlUseItem`，松手行为与原版一致。
3. **联机**：控制器/激光柱走游戏原生弹幕同步；画面在每台客户端本地按弹幕状态绘制。
   控制器只在拥有者本地发射，其他客户端看到的是同步后的结果。
4. **性能**：效果只在有控制器/激光柱/环/闪白时提交；没有状态时不跑全屏 pass。环每个最多解两个 θ 解，
   并且带包围盒剔除，屏幕大部分像素在解方程前就被跳过。