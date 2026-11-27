using System;
using System.Numerics;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.NaviteAPI.ImGui;
using Terraria;
using Terraria.Map;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using SpriteBatch = Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch;
using XnaVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;
using Matrix = Il2CppMicrosoft.Xna.Framework.Matrix;

namespace CheatMenu.Infra;

/// <summary>
/// 世界编辑公共支撑（地图刷子 / 区域复制粘贴共用）：
/// 瞄准点与坐标换算（世界↔覆盖层物理像素、全屏小地图）、预览色（油漆应用）、
/// ImGui 绘制小工具（背景绘制列表，物理像素）、TileManipulation / 伪位置 / 伪物品栏发包。
/// 1:1 对应原版 <c>WorldEditCommon.hpp</c>。
///
/// 坐标系约定（与原版一致，不要混用）：
/// <list type="bullet">
/// <item><c>WorldToScreen</c>：世界像素 → ImGui 覆盖层**物理像素**，
/// 直接喂给 <see cref="ImDrawListPtr"/> 的 AddRect/AddRectFilled/AddCircleFilled。</item>
/// <item><c>WorldToScreenFullscreenMap</c>：世界像素 → **全屏小地图**物理像素，
/// 只在 <c>Main.mapFullscreen</c> 时用。</item>
/// </list>
/// 原版用 <c>ImGui::GetBackgroundDrawList()</c> 画这两套坐标；Pylon 侧对应
/// <c>ModOverlay.Register(..., background: true)</c>。**不要再喂给 SpriteBatch**，
/// 那是游戏逻辑分辨率坐标，和这里的物理像素差一个缩放。
/// </summary>
internal static class WorldEdit
{
    // ==================== 瞄准点（原版 WorldEdit::GetAimWorldPos） ====================

    /// <summary>
    /// 瞄准点（世界像素）= <c>Main.MouseScreen + Main.screenPosition</c>。
    ///
    /// 原版 C++ 早期用 <c>Main.MouseWorld</c>，但手机版准星会被「准星离角色最大距离」
    /// 钳制（拖不到远处、也拖不到屏幕边缘），于是 WorldEditCommon.hpp 的 BoulderTouch
    /// 路径改成官方触摸映射 <c>MouseScreen + screenPosition</c>（反编译确认
    /// <c>get_MouseWorld()</c> 内部就是这两项相加，所以世界坐标语义完全一致，
    /// 但没有那道距离钳制，位置与可见画面严格对应）。
    /// </summary>
    public static XnaVector2 AimWorldPos()
    {
        var screen = Main.MouseScreen;      // 只读属性（dump.cs 51751）
        var origin = Main.screenPosition;   // 可读写（dump.cs 51579）
        return new XnaVector2(screen.X + origin.X, screen.Y + origin.Y);
    }

    /// <summary>瞄准的格子坐标（原版 WorldEdit::AimTilePos）。</summary>
    public static XnaVector2 AimTilePos()
    {
        var w = AimWorldPos();
        return new XnaVector2(MathF.Floor(w.X / 16f), MathF.Floor(w.Y / 16f));
    }

    /// <summary>
    /// 玩家正按住世界（原版 WorldEdit::IsWorldHeld 的 C# 等价）。
    /// 原版还要求 Unity 触摸数组里有活动手指；Pylon 侧游戏已把触摸映射成
    /// mouseLeft/worldMouseLeft，两个都认即可（点 ImGui 窗口不置这两个位）。
    /// </summary>
    public static bool IsWorldHeld()
    {
        if (Main.gameMenu) return false;
        return Main.mouseLeft || Main.worldMouseLeft;
    }

    // ==================== 坐标 ====================

    /// <summary>世界像素 → 覆盖层屏幕坐标（XNAUnityRunner.AdjustWorldForRealScreen，物理像素）。</summary>
    public static XnaVector2 WorldToScreen(XnaVector2 world) =>
        XNAUnityRunner.AdjustWorldForRealScreen(world);

    /// <summary>同上，ImGui 侧向量重载（避免各 Feature 到处转类型）。</summary>
    public static Vector2 WorldToScreen(Vector2 world)
    {
        var p = XNAUnityRunner.AdjustWorldForRealScreen(new XnaVector2(world.X, world.Y));
        return new Vector2(p.X, p.Y);
    }

    /// <summary>
    /// 世界像素 → 全屏小地图屏幕坐标：((world格 - mapFullscreenPos) * scale) + 屏幕中心。
    /// scale 无效返回 (0,0)，不做估算兜底（原版同样「失败即失败」）。
    /// </summary>
    public static XnaVector2 WorldToScreenFullscreenMap(XnaVector2 worldPx, float offsetX = 0f, float offsetY = 0f)
    {
        var scale = Main.mapFullscreenScale;
        if (scale <= 0f) return default;
        var pos = Main.mapFullscreenPos;
        var display = ImGui.GetIO().DisplaySize;
        return new XnaVector2(
            (worldPx.X / 16f - pos.X) * scale + display.X * 0.5f + offsetX,
            (worldPx.Y / 16f - pos.Y) * scale + display.Y * 0.5f + offsetY);
    }

    /// <summary>同上，ImGui 侧向量重载。</summary>
    public static Vector2 WorldToScreenFullscreenMap(Vector2 worldPx, float offsetX = 0f, float offsetY = 0f)
    {
        var p = WorldToScreenFullscreenMap(new XnaVector2(worldPx.X, worldPx.Y), offsetX, offsetY);
        return new Vector2(p.X, p.Y);
    }

    public static bool InWorld(int x, int y) =>
        x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY;

    /// <summary>
    /// 图格句柄 <c>Main.tile[x, y]</c>。Terraria.Tile 在手机版里只是个 4 字节句柄
    /// （dump.cs 71166：实例字段只有 <c>_tileOffset</c>），真正的位数据都在
    /// TileData 的原生数组里，所以句柄上的语义 getter/setter 读写的是世界本体
    /// （框架自己的存档管线 Migration 也这么用：TileSavePipeline 里
    /// <c>var cell = tile[x, y]; cell.wall = type; cell.wallColor(...)</c>）。
    /// 调用前必须 <see cref="InWorld"/> 检查，越界会取到别的图格。
    /// </summary>
    public static Tile TileAt(int x, int y) => Main.tile[x, y];

    // ==================== 预览色（原版 IM_COL32 常量） ====================

    /// <summary>
    /// <c>IM_COL32(r, g, b, a)</c> 的等价实现。ImGui 的 ImU32 内存布局是 RGBA 字节序，
    /// 数值上即 <c>a&lt;&lt;24 | b&lt;&lt;16 | g&lt;&lt;8 | r</c>（不是 0xAARRGGBB）。
    /// 这里直接位运算（不用 ImGui.ColorConvertFloat4ToU32）是为了在逐格预览循环里
    /// 不产生原生调用：绘制热路径每帧要打包几千个颜色。
    /// </summary>
    public static uint ColorU32(byte r, byte g, byte b, byte a) =>
        (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    /// <summary>刷子世界内预览：IM_COL32(255, 0, 0, 130)。</summary>
    public static readonly uint PreviewRed = ColorU32(255, 0, 0, 130);

    /// <summary>刷子全屏地图预览：IM_COL32(255, 0, 0, 140)。</summary>
    public static readonly uint PreviewRedMap = ColorU32(255, 0, 0, 140);

    /// <summary>复制选区填充：IM_COL32(255, 0, 0, 50)。</summary>
    public static readonly uint SelectFill = ColorU32(255, 0, 0, 50);

    /// <summary>复制选区描边：IM_COL32(255, 0, 0, 220)。</summary>
    public static readonly uint SelectStroke = ColorU32(255, 0, 0, 220);

    /// <summary>已复制内容预览框：IM_COL32(50, 255, 50, 220)。</summary>
    public static readonly uint CopiedStroke = ColorU32(50, 255, 50, 220);

    /// <summary>粘贴中进度框：IM_COL32(255, 255, 0, 240)。</summary>
    public static readonly uint PasteStroke = ColorU32(255, 255, 0, 240);

    // ==================== ImGui 绘制小工具（背景层，物理像素） ====================
    // 每个都对应原版 WorldEdit 里的一次 dl->AddXxx；坐标一律先经 WorldToScreen。
    // 热路径不分配：只用栈上 Vector2，不 new 容器、不拼字符串。

    /// <summary>世界内单格填充（原版 <c>dl->AddRectFilled(p1, p2, col)</c>，详细预览用）。</summary>
    public static void FillWorldTile(ImDrawListPtr dl, int tx, int ty, uint col)
    {
        var a = WorldToScreen(new XnaVector2(tx * 16f, ty * 16f));
        var b = WorldToScreen(new XnaVector2((tx + 1) * 16f, (ty + 1) * 16f));
        dl.AddRectFilled(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), col);
    }

    /// <summary>世界内区块填充（复制粘贴的彩色色块预览用）。</summary>
    public static void FillWorldTiles(ImDrawListPtr dl, int x, int y, int w, int h, uint col)
    {
        if (w <= 0 || h <= 0) return;
        var a = WorldToScreen(new XnaVector2(x * 16f, y * 16f));
        var b = WorldToScreen(new XnaVector2((x + w) * 16f, (y + h) * 16f));
        dl.AddRectFilled(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), col);
    }

    /// <summary>世界内区块描边（选区红框 / 已复制绿框 / 粘贴黄框）。</summary>
    public static void StrokeWorldTiles(ImDrawListPtr dl, int x, int y, int w, int h, uint col, float thickness)
    {
        if (w <= 0 || h <= 0) return;
        var a = WorldToScreen(new XnaVector2(x * 16f, y * 16f));
        var b = WorldToScreen(new XnaVector2((x + w) * 16f, (y + h) * 16f));
        dl.AddRect(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), col, 0f, 0, thickness);
    }

    /// <summary>
    /// 世界内实心圆（原版简单预览：AddCircleFilled）。
    /// 半径**不用世界像素直接算**，而是拿两次 WorldToScreen 的实测距离 ——
    /// 相机有缩放（手机版还受 UI 缩放影响），只有实测才能和画面一致：
    /// <code>
    /// a = WorldToScreen(aim);
    /// b = WorldToScreen(aim + Vector2(offsetPx, 0));
    /// r = length(b - a);
    /// </code>
    /// 原版传进来的 offsetPx 是 <c>brushDiameter + 16</c>，半径就取这个实测距离本身。
    /// </summary>
    public static void FillWorldCircle(ImDrawListPtr dl, float worldX, float worldY, float offsetPx, uint col, int segments)
    {
        var a = WorldToScreen(new XnaVector2(worldX, worldY));
        var b = WorldToScreen(new XnaVector2(worldX + offsetPx, worldY));
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var radius = MathF.Sqrt(dx * dx + dy * dy);
        if (radius <= 0f) return;
        dl.AddCircleFilled(new Vector2(a.X, a.Y), radius, col, segments);
    }

    /// <summary>全屏小地图单格填充（原版 WorldEdit::DrawMapTileRect）。</summary>
    public static void FillMapTile(ImDrawListPtr dl, int tx, int ty, uint col)
    {
        var a = WorldToScreenFullscreenMap(new XnaVector2(tx * 16f, ty * 16f));
        var b = WorldToScreenFullscreenMap(new XnaVector2((tx + 1) * 16f, (ty + 1) * 16f));
        dl.AddRectFilled(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), col);
    }

    /// <summary>
    /// 全屏小地图实心圆（原版地图预览的简单模式）：
    /// 半径 = |map(aim + offset).x - map(aim).x| * 0.5（同样靠实测，不猜 mapFullscreenScale）。
    /// </summary>
    public static void FillMapCircle(ImDrawListPtr dl, float worldX, float worldY, float offsetPx, uint col, int segments)
    {
        var a = WorldToScreenFullscreenMap(new XnaVector2(worldX, worldY));
        var b = WorldToScreenFullscreenMap(new XnaVector2(worldX + offsetPx, worldY));
        var radius = MathF.Abs(b.X - a.X) * 0.5f;
        if (radius <= 0f) return;
        dl.AddCircleFilled(new Vector2(a.X, a.Y), radius, col, segments);
    }

    // ==================== 预览色（油漆/涂层） ====================

    /// <summary>油漆着色；paint&lt;=0 或越界返回白色（不染色）。</summary>
    public static Color PaintTintForPreview(int paint)
    {
        if (paint <= 0) return new Color(255, 255, 255, 255);
        var paints = PaintColors();
        if (paint >= paints.Length) return new Color(255, 255, 255, 255);
        var c = paints[paint];
        return new Color(c.R, c.G, c.B, 255);
    }

    private static Il2CppStructArray<Color> _paints;

    /// <summary>WorldGen.paintColor(0..31) 懒加载缓存。</summary>
    private static Il2CppStructArray<Color> PaintColors()
    {
        if (_paints is not null) return _paints;
        var paints = new Il2CppStructArray<Color>(32);
        for (var i = 0; i < 32; i++)
        {
            try { paints[i] = WorldGen.paintColor(i); }
            catch { paints[i] = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255); }
        }
        _paints = paints;
        return paints;
    }

    /// <summary>移植 TerraAngel GetTileColor 的油漆应用（Shadow/Negative/Illuminant 特判）。</summary>
    public static void ApplyPaintForPreview(ref float r, ref float g, ref float b, int paint)
    {
        var paints = PaintColors();
        switch (paint)
        {
            case 29:    // ShadowPaint
            {
                if (paints.Length <= 29) break;
                var num = r / 255f;
                var num2 = g / 255f;
                var num3 = b / 255f;
                if (num2 > num) (num, num2) = (num2, num);
                if (num3 > num) (num, num3) = (num3, num);
                var num7 = num3 * 0.3f;
                r = paints[29].R * num7;
                g = paints[29].G * num7;
                b = paints[29].B * num7;
                break;
            }
            case 30:    // NegativePaint
                r = (255 - r) / 2f;
                g = (255 - g) / 2f;
                b = (255 - b) / 2f;
                break;
            case 31:    // IlluminantPaint
                r = g = b = 255f;
                break;
            case 0:
                break;
            default:
            {
                if (paint >= paints.Length) break;
                var num = r / 255f;
                var num2 = g / 255f;
                var num3 = b / 255f;
                if (num2 > num) num = num2;
                if (num3 > num) num = num3;
                r = paints[paint].R * num;
                g = paints[paint].G * num;
                b = paints[paint].B * num;
                break;
            }
        }
    }

    /// <summary>方块 (type, paint) → 预览 RGB；失败返回 false（中性灰）。</summary>
    public static bool TryGetTilePreviewColor(int type, int paint, out byte r, out byte g, out byte b)
    {
        r = g = b = 128;
        var tl = MapHelper.tileLookup;
        var cl = MapHelper.colorLookup;
        if (type < 0 || tl is null || cl is null || type >= tl.Length)
            return false;

        var idx = tl[type];
        if (idx >= cl.Length) return false;

        var c = cl[idx];
        float rF = c.R, gF = c.G, bF = c.B;
        ApplyPaintForPreview(ref rF, ref gF, ref bF, paint);
        r = (byte)Math.Clamp((int)rF, 0, 255);
        g = (byte)Math.Clamp((int)gF, 0, 255);
        b = (byte)Math.Clamp((int)bF, 0, 255);
        return true;
    }

    /// <summary>墙 (type, paint) → 预览 RGB。</summary>
    public static bool TryGetWallPreviewColor(int type, int paint, out byte r, out byte g, out byte b)
    {
        r = g = b = 128;
        var wl = MapHelper.wallLookup;
        var cl = MapHelper.colorLookup;
        if (type <= 0 || wl is null || cl is null || type >= wl.Length)
            return false;

        var idx = wl[type];
        if (idx >= cl.Length) return false;

        var c = cl[idx];
        float rF = c.R, gF = c.G, bF = c.B;
        ApplyPaintForPreview(ref rF, ref gF, ref bF, paint);
        r = (byte)Math.Clamp((int)rF, 0, 255);
        g = (byte)Math.Clamp((int)gF, 0, 255);
        b = (byte)Math.Clamp((int)bF, 0, 255);
        return true;
    }

    // ==================== 网络包 ====================

    /// <summary>TileManipulation（17 号包）：op=操作，x/y，data=ushort，data2=byte。</summary>
    public static void SendTileManipulation(int op, int x, int y, int data = 0, int data2 = 0)
    {
        if (Main.netMode != 1) return;
        NetMessage.SendData(17, -1, -1, null, op, (float)x, (float)y, (float)data, data2, 0, 0);
    }

    /// <summary>伪物品栏 SyncEquipment（海绵/水桶模拟）：写 InventorySlot 包。</summary>
    public static void SendFakeInventorySlot(int slot, int itemId, int stack = 1, int prefix = 0)
    {
        if (Main.netMode != 1) return;
        var packet = new RawPacket();
        packet.Packet(5, w =>
        {
            w.Write((byte)slot);
            w.Write((short)stack);
            w.Write((byte)prefix);
            w.Write((short)itemId);
        });
        packet.Send();
    }

    /// <summary>恢复本地玩家槽位物品（发包序列结尾）：SyncEquipment 全量写本机物品。</summary>
    public static void ResetInventorySlot(int slot)
    {
        if (Main.netMode != 1) return;
        var self = Game.LocalPlayer;
        var item = self?.inventory[slot];
        if (item is null) return;
        SendFakeInventorySlot(slot, item.type, item.stack, item.prefix);
    }

    // ==================== 真实贴图预览（SpriteBatch，屏幕像素） ====================

    /// <summary>
    /// 单位矩阵批次：等价原版 <c>SB_BeginWorld</c>（<c>SpriteBatch.Begin(SpriteSortMode.Deferred,
    /// null, null, null, null, null, Nullable&lt;Matrix&gt;{无值}, deferredBatch: true)</c>）。
    /// 无变换 = Identity → 批次坐标就是帧缓冲**逻辑像素**，与原版注释里的口径一致。
    /// 注意 <c>Nullable&lt;Matrix&gt;</c> 必须「无值」；给一个零矩阵会让所有顶点被压成 0。
    /// </summary>
    public static bool BeginWorldBatch(SpriteBatch batch)
    {
        if (batch is null || batch._beginCalled) return false;
        try
        {
            batch.Begin(SpriteSortMode.Deferred, null, null, null, null, null,
                default(Il2CppSystem.Nullable<Matrix>), true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>结束自建批次（原版 <c>SB_End</c>）。</summary>
    public static void EndWorldBatch(SpriteBatch batch)
    {
        if (batch is null) return;
        try { batch.End(); }
        catch { /* 批次坏了就别再抛 */ }
    }

    /// <summary>
    /// 画一格真实方块贴图（原版 <c>SB_DrawFast(TextureAssets.Tile, pos, {frameX, frameY, 16, 16}, tint)</c>）。
    /// <paramref name="x"/>/<paramref name="y"/> 是**批次空间**坐标（单位矩阵批次 = 帧缓冲逻辑像素），
    /// 由调用方按「物理像素 × 逻辑/物理」算好；这里不再碰 <c>Main.screenPosition</c>。
    /// 贴图没加载好就先请求加载并返回 false。
    /// </summary>
    public static bool DrawTileTextureAt(SpriteBatch batch, float x, float y, int type, short frameX, short frameY, int paint)
    {
        if (batch is null || type <= 0) return false;
        var assets = Terraria.GameContent.TextureAssets.Tile;
        if (assets is null || type >= assets.Length) return false;

        Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D texture;
        try
        {
            var asset = assets[type];
            if (asset is null || asset.State != ReLogic.Content.AssetState.Loaded)
            {
                try { Main.instance.LoadTiles(type); } catch { /* 加载失败就跳过这一格 */ }
                return false;
            }
            texture = asset.Value;
        }
        catch
        {
            return false;
        }
        if (texture is null) return false;

        try
        {
            var source = new Il2CppMicrosoft.Xna.Framework.Rectangle(frameX, frameY, 16, 16);
            batch.Draw(texture, new XnaVector2(x, y),
                new Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Rectangle>(source),
                PaintTintForPreview(paint), 0f, XnaVector2.Zero, 1f,
                Il2CppMicrosoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>画一格墙壁贴图：32×32 源框，位置由调用方在锚点基础上 −8 格像素（原版同款）。</summary>
    public static bool DrawWallTextureAt(SpriteBatch batch, float x, float y, int wall, int wallFrameX, int wallFrameY, int paint)
    {
        if (batch is null || wall <= 0) return false;
        var assets = Terraria.GameContent.TextureAssets.Wall;
        if (assets is null || wall >= assets.Length) return false;

        Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D texture;
        try
        {
            var asset = assets[wall];
            if (asset is null || asset.State != ReLogic.Content.AssetState.Loaded)
            {
                try { Main.instance.LoadWall(wall); } catch { /* 加载失败就跳过这一格 */ }
                return false;
            }
            texture = asset.Value;
        }
        catch
        {
            return false;
        }
        if (texture is null) return false;

        try
        {
            var source = new Il2CppMicrosoft.Xna.Framework.Rectangle(wallFrameX, wallFrameY, 32, 32);
            batch.Draw(texture, new XnaVector2(x, y),
                new Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Rectangle>(source),
                PaintTintForPreview(paint), 0f, XnaVector2.Zero, 1f,
                Il2CppMicrosoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
            return true;
        }
        catch
        {
            return false;
        }
    }
}