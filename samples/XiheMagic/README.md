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
| 魔力 | timeLeft<190 后每 tick `CheckMana(10, true, false)`，扣不动就断 | 相同 |
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
  - `MagicCircle/tex1.png`：三个 MagicRing 的环带；
  - `Circle.png`：落点柔光；
  - `Laser.wav`：点火音效。
- 颜色映射复刻 `MEAC/Effects/Colorize.fx` 的 `colorize()`（饱和度 0.8、`uOffset=0`）；
- 天幕是屏幕空间的近似（MEAC 的 `AmazingBugSky` 是自绘天空，Pylon 没有自定义天空钩子）。

### 时序（与 MEAC 对齐）

| 时间 | 画面 |
|---|---|
| 引导中 | 角色上下各一道彩色半圆 + 十字辉光 |
| 柱生成 age 0 | 第 1 个 MagicRing 在柱心（mouse−300）展开 |
| age 20 | 第 2 个环（柱心下方 200px）+ 细柱开始出现（scale 0.002→0.16） |
| age 40 | 第 3 个环（下方 400px） |
| age 110（timeLeft 250） | 点火：闪白 + Laser.wav，再补三个大 MagicRing，柱快速变粗到 ~3.16（判定半宽 scale×90） |
| 之后 | 持续粗柱 + 落点柔光 + 震屏；松手后 20 tick 内收缩消失 |

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
dotnet build samples\XiheMagic\XiheMagic.csproj -c Debug -m:1 -p:RestoreUseStaticGraphEvaluation=true
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

1. **画面实现**：XNA `.fx` + 自绘 SpriteBatch → Pylon ScreenShader + 原贴图；观感按原参数对齐，
   但环/天幕是屏幕空间近似（原版是 3D 投影环与自定义天空）。
2. **按住引导**：手机版没有 `useLimitPerAnimation`，改用 `channel=true + autoReuse=true`；
   控制器每帧检查 `player.controlUseItem`，松手行为与原版一致。
3. **联机**：控制器/激光柱走游戏原生弹幕同步；画面在每台客户端本地按弹幕状态绘制。
   控制器只在拥有者本地发射，其他客户端看到的是同步后的结果。
4. **性能**：效果只在有控制器/激光柱/闪白时提交；没有状态时不跑全屏 pass。
