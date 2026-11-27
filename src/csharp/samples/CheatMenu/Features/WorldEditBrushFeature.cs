using System;

using Pylon.ModLoader.ImGuiHost;
using Pylon.NaviteAPI.ImGui;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;
using XnaVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;

namespace CheatMenu.Features;

/// <summary>
/// 地图刷子。1:1 对应原版 <c>WorldEditBrushTool</c>（世界 · 世界编辑，移植 TerraAngel）：
/// 以游戏准星为中心的圆形笔刷，方块/墙体/液体三路动作（无/破坏/放置/替换），
/// 放置数据取自手持物品；联机走 TileManipulation 包 + 伪位置/伪物品栏绕过距离限制；
/// 可选点亮地图与方块边框更新。刷子开关不持久化（防忘关误破坏）。
///
/// 预览：原版画在 <c>ImGui::GetBackgroundDrawList()</c> 上（世界内红色圆/方格，
/// 全屏小地图打开时改画地图那份，两者互斥）。Pylon 侧对应
/// <c>ModOverlay.Register(..., background: true)</c> —— 每帧 ImGui 帧内、Render 前回调，
/// 坐标就是 <c>WorldEdit.WorldToScreen</c> 的**物理像素**。
/// 之前塞进 LegacyGameInterfaceLayer（InterfaceScaleType.Game）是错的：
/// 那是游戏逻辑分辨率的 SpriteBatch 批次空间，和物理像素差一个缩放，
/// 结果预览既错位又不可见。
/// </summary>
public sealed class WorldEditBrushFeature : CheatFeature
{
    private static readonly string[] TileActions = ["无", "破坏", "放置", "替换"];
    private static readonly string[] WallActions = ["无", "破坏", "放置", "替换"];
    private static readonly string[] LiquidActions = ["无", "移除", "水", "岩浆", "蜂蜜", "微光"];

    /// <summary>常驻绘制挂点键。卸载由框架 ModOverlay.Reset() 统一清空。</summary>
    private const string OverlayKey = "CheatMenu/WorldEditBrush";

    private bool _brushEnabled;          // 不持久化
    private bool _needsResetPlayer;

    private int _createTile = -1, _placeStyle, _createWall;
    private XnaVector2 _playerStartPos, _lastTeleportPos;

    public override string Id => "worldedit.brush";
    public override string Title => "世界刷子";
    public override CheatCategory Category => CheatCategory.World;
    public override string SubPage => "世界编辑";

    public override void Install()
    {
        RegisterOverlay(OverlayKey, DrawOverlay);
    }

    public override void Tick()
    {
        if (!_brushEnabled) return;
        if (Main.gameMenu) return;

        // 按住拖动持续刷（原版 WorldEdit::IsWorldHeld），位置跟随触摸/准星
        if (CheatFeatureHost.Config is { WorldTapBrush: true } && WorldEdit.IsWorldHeld())
        {
            var aim = WorldEdit.AimTilePos();
            var tx = (int)aim.X;
            var ty = (int)aim.Y;
            if (WorldEdit.InWorld(tx, ty)) EditAtTile(tx, ty);
        }
    }

    // ---------------- 编辑内核 ----------------

    private void EditAtTile(int tx, int ty)
    {
        var local = Game.LocalPlayer;
        _playerStartPos = local?.Center ?? XnaVector2.Zero;
        _lastTeleportPos = _playerStartPos;
        _needsResetPlayer = false;
        CaptureHeldItemPlaceData();

        // 原版 EditAtTile：圆内逐格刷（x/y 都是 float 循环，半径判定用格距离）
        var bd = MathF.Floor((CheatFeatureHost.Config?.BrushDiameter ?? 16) / 16f);
        for (var x = tx - bd; x < tx + bd; x++)
        {
            // ⚠️ 这里原来写成 `y < tx + bd`（把 y 的上界错用 x），
            // 笔刷形状因此被压成一条斜带、下方半圆刷不到 —— 与原版
            // `for (y = ty - bd; y < ty + bd; y++)` 不一致。
            for (var y = ty - bd; y < ty + bd; y++)
            {
                if (!WorldEdit.InWorld((int)x, (int)y)) continue;
                var dx = x - tx;
                var dy = y - ty;
                if (MathF.Sqrt(dx * dx + dy * dy) >= bd) continue;
                Kernel((int)x, (int)y);
            }
        }

        if (_needsResetPlayer && Main.netMode == 1)
            SendFakePlayerPosition(_playerStartPos);
    }

    private void Kernel(int x, int y)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        switch (cfg.BrushTileAction)
        {
            case 1: KillTile(x, y); break;
            case 2: PlaceTile(x, y, false); break;
            case 3: PlaceTile(x, y, true); break;
        }
        switch (cfg.BrushWallAction)
        {
            case 1: KillWall(x, y); break;
            case 2: PlaceWall(x, y, false); break;
            case 3: PlaceWall(x, y, true); break;
        }
        switch (cfg.BrushLiquidAction)
        {
            case 1: KillLiquid(x, y); break;
            case 2: PlaceLiquid(x, y, 0); break;
            case 3: PlaceLiquid(x, y, 1); break;
            case 4: PlaceLiquid(x, y, 2); break;
            case 5: PlaceLiquid(x, y, 3); break;
        }

        if (cfg.RevealMap)
        {
            Main.Map?.Update(x, y, 255);
            Main.refreshMap = true;
        }
    }

    // ---- 手持物品的放置数据 ----

    private void CaptureHeldItemPlaceData()
    {
        _createTile = -1;
        _placeStyle = 0;
        _createWall = 0;
        var local = Game.LocalPlayer;
        var item = local?.HeldItem;
        if (item is null) return;
        _createTile = item.createTile;
        _placeStyle = item.placeStyle;
        _createWall = item.createWall;
    }

    // ---- 远距离伪位置（绕过 TShock 距离限制） ----

    private void TeleportToFarTile(int x, int y)
    {
        if (CheatFeatureHost.Config is not { TeleportToTilesFarAway: true }) return;
        var wx = x * 16f;
        var wy = y * 16f;
        if (MathF.Abs(wx - _lastTeleportPos.X) > 26f * 16f ||
            MathF.Abs(wy - _lastTeleportPos.Y) > 26f * 16f)
        {
            _needsResetPlayer = true;
            _lastTeleportPos = new XnaVector2(wx, wy);
            SendFakePlayerPosition(_lastTeleportPos);
        }
    }

    private static void SendFakePlayerPosition(XnaVector2 worldPos)
    {
        var self = Game.LocalPlayer;
        if (self is null || Main.netMode != 1) return;

        var packet = new RawPacket();
        packet.Packet(13, w =>
        {
            w.Write((byte)Main.myPlayer);
            w.Write(ControlFlags(self));
            w.Write(MovementFlags(self));
            w.Write(MiscFlags(self));
            w.Write(ExtraFlags(self));
            w.Write((byte)self.selectedItem);
            w.Write(worldPos.X);
            w.Write(worldPos.Y);
            var v = self.velocity;
            if (v.X != 0f || v.Y != 0f)
            {
                w.Write(v.X);
                w.Write(v.Y);
            }
            if (self.mount.Active) w.Write((ushort)self.mount.Type);
        });
        packet.Send();
    }

    private static byte ControlFlags(Player p)
    {
        byte f = 0;
        if (p.controlUp) f |= 0x01;
        if (p.controlDown) f |= 0x02;
        if (p.controlLeft) f |= 0x04;
        if (p.controlRight) f |= 0x08;
        if (p.controlJump) f |= 0x10;
        if (p.controlUseItem) f |= 0x20;
        if (p.direction == 1) f |= 0x40;
        return f;
    }

    private static byte MovementFlags(Player p)
    {
        byte f = 0;
        if (p.pulley) f |= 0x01;
        if (p.velocity.X != 0f || p.velocity.Y != 0f) f |= 0x04;
        if (p.vortexStealthActive) f |= 0x08;
        if (p.gravDir == 1f) f |= 0x10;
        if (p.shieldRaised) f |= 0x20;
        if (p.ghost) f |= 0x40;
        if (p.mount.Active) f |= 0x80;
        return f;
    }

    private static byte MiscFlags(Player p)
    {
        byte f = 0;
        if (p.tryKeepingHoveringUp) f |= 0x01;
        if (p.IsVoidVaultEnabled) f |= 0x02;
        if (p.sitting.isSitting) f |= 0x04;
        if (p.downedDD2EventAnyDifficulty) f |= 0x08;
        if (p.petting.isPetting) f |= 0x10;
        if (p.petting.isPetSmall) f |= 0x20;
        if (p.PotionOfReturnOriginalUsePosition.HasValue) f |= 0x40;
        if (p.tryKeepingHoveringDown) f |= 0x80;
        return f;
    }

    private static byte ExtraFlags(Player p)
    {
        byte f = 0;
        if (p.sleeping.isSleeping) f |= 0x01;
        if (p.autoReuseAllWeapons) f |= 0x02;
        if (p.controlDownHold) f |= 0x04;
        if (p.isOperatingAnotherEntity) f |= 0x08;
        if (p.controlUseTile) f |= 0x10;
        if (p.netCameraTarget.HasValue) f |= 0x20;
        if (p.lastItemUseAttemptSuccess) f |= 0x40;
        return f;
    }

    // ==================== 内核编辑 ====================

    private void KillTile(int x, int y)
    {
        if (!WorldGen.CanKillTile(x, y)) return;
        var index = Game.TileIndex(x, y);
        Terraria.TileData.SetType(index, 0);
        // 原版只清 active 位（SHeader bit5），保留油漆/斜坡等元数据
        var header = Terraria.TileData.GetSHeader(index);
        Terraria.TileData.SetSHeader(index, (short)(header & ~0x20));
        WorldEdit.SendTileManipulation(0, x, y);   // KillTileNoItem
        if (CheatFeatureHost.Config is { SquareFrame: true })
            WorldGen.SquareTileFrame(x, y);
    }

    private void PlaceTile(int x, int y, bool replace)
    {
        var index = Game.TileIndex(x, y);
        var active = Game.TileActive(x, y);
        var type = Game.TileType(x, y);
        var shouldPlace = !active || (replace && type != _createTile);
        if (!shouldPlace || _createTile == -1) return;

        TeleportToFarTile(x, y);

        Terraria.TileData.SetType(index, (ushort)_createTile);
        // 原版 Setactive(true)：只置 active 位
        var header = Terraria.TileData.GetSHeader(index);
        Terraria.TileData.SetSHeader(index, (short)((header & ~0x60) | 0x20));
        WorldGen.PlaceTile(x, y, _createTile, false, false, -1, _placeStyle);
        WorldEdit.SendTileManipulation(1, x, y, _createTile, _placeStyle);   // PlaceTile
        if (CheatFeatureHost.Config is { SquareFrame: true })
            WorldGen.SquareTileFrame(x, y);
    }

    private void KillWall(int x, int y)
    {
        var index = Game.TileIndex(x, y);
        if (Terraria.TileData.GetWall(index) == 0) return;
        Terraria.TileData.SetWall(index, 0);
        WorldEdit.SendTileManipulation(3, x, y);   // KillWall
        if (CheatFeatureHost.Config is { SquareFrame: true })
            WorldGen.SquareWallFrame(x, y);
    }

    private void PlaceWall(int x, int y, bool replace)
    {
        var index = Game.TileIndex(x, y);
        var wall = Terraria.TileData.GetWall(index);
        var shouldPlace = wall == 0 || (replace && wall != _createWall);
        if (!shouldPlace || _createWall == 0) return;

        TeleportToFarTile(x, y);

        Terraria.TileData.SetWall(index, (ushort)_createWall);
        // replace: 4 = ReplaceWall；place: 2 = PlaceWall（TileManipulationID）
        WorldEdit.SendTileManipulation(replace ? 4 : 2, x, y, _createWall);
        if (CheatFeatureHost.Config is { SquareFrame: true })
            WorldGen.SquareWallFrame(x, y);
    }

    private void KillLiquid(int x, int y)
    {
        var index = Game.TileIndex(x, y);
        if (Terraria.TileData.GetLiquid(index) == 0) return;

        if (Main.netMode == 1)
        {
            var lt = LiquidType(index);
            var sponge = lt == 1 ? 4872 /*LavaAbsorbantSponge*/ : 3032 /*SuperAbsorbantSponge*/;
            WorldEdit.SendFakeInventorySlot(0, sponge);
            _lastTeleportPos = new XnaVector2(x * 16f, y * 16f);
            _needsResetPlayer = true;
            SendFakePlayerPosition(_lastTeleportPos);
        }
        WorldGen.EmptyLiquid(x, y);
        if (Main.netMode == 1) WorldEdit.ResetInventorySlot(0);
    }

    private void PlaceLiquid(int x, int y, int liquid)
    {
        var index = Game.TileIndex(x, y);
        if (LiquidType(index) == liquid && Terraria.TileData.GetLiquid(index) == 255) return;

        if (Main.netMode == 1)
        {
            // ItemID：WaterBucket 206 / LavaBucket 207 / HoneyBucket 1128 / BottomlessShimmerBucket 5364
            var bucket = liquid switch
            {
                1 => 207,
                2 => 1128,
                3 => 5364,
                _ => 206,
            };
            WorldEdit.SendFakeInventorySlot(0, bucket);
            _lastTeleportPos = new XnaVector2(x * 16f, y * 16f);
            _needsResetPlayer = true;
            SendFakePlayerPosition(_lastTeleportPos);
        }
        WorldGen.PlaceLiquid(x, y, (byte)liquid, 255);
        if (Main.netMode == 1) WorldEdit.ResetInventorySlot(0);
    }

    private static int LiquidType(int index) =>
        (Terraria.TileData.GetSHeader(index) >> 6) & 0x3;

    // ==================== 预览（ModOverlay 背景层，等价原版 GetBackgroundDrawList） ====================

    /// <summary>
    /// 每帧回调。原版 <c>Update()</c> 里 DrawPreview + DrawMapPreview 各画一份，
    /// 但两者互斥：DrawPreview 首行 <c>if (Main.mapFullscreen) return;</c>。
    /// 这里合并成一次分派，语义相同（地图态只画地图那份，避免双框）。
    /// </summary>
    private void DrawOverlay(ImDrawListPtr dl)
    {
        if (!_brushEnabled) return;          // 原版 Update 首行
        if (Main.gameMenu) return;           // 原版 Update 第二行

        if (Main.mapFullscreen)
        {
            DrawMapPreview(dl);
            return;
        }
        DrawWorldPreview(dl);
    }

    /// <summary>世界内预览（原版 DrawPreview）。</summary>
    private void DrawWorldPreview(ImDrawListPtr dl)
    {
        var aim = WorldEdit.AimWorldPos();
        var mtx = (int)MathF.Floor(aim.X / 16f);
        var mty = (int)MathF.Floor(aim.Y / 16f);
        if (!WorldEdit.InWorld(mtx, mty)) return;

        var cfg = CheatFeatureHost.Config;
        var diameter = (float)(cfg?.BrushDiameter ?? 16);
        var col = WorldEdit.PreviewRed;      // IM_COL32(255, 0, 0, 130)

        if (cfg is not { DrawDetailedPreview: true })
        {
            // 简单模式：红色实心圆。半径 = 两次 WorldToScreen 的实测距离（原版 +brushDiameter+16）
            WorldEdit.FillWorldCircle(dl, aim.X, aim.Y, diameter + 16f, col, 48);
            return;
        }

        // 详细模式：圆内逐格红色方块（原版 AddRectFilled）
        var bd = MathF.Floor(diameter / 16f);
        for (var x = mtx - bd; x < mtx + bd; x++)
        {
            for (var y = mty - bd; y < mty + bd; y++)
            {
                if (!WorldEdit.InWorld((int)x, (int)y)) continue;
                var dx = x - mtx;
                var dy = y - mty;
                if (MathF.Sqrt(dx * dx + dy * dy) >= bd) continue;
                WorldEdit.FillWorldTile(dl, (int)x, (int)y, col);
            }
        }
    }

    /// <summary>全屏小地图预览（原版 DrawMapPreview，对应 TA DrawPreviewInMap）。</summary>
    private void DrawMapPreview(ImDrawListPtr dl)
    {
        var aim = WorldEdit.AimTilePos();
        var ax = (int)aim.X;
        var ay = (int)aim.Y;

        var cfg = CheatFeatureHost.Config;
        var diameter = (float)(cfg?.BrushDiameter ?? 16);
        var col = WorldEdit.PreviewRedMap;   // IM_COL32(255, 0, 0, 140)

        if (cfg is not { DrawDetailedPreview: true })
        {
            // 简单模式：半径 = |map(aim + 笔刷直径).x - map(aim).x| * 0.5
            // （原版 b 取世界格 (ax + brushDiameter/16) * 16 = ax*16 + brushDiameter）
            WorldEdit.FillMapCircle(dl, ax * 16f, ay * 16f, diameter, col, 32);
            return;
        }

        var bd = MathF.Floor(diameter / 16f);
        for (var x = ax - bd; x < ax + bd; x++)
        {
            for (var y = ay - bd; y < ay + bd; y++)
            {
                if (!WorldEdit.InWorld((int)x, (int)y)) continue;
                var dx = x - ax;
                var dy = y - ay;
                if (MathF.Sqrt(dx * dx + dy * dy) >= bd) continue;
                WorldEdit.FillMapTile(dl, (int)x, (int)y, col);
            }
        }
    }

    // ==================== UI ====================

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("地图刷子", "BRUSH");

        ModernUI.BeginRow();
        ModernUI.RowLabel("启用刷子");
        if (ModernUI.Toggle("##brushenabled", _brushEnabled))
            _brushEnabled = !_brushEnabled;
        ModernUI.EndRow();

        RowToggle("##tapbrush", "触摸拖动使用（按住持续刷）", cfg.WorldTapBrush, v => cfg.WorldTapBrush = v);

        if (ModernUI.PrimaryButton("在准心位置刷一次"))
        {
            var t = WorldEdit.AimTilePos();
            EditAtTile((int)t.X, (int)t.Y);
        }

        RowToggle("##detailed", "详细预览", cfg.DrawDetailedPreview, v => cfg.DrawDetailedPreview = v);
        RowToggle("##squareframe", "更新方块边框", cfg.SquareFrame, v => cfg.SquareFrame = v);
        RowToggle("##teleport", "绕过 TShock 距离限制", cfg.TeleportToTilesFarAway, v => cfg.TeleportToTilesFarAway = v);
        RowToggle("##revealmap", "点亮地图", cfg.RevealMap, v => cfg.RevealMap = v);

        var diameter = (float)cfg.BrushDiameter;
        if (ModernUI.RowSlider("##brushdia", "笔刷直径", "16-800", ref diameter, 16f, 800f, "0"))
        {
            cfg.BrushDiameter = (int)Math.Clamp(diameter, 16f, 800f);
            cfg.DrawDetailedPreview = cfg.BrushDiameter <= 600;
            CheatFeatureHost.MarkDirty();
        }

        ModernUI.BeginRow();
        ModernUI.RowLabel("方块");
        if (ModernUI.ComboField("##brushtile", TileActions, ref cfg.BrushTileAction))
            cfg.BrushTileAction = Math.Clamp(cfg.BrushTileAction, 0, TileActions.Length - 1);
        ModernUI.EndRow();

        ModernUI.BeginRow();
        ModernUI.RowLabel("墙体");
        if (ModernUI.ComboField("##brushwall", WallActions, ref cfg.BrushWallAction))
            cfg.BrushWallAction = Math.Clamp(cfg.BrushWallAction, 0, WallActions.Length - 1);
        ModernUI.EndRow();

        ModernUI.BeginRow();
        ModernUI.RowLabel("液体");
        if (ModernUI.ComboField("##brushliquid", LiquidActions, ref cfg.BrushLiquidAction))
            cfg.BrushLiquidAction = Math.Clamp(cfg.BrushLiquidAction, 0, LiquidActions.Length - 1);
        ModernUI.EndRow();

        var aim = WorldEdit.AimTilePos();
        ModernUI.BeginRow();
        ModernUI.RowLabel("准心位置", $"({(int)aim.X}, {(int)aim.Y})");
        ModernUI.EndRow();

        ModernUI.EndCard();
    }

    private static void RowToggle(string id, string label, bool value, Action<bool> set)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        if (ModernUI.Toggle(id, value))
        {
            set(!value);
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();
    }
}