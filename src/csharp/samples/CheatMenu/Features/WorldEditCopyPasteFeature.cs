using System;
using System.Collections.Generic;

using Pylon.ModLoader.ImGuiHost;
using Pylon.NaviteAPI.ImGui;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;
using SpriteBatch = Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch;
using Matrix = Il2CppMicrosoft.Xna.Framework.Matrix;
using XnaVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;

namespace CheatMenu.Features;

/// <summary>
/// 区域复制粘贴。1:1 对应原版 <c>WorldEditCopyPasteTool</c>（世界 · 世界编辑）：
/// 字节级复制 Tile（原始头字节原样写回，不触发 frame 重算，保证树木/多格物件完整），
/// 采集箱子/标牌/TileEntity 快照并在粘贴后重建；
/// 粘贴分直写（SendTileRect 两段式 + 批量 SendTileSquare）与联机发包（伪物品栏 + TileManipulation）两条路径；
/// 预览：选中红框（含半透明填充）/ 已复制绿框 + 彩色色块（≤5000 格）/ 粘贴中黄框，
/// 全部画在 <c>ModOverlay</c> 背景层（等价原版 <c>ImGui::GetBackgroundDrawList()</c>，物理像素）。
///
/// ⚠️ 之前预览走 <c>AddInterfaceLayers + InterfaceScaleType.Game</c> 的 SpriteBatch 批次，
/// 却喂了 WorldToScreen 的物理像素坐标 —— 批次空间是游戏逻辑分辨率，两者差一个缩放，
/// 于是画不出来/画到屏外。现在统一走 ImGui 物理像素。
///
/// 另外原版还有一条“真实贴图预览”（Main.DoDraw 之后自己 Begin 一批 SpriteBatch 画真图块）。
/// Pylon 没有等价的 DoDraw 后置挂点，这条路径本移植不提供：ImGui 侧只保留地图色块预览
/// （原版 <c>DrawSelectionPreview</c> 的那份），不引入 SpriteBatch。
/// </summary>
public sealed class WorldEditCopyPasteFeature : CheatFeature
{
    // ==================== 数据结构 ====================

    private sealed class CopiedTile
    {
        public bool active, inActive, halfBrick;
        public byte slope;
        public ushort type, wall;
        public byte liquid, liquidType;
        public byte color, wallColor;
        public bool fullbrightBlock, invisibleBlock, fullbrightWall, invisibleWall;
        public bool wire1, wire2, wire3, wire4, actuator;
        public short frameX, frameY, wallFrameX, wallFrameY;
        public byte frameNumber, wallFrameNumber;
        public bool tileFramed, checkingLiquid, skipLiquid;
        // 原始字节（特殊图格完整性的关键；墙帧/油漆等全部位都在里面）
        public short sTileHeader;
        public byte bTileHeader, bTileHeader2, bTileHeader3;
        // 预览色
        public byte pr, pg, pb;

        public bool IsEmpty => !active && wall == 0 && liquid == 0;
    }

    private sealed class SavedChest
    {
        public int Ox, Oy, MaxItems;
        public string Name;
        public List<ChestItem> Items = new();
    }

    private sealed class SavedSign
    {
        public int Ox, Oy;
        public string Text;
    }

    private sealed class SavedEntity
    {
        public int Ox, Oy;
        public byte Type;
    }

    private sealed class Section
    {
        public int W, H;
        public List<CopiedTile> Tiles;
        public List<SavedChest> Chests = new();
        public List<SavedSign> Signs = new();
        public List<SavedEntity> Entities = new();
        public bool Ready;
    }

    private sealed class ChestItem
    {
        public short type;
        public short stack;
        public byte prefix;
        public bool favorited;
    }

    // ==================== 状态 ====================

    /// <summary>常驻绘制挂点键。卸载由框架 ModOverlay.Reset() 统一清空。</summary>
    private const string OverlayKey = "CheatMenu/WorldEditCopyPaste";

    /// <summary>彩色色块预览的格数上限（原版同样限制 5000）。</summary>
    private const int MaxContentPreview = 5000;

    private Section _section = new();
    private bool _enabled;
    private bool _selectArmed, _selecting;
    private int _selStartX, _selStartY, _selEndX, _selEndY;
    private bool _pasting;
    private int _pasteX, _pasteY;
    private int _pastePos, _pasteDone, _pasteTotal;
    private int _batchMinX, _batchMinY, _batchMaxX, _batchMaxY, _batchCount;
    private bool _hasAnchor;
    private int _anchorX, _anchorY;
    private int _pasteOffsetX, _pasteOffsetY;   // 不持久化
    private bool _wasMouseLeft;

    /// <summary>原版 DrawUI 里强制置 true 的两个预览开关（不持久化、恒开）。</summary>
    private static readonly bool ShowTexturePreview = true;
    private static readonly bool ShowContentPreview = true;

    /// <summary>上一帧贴图预览是否真的画出了格子（贴图批次拿不到时退回色块预览）。</summary>
    private static bool _texturePreviewDrawn;
    private static bool _previewReported;

    // tile→可放置物品 映射（联机粘贴用；原版 TileUtil 懒建）
    private static Dictionary<int, int> _tileToItem;
    private static Dictionary<int, int> _wallToItem;

    public override string Id => "worldedit.copypaste";
    public override string Title => "剪贴板";
    public override CheatCategory Category => CheatCategory.World;
    public override string SubPage => "世界编辑";

    public override void Install()
    {
        RegisterOverlay(OverlayKey, DrawSelectionPreview);
    }

    // ==================== 读 / 写 Tile ====================

    private static CopiedTile ReadWorldTile(int wx, int wy)
    {
        var t = new CopiedTile();
        var index = Game.TileIndex(wx, wy);
        if (index < 0) return t;

        // ① 扁平索引侧：整格原始字节 + 类型/墙/液体/块帧（写回时原样落回去）
        t.sTileHeader = Terraria.TileData.GetSHeader(index);
        t.bTileHeader = Terraria.TileData.GetBHeader(index);
        t.bTileHeader2 = Terraria.TileData.GetBHeader2(index);
        t.bTileHeader3 = Terraria.TileData.GetBHeader3(index);
        t.type = Terraria.TileData.GetType(index);
        t.wall = Terraria.TileData.GetWall(index);
        t.liquid = Terraria.TileData.GetLiquid(index);
        t.frameX = Terraria.TileData.GetFrameX(index);
        t.frameY = Terraria.TileData.GetFrameY(index);

        // ② 语义字段走 Tile 句柄的官方 getter（等价原版 Getactive_Call / Getslope_Call / …）：
        //    TileData 的静态面只暴露了 checkingLiquid/skipLiquid/TileFramed 三个语义位，
        //    active/inActive/slope/color/wallColor/wire*/actuator/liquidType 都在 Tile 上。
        //    以前靠手写位偏移解码，liquidType 取 (header>>6)&3 与 inActive 的 bit6 撞车，
        //    wire 取 header bit15 也没有依据 —— 用官方 getter 就不会猜错。
        var tile = WorldEdit.TileAt(wx, wy);
        t.active = tile.active();
        t.inActive = tile.inActive();
        t.halfBrick = tile.halfBrick();
        t.slope = tile.slope();
        t.liquidType = tile.liquidType();
        t.color = tile.color();
        t.wallColor = tile.wallColor();
        t.fullbrightBlock = tile.fullbrightBlock();
        t.invisibleBlock = tile.invisibleBlock();
        t.fullbrightWall = tile.fullbrightWall();
        t.invisibleWall = tile.invisibleWall();
        t.wire1 = tile.wire();
        t.wire2 = tile.wire2();
        t.wire3 = tile.wire3();
        t.wire4 = tile.wire4();
        t.actuator = tile.actuator();
        t.frameNumber = tile.frameNumber();
        t.wallFrameNumber = tile.wallFrameNumber();
        t.tileFramed = tile.TileFramed();
        t.checkingLiquid = tile.checkingLiquid();
        t.skipLiquid = tile.skipLiquid();

        // ③ 墙帧：原版读的是 Tile::GetwallFrameX_Call(w) / GetwallFrameY_Call(w)。
        //    ⚠️ 以前这里写的是 TileData.GetFrameX/GetFrameY —— 那是**方块**帧，
        //    拿它当墙帧用会把 32×32 墙贴图的源矩形错到方块帧上（预览花掉），
        //    而写回时又完全没写墙帧（墙按 0 帧拉伸 → 周期性隔断）。
        t.wallFrameX = (short)tile.wallFrameX();
        t.wallFrameY = (short)tile.wallFrameY();

        // ④ 预览色：有方块用方块色，否则用墙色（地图表未就绪时保持 0=不画）
        if (t.active)
        {
            if (WorldEdit.TryGetTilePreviewColor(t.type, t.color, out var r, out var g, out var b))
            {
                t.pr = r;
                t.pg = g;
                t.pb = b;
            }
        }
        else if (t.wall > 0)
        {
            if (WorldEdit.TryGetWallPreviewColor(t.wall, t.wallColor, out var r, out var g, out var b))
            {
                t.pr = r;
                t.pg = g;
                t.pb = b;
            }
        }
        return t;
    }

    /// <summary>
    /// 与 Tile.CopyFrom 底层等价：原始字节原样写回，不触发 frame 重算。
    /// ⚠️ 不用 SetEverything：它内部会重算边框帧，把连贯的树/多格物件打散。
    /// </summary>
    private static void ApplyCopiedTileToWorld(int wx, int wy, CopiedTile t)
    {
        var index = Game.TileIndex(wx, wy);
        if (index < 0) return;

        Terraria.TileData.SetType(index, t.type);
        Terraria.TileData.SetWall(index, t.wall);
        Terraria.TileData.SetLiquid(index, t.liquid);
        // 原始头字节（active/slope/wire/paint/actuator/frameNumber/liquidType 等全部位）
        Terraria.TileData.SetSHeader(index, t.sTileHeader);
        Terraria.TileData.SetBHeader(index, t.bTileHeader);
        Terraria.TileData.SetBHeader2(index, t.bTileHeader2);
        Terraria.TileData.SetBHeader3(index, t.bTileHeader3);
        // 块帧（原样属性 setter，不触发方法内重算）
        Terraria.TileData.SetFrameX(index, t.frameX);
        Terraria.TileData.SetFrameY(index, t.frameY);

        // 墙帧：原版写回用的是 Tile::SetwallFrameX_Call(w) / SetwallFrameY_Call(w)。
        // 头字节里本来就带着墙帧位（手机版 Tile 只有 4 字节句柄，位数据全在 TileData 数组里，
        // 没有独立墙帧存储），所以这里再按官方 setter 写一遍是幂等的、不会与头字节冲突；
        // 保留它是为了与读取端 getter 严格对称 —— 原版注释：墙帧不写会导致周期性隔断。
        var tile = WorldEdit.TileAt(wx, wy);
        tile.wallFrameX(t.wallFrameX);
        tile.wallFrameY(t.wallFrameY);
    }

    private static bool IsGolfProp(int type) =>
        type == 476 || type == 493 || type == 494;

    private static bool IsTileSolid(int type)
    {
        var solid = Main.tileSolid;
        if (solid is null) return true;
        return type >= 0 && type < solid.Length && solid[type];
    }

    private static bool IsDirtGrass(int type) =>
        type is 2 or 23 or 199 or 109;

    // ==================== 复制 ====================

    private void ArmSelect()
    {
        _selectArmed = true;
        _selecting = false;
        ModernUI.Toast("按下并拖动圈出范围，抬手完成复制");
    }

    private void CancelSelect()
    {
        _selectArmed = false;
        _selecting = false;
        ModernUI.Toast("已取消复制");
    }

    private bool Copy(int tlX, int tlY, int brX, int brY)
    {
        var maxX = Main.maxTilesX;
        var maxY = Main.maxTilesY;
        if (maxX <= 0 || maxY <= 0) return false;
        tlX = Math.Clamp(tlX, 0, maxX - 1);
        tlY = Math.Clamp(tlY, 0, maxY - 1);
        brX = Math.Clamp(brX, 0, maxX - 1);
        brY = Math.Clamp(brY, 0, maxY - 1);

        var w = brX - tlX + 1;
        var h = brY - tlY + 1;
        if (w <= 0 || h <= 0) return false;

        var sec = new Section
        {
            W = w,
            H = h,
            Tiles = new List<CopiedTile>(w * h),
        };
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                sec.Tiles.Add(ReadWorldTile(tlX + x, tlY + y));
        sec.Ready = true;

        CollectExtras(tlX, tlY, w, h, sec);

        // 新复制区域尺寸不同，旧偏移不再适用
        _pasteOffsetX = 0;
        _pasteOffsetY = 0;
        _section = sec;
        _previewReported = false;
        CheatMenuMod.Log?.Info($"[copypaste] 复制完成：({tlX},{tlY})-({brX},{brY}) → {w}x{h} 格，" +
            $"箱子 {sec.Chests.Count} / 标牌 {sec.Signs.Count} / 实体 {sec.Entities.Count}；" +
            $"锚点 ({_anchorX},{_anchorY})");
        return true;
    }

    // ==================== 特殊实体采集 ====================

    private static void CollectExtras(int tlX, int tlY, int w, int h, Section sec)
    {
        // ---- 箱子 ----
        var chestArr = Main.chest;
        if (chestArr is not null)
        {
            foreach (var c in chestArr)
            {
                if (c is null) continue;
                var cx = c.x;
                var cy = c.y;
                var hit = (cx >= tlX && cx < tlX + w && cy >= tlY && cy < tlY + h) ||
                          (cx + 1 >= tlX && cx + 1 < tlX + w && cy + 1 >= tlY && cy + 1 < tlY + h);
                if (!hit) continue;

                var s = new SavedChest
                {
                    Ox = cx - tlX,
                    Oy = cy - tlY,
                    MaxItems = c.maxItems,
                    Name = c.name,
                };
                if (c.item is not null)
                {
                    foreach (var src in c.item)
                    {
                        s.Items.Add(new ChestItem
                        {
                            type = (short)src.type,
                            stack = (short)src.stack,
                            prefix = (byte)src.prefix,
                            favorited = src.favorited,
                        });
                    }
                }
                sec.Chests.Add(s);
            }
        }

        // ---- 标牌 ----
        var signArr = Main.sign;
        if (signArr is not null)
        {
            foreach (var s in signArr)
            {
                if (s is null) continue;
                if (s.x < tlX || s.x >= tlX + w || s.y < tlY || s.y >= tlY + h) continue;
                sec.Signs.Add(new SavedSign
                {
                    Ox = s.x - tlX,
                    Oy = s.y - tlY,
                    Text = s.text,
                });
            }
        }

        // ---- TileEntity ----
        try
        {
            foreach (var te in TileEntity.ByPosition.Values)
            {
                if (te is null) continue;
                var ex = te.Position.X;
                var ey = te.Position.Y;
                if (ex < tlX || ex >= tlX + w || ey < tlY || ey >= tlY + h) continue;
                sec.Entities.Add(new SavedEntity
                {
                    Ox = ex - tlX,
                    Oy = ey - tlY,
                    Type = te.type,
                });
            }
        }
        catch
        {
            // 实体字典未就绪：跳过
        }
    }

    // ==================== 粘贴 ====================

    private void StartPaste(int ox, int oy)
    {
        if (!_section.Ready)
        {
            ModernUI.Toast("请先复制一块区域");
            return;
        }
        _pasting = true;
        _pasteX = ox;
        _pasteY = oy;
        _pastePos = 0;
        _pasteDone = 0;
        _pasteTotal = _section.W * _section.H;
        _batchMinX = _batchMinY = int.MaxValue;
        _batchMaxX = _batchMaxY = -1;
        _batchCount = 0;
    }

    private void CancelPaste()
    {
        _pasting = false;
        ModernUI.Toast("已取消粘贴");
    }

    private void ClearCopied()
    {
        _section = new Section();
        _pasting = false;
        _batchCount = 0;
        _hasAnchor = false;
        _anchorX = _anchorY = 0;
        ModernUI.Toast("已清除复制内容");
    }

    // ---- 直写模式（两段式：solid 先，非 solid 后） ----

    private static void PasteDirect(int wx, int wy, CopiedTile copied, bool solidPass)
    {
        if (copied.IsEmpty) return;
        var isSolid = copied.active && !IsGolfProp(copied.type) && IsTileSolid(copied.type);
        if (solidPass != isSolid) return;
        ApplyCopiedTileToWorld(wx, wy, copied);
    }

    // ---- 联机发包模式（伪物品栏 + TileManipulation） ----

    private void PasteTileMulti(int wx, int wy, CopiedTile copied)
    {
        var copiedWorld = ReadWorldTile(wx, wy);
        var packet = new RawPacket();
        var wroteAny = false;

        void Manip(int op, int data = 0, int data2 = 0)
        {
            packet.Packet(17, w =>
            {
                w.Write((byte)op);
                w.Write((short)wx);
                w.Write((short)wy);
                w.Write((short)data);
                w.Write((byte)data2);
            });
        }

        void WithItem(int itemId, Action op)
        {
            // 原版 WritePositionedOperationWithItem：控制包 + 伪物品栏 + 操作 +（结尾统一复位）
            WritePlayerControls(packet, wx * 16f, wy * 16f);
            WriteInventorySlot(packet, 0, itemId, 1, 0);
            op();
        }

        if (copied.active)
        {
            if (IsDirtGrass(copied.type)) WithItem(2, () => Manip(1, 2));            // 草皮先放土
            if (copied.type is 60 or 70 or 661 or 662) WithItem(59, () => Manip(1, 59));  // 丛林草先放泥
            if (copied.type == 633) WithItem(57, () => Manip(1, 57));                // 灰烬草先放灰烬

            if (copiedWorld.active) WithItem(213, () => Manip(0));                   // IronPickaxe
            var itemId = TileItem(copied.type);
            if (itemId > 0) WithItem(itemId, () => Manip(1, copied.type));
            wroteAny = true;
        }
        else if (copiedWorld.active)
        {
            WithItem(213, () => Manip(0));                                           // IronPickaxe
            wroteAny = true;
        }

        if (copied.active && (copied.slope > 0 || copied.halfBrick))
        {
            var slopeData = copied.halfBrick ? 1 : copied.slope;
            WithItem(213, () => Manip(14, slopeData));                               // IronHammer SlopeTile
        }

        if (copied.wall > 0)
        {
            var wallItem = WallItem(copied.wall);
            if (wallItem > 0) WithItem(wallItem, () => Manip(3, copied.wall));
            wroteAny = true;
        }
        else if (copiedWorld.wall > 0)
        {
            WithItem(213, () => Manip(2));                                           // IronHammer KillWall
            wroteAny = true;
        }

        if (copied.liquid > 0)
        {
            packet.Packet(48, w =>
            {
                w.Write((short)wx);
                w.Write((short)wy);
                w.Write((byte)copied.liquid);
                w.Write((byte)copied.liquidType);
            });
        }

        if (copied.color > 0)
            WithItem(1071, () => packet.Packet(63, w =>   // Paintbrush
            {
                w.Write((short)wx);
                w.Write((short)wy);
                w.Write((byte)copied.color);
                w.Write((byte)0);
            }));
        if (copied.fullbrightBlock)
            WithItem(1071, () => packet.Packet(63, w =>
            {
                w.Write((short)wx);
                w.Write((short)wy);
                w.Write((byte)1);
                w.Write((byte)1);
            }));
        if (copied.invisibleBlock)
            WithItem(1071, () => packet.Packet(63, w =>
            {
                w.Write((short)wx);
                w.Write((short)wy);
                w.Write((byte)2);
                w.Write((byte)1);
            }));

        if (copied.wallColor > 0)
            WithItem(1072, () => packet.Packet(64, w =>   // PaintRoller
            {
                w.Write((short)wx);
                w.Write((short)wy);
                w.Write((byte)copied.wallColor);
                w.Write((byte)0);
            }));

        WireOps(packet, Manip, WithItem, copied, copiedWorld);
        wroteAny = true;

        if (wroteAny)
        {
            var self = Game.LocalPlayer;
            var real = self?.inventory?[0];
            WriteInventorySlot(packet, 0, real?.type ?? 0, real?.stack ?? 0, real?.prefix ?? 0);
            WritePlayerControls(packet, self?.Center.X ?? 0f, self?.Center.Y ?? 0f);
            packet.Send();
        }
    }

    private static void WireOps(RawPacket packet, Action<int, int, int> manip,
        Action<int, Action> withItem, CopiedTile copied, CopiedTile world)
    {
        // 电线/促动器操作不带 data（msg 17 的 data/data2 留 0）
        void Wire(bool want, bool has, int placeOp, int killOp)
        {
            if (want) withItem(509, () => manip(placeOp, 0, 0));
            else if (has) withItem(509, () => manip(killOp, 0, 0));
        }
        Wire(copied.wire1, world.wire1, 5, 6);
        Wire(copied.wire2, world.wire2, 10, 11);
        Wire(copied.wire3, world.wire3, 12, 13);
        Wire(copied.wire4, world.wire4, 16, 17);

        if (copied.actuator) withItem(309, () => manip(8, 0, 0));
        else if (world.actuator) withItem(509, () => manip(9, 0, 0));

        if (copied.inActive)
        {
            if (!copied.actuator) withItem(309, () => manip(8, 0, 0));
            withItem(4271, () => manip(19, 0, 0));   // ActuationRod Actuate
            if (!copied.actuator) withItem(509, () => manip(9, 0, 0));
        }
    }

    private static void WritePlayerControls(RawPacket packet, float x, float y)
    {
        var self = Game.LocalPlayer;
        packet.Packet(13, w =>
        {
            w.Write((byte)Main.myPlayer);
            w.Write(ButcherFlags(self, 'c'));
            w.Write(ButcherFlags(self, 'm'));
            w.Write(ButcherFlags(self, 'x'));
            w.Write(ButcherFlags(self, 'e'));
            w.Write((byte)0);   // selectedItem = useSlot 0
            w.Write(x);
            w.Write(y);
        });
    }

    private static byte ButcherFlags(Player p, char kind) => 0;

    private static void WriteInventorySlot(RawPacket packet, int slot, int itemId, int stack, int prefix)
    {
        packet.Packet(5, w =>
        {
            w.Write((byte)Main.myPlayer);
            w.Write((short)slot);
            w.Write((short)stack);
            w.Write((byte)prefix);
            w.Write((short)itemId);
            w.Write((byte)0);
        });
    }

    // ---- tile/wall → 可放置物品 映射 ----

    private static int TileItem(int tile)
    {
        BuildMappings();
        return _tileToItem.TryGetValue(tile, out var item) ? item : 0;
    }

    private static int WallItem(int wall)
    {
        BuildMappings();
        return _wallToItem.TryGetValue(wall, out var item) ? item : 0;
    }

    private static void BuildMappings()
    {
        if (_tileToItem is not null) return;
        _tileToItem = new Dictionary<int, int>();
        _wallToItem = new Dictionary<int, int>();
        var scratch = new Item();
        try
        {
            for (var i = 1; i < ItemID.Count; i++)
            {
                try
                {
                    scratch.netDefaults(i);
                }
                catch
                {
                    continue;
                }
                var ct = scratch.createTile;
                var cw = scratch.createWall;
                if (ct > 0 && !_tileToItem.ContainsKey(ct)) _tileToItem[ct] = i;
                if (cw > 0 && !_wallToItem.ContainsKey(cw)) _wallToItem[cw] = i;
            }
        }
        catch
        {
            // 扫描中断：已有映射仍可用
        }
    }

    // ---- 粘贴主循环（预算） ----

    private void UpdatePaste()
    {
        if (!_pasting) return;
        if (Main.gameMenu)
        {
            _pasting = false;
            return;
        }

        var cfg = CheatFeatureHost.Config;
        var w = _section.W;
        var h = _section.H;
        var total = w * h;
        var directMode = (cfg?.PastePlaceMode ?? 0) == 0;

        var budget = directMode
            ? 1200
            : Math.Clamp((cfg?.PasteTilesPerSecond ?? 60) / 60, 1, 30);
        var done = 0;

        while (_pastePos < total && done < budget)
        {
            var idx = _pastePos++;
            var x = idx % w;
            var y = idx / w;
            var wx = _pasteX + x;
            var wy = _pasteY + y;
            _pasteDone++;
            done++;

            if (!WorldEdit.InWorld(wx, wy)) continue;
            var t = _section.Tiles[idx];

            if (directMode)
            {
                // 两段式：先 solid 后非 solid（保证多格物件先清位再放墙/非实体）
                PasteDirect(wx, wy, t, true);
                PasteDirect(wx, wy, t, false);
                // ⚠️ 不调用 SquareTileFrame/SquareWallFrame：那会重算多格物件的 frame。
                // 字节级复制已经把完整 frame 写回，只需 SendTileSquare 同步。
                _batchMinX = Math.Min(_batchMinX, wx);
                _batchMinY = Math.Min(_batchMinY, wy);
                _batchMaxX = Math.Max(_batchMaxX, wx);
                _batchMaxY = Math.Max(_batchMaxY, wy);
                _batchCount++;
                if (_batchCount >= 256) FlushBatch();   // 每 256 格一批，批内物件完整后统一同步
            }
            else
            {
                if (t.IsEmpty) continue;
                PasteTileMulti(wx, wy, t);
            }
        }

        if (_pastePos >= total)
        {
            FlushBatch();
            _pasting = false;
            if (directMode)
            {
                Main.refreshMap = true;
                RestoreExtras();
            }
            ModernUI.Toast("复制粘贴完成");
            if (cfg is { PasteClearAfter: true })
            {
                _section = new Section();
                _hasAnchor = false;
                _anchorX = _anchorY = 0;
            }
        }
    }

    /// <summary>
    /// 把当前批次的已写格子一次性同步给客户端（避免逐格同步的半成品状态）。
    ///
    /// 原版：<c>WorldEdit::SendTileSquareBySize(myPlayer, minX, minY, size, None)</c>，
    /// 而 C++ 侧按**参数名**精确绑定重载（TerrariaSupport/src/Terraria/NetMessage.cpp:10）：
    /// <c>SendTileSquare_BySize_m = GetMethod("SendTileSquare", {whoAmi, tileX, tileY,
    /// centeredSquareSize, changeType})</c> —— 即 **centeredSquareSize** 那个重载，
    /// 不是 (xSize, ySize) 那个。所以这里也必须是
    /// <c>SendTileSquare(int whoAmi, int tileX, int tileY, int centeredSquareSize, TileChangeType)</c>
    /// （dump.cs 56282）；传 5 个实参正好唯一命中它，不会歧义。
    /// </summary>
    private void FlushBatch()
    {
        if (_batchCount <= 0) return;
        var size = Math.Max(_batchMaxX - _batchMinX + 1, _batchMaxY - _batchMinY + 1);
        NetMessage.SendTileSquare(Main.myPlayer, _batchMinX, _batchMinY, size, TileChangeType.None);
        _batchMinX = _batchMinY = int.MaxValue;
        _batchMaxX = _batchMaxY = -1;
        _batchCount = 0;
    }

    // ==================== 实体重建 ====================

    private void RestoreExtras()
    {
        // ---- 箱子 ----
        foreach (var s in _section.Chests)
        {
            var wx = _pasteX + s.Ox;
            var wy = _pasteY + s.Oy;
            if (!WorldEdit.InWorld(wx, wy)) continue;

            var oldIdx = Chest.FindChest(wx, wy);
            if (oldIdx != -1) Chest.RemoveChest(oldIdx);

            var idx = Chest.CreateChest(wx, wy, -1);
            if (idx < 0 || idx >= Main.chest.Length) continue;
            var c = Main.chest[idx];
            if (c is null) continue;

            c.name = s.Name ?? string.Empty;
            if (c.item is not null && s.Items.Count > 0)
            {
                var mi = Math.Min(c.item.Length, s.Items.Count);
                for (var t = 0; t < mi; t++)
                {
                    var src = s.Items[t];
                    // Il2CppStructArray 的索引器返回副本：取值改完再写回
                    var slot = c.item[t];
                    slot.type = src.type;
                    slot.stack = src.stack;
                    slot.prefix = src.prefix;
                    slot.favorited = src.favorited;
                    c.item[t] = slot;
                }
            }
            Chest.Assign(c);
        }

        // ---- 标牌 ----
        foreach (var s in _section.Signs)
        {
            var wx = _pasteX + s.Ox;
            var wy = _pasteY + s.Oy;
            if (!WorldEdit.InWorld(wx, wy)) continue;
            var idx = Sign.ReadSign(wx, wy, true);
            if (idx < 0 || idx >= Main.sign.Length) continue;
            var sg = Main.sign[idx];
            if (sg is not null && !string.IsNullOrEmpty(s.Text)) sg.text = s.Text;
        }

        // ---- TileEntity ----
        foreach (var e in _section.Entities)
        {
            var wx = _pasteX + e.Ox;
            var wy = _pasteY + e.Oy;
            if (!WorldEdit.InWorld(wx, wy)) continue;
            TileEntity.PlaceEntityNet(wx, wy, e.Type);
        }
    }

    // ==================== Tick（状态机） ====================

    public override void Tick()
    {
        var cfg = CheatFeatureHost.Config;
        var enabled = cfg is { CopyPasteEnabled: true };
        if (!enabled)
        {
            _enabled = false;
            if (_pasting) _pasting = false;
            _selectArmed = false;
            _selecting = false;
            _wasMouseLeft = false;
            return;
        }
        _enabled = true;
        if (Main.gameMenu) return;

        if (_pasting) UpdatePaste();

        // 瞄准点：统一用 WorldEdit 的官方触摸映射（MouseScreen + screenPosition），
        // 不用 Main.MouseWorld —— 手机版准星有「离角色最大距离」钳制，落点会和手指脱节。
        var aim = WorldEdit.AimTilePos();
        var held = WorldEdit.IsWorldHeld();

        // ① 圈地中：终点**只在手指按住时**跟手 —— 严格照原版
        //    `if (m_selecting) { if (TouchTilePos(tx,ty)) { m_selectEndX=tx; m_selectEndY=ty; } }`。
        //    抬手那一帧触摸已经结束（TouchTilePos 返回 false），终点必须保持按下时的最后值，
        //    否则会用抬手后的鼠标坐标覆盖终点 → 复制的区域比圈出来的大/小（就是「预览和圈的不一样」）。
        if (_selecting && held)
        {
            _selEndX = (int)aim.X;
            _selEndY = (int)aim.Y;
        }

        // ② 抬起即完成复制（原版：HasTouchEnded → Copy(min,max) 后 return）
        if (_selecting && !held && _wasMouseLeft)
        {
            _selecting = false;
            if (Copy(Math.Min(_selStartX, _selEndX), Math.Min(_selStartY, _selEndY),
                     Math.Max(_selStartX, _selEndX), Math.Max(_selStartY, _selEndY)))
                ModernUI.Toast("复制完成");
            _wasMouseLeft = false;
            return;
        }

        // ③ 锚点跟手（按住更新，抬起保持最后位置；原版 DrawSelectionPreview 里 TouchTilePos 同义）
        if (held)
        {
            _anchorX = (int)aim.X;
            _anchorY = (int)aim.Y;
        }
        if (!_hasAnchor)
        {
            _anchorX = (int)aim.X;
            _anchorY = (int)aim.Y;
            _hasAnchor = true;
        }

        // ④ 按下沿：圈地设置起点
        if (held && !_wasMouseLeft && _selectArmed)
        {
            _selStartX = (int)aim.X;
            _selStartY = (int)aim.Y;
            _selEndX = _selStartX;
            _selEndY = _selStartY;
            _selectArmed = false;
            _selecting = true;
        }
        _wasMouseLeft = held;
    }

    // ==================== 预览（ModOverlay 背景层，等价原版 GetBackgroundDrawList） ====================

    /// <summary>
    /// 对应原版 <c>DrawSelectionPreview()</c>：
    /// 选中中 → 红色半透明填充 + 红框；已复制 → 绿框 + 彩色色块（≤5000 格）；
    /// 粘贴中 → 黄框。全屏地图态整段跳过（原版首行 <c>if (Main.mapFullscreen) return;</c>）。
    /// </summary>
    private void DrawSelectionPreview(ImDrawListPtr dl)
    {
        if (!_enabled) return;
        if (Main.gameMenu) return;
        if (Main.mapFullscreen) return;

        // 锚点：只认触摸（手指按住跟手；抬起保持最后触摸位置，绝不用准心兜底，避免回弹）。
        // 与原版一致——这段在「圈地中」判断**之前**，所以拖动过程中锚点一路跟到终点。
        int ax = _anchorX, ay = _anchorY;
        if (WorldEdit.IsWorldHeld())
        {
            var touch = WorldEdit.AimTilePos();
            ax = (int)touch.X;
            ay = (int)touch.Y;
            _anchorX = ax;
            _anchorY = ay;
        }
        if (!_hasAnchor) { _anchorX = ax; _anchorY = ay; _hasAnchor = true; }
        // 预览位置 = 锚点 + 粘贴位置微调偏移（所见即所得，与粘贴落点一致）
        ax += _pasteOffsetX;
        ay += _pasteOffsetY;

        // ① 圈地中：半透明红色填充 + 红色描边（原版 AddRectFilled + AddRect）
        if (_selecting)
        {
            var x1 = Math.Min(_selStartX, _selEndX);
            var x2 = Math.Max(_selStartX, _selEndX);
            var y1 = Math.Min(_selStartY, _selEndY);
            var y2 = Math.Max(_selStartY, _selEndY);
            var w = x2 - x1 + 1;   // 含两端点：与原版 (x2+1)、(y2+1) 的矩形一致
            var h = y2 - y1 + 1;
            WorldEdit.FillWorldTiles(dl, x1, y1, w, h, WorldEdit.SelectFill);
            WorldEdit.StrokeWorldTiles(dl, x1, y1, w, h, WorldEdit.SelectStroke, 2f);
            return;
        }

        // ② 已复制：绿框 +（贴图批次不可用时）彩色内容预览
        if (_section.Ready)
        {
            if (WorldEdit.InWorld(ax, ay))
            {
                var w = _section.W;
                var h = _section.H;

                // 内容色块预览（原版 ShowContentPreview && !ShowTexturePreview && total<=5000）。
                // 原版 DrawUI 里把 ShowTexturePreview / ShowContentPreview 都强制成 true，
                // 所以这段**永不执行**：剪贴板预览只有真贴图（DrawGameBatchPreview）那一份。
                // 这里同样恒为 false，保留代码形状以便与原版逐行对照。
                if (ShowContentPreview && !ShowTexturePreview && w * h <= MaxContentPreview)
                {
                    for (var y = 0; y < h; y++)
                    {
                        for (var x = 0; x < w; x++)
                        {
                            var t = _section.Tiles[y * w + x];
                            if (t.pr == 0 && t.pg == 0 && t.pb == 0) continue;   // 空格/无色不画
                            WorldEdit.FillWorldTile(dl, ax + x, ay + y,
                                UnpackColor(t.pr, t.pg, t.pb, 200));
                        }
                    }
                }

                WorldEdit.StrokeWorldTiles(dl, ax, ay, w, h, WorldEdit.CopiedStroke, 2f);
            }
        }

        // ③ 粘贴中：黄色描边（原版 AddRect 3px）
        if (_pasting)
        {
            WorldEdit.StrokeWorldTiles(dl, _pasteX, _pasteY, _section.W, _section.H,
                WorldEdit.PasteStroke, 3f);
        }
    }

    /// <summary>IM_COL32(r, g, b, a) 的即时打包（颜色值来自地图表，不是常量表，故不缓存）。</summary>
    private static uint UnpackColor(byte r, byte g, byte b, byte a) =>
        WorldEdit.ColorU32(r, g, b, a);

    // ==================== 真实贴图预览 ====================

    /// <summary>
    /// 对应原版 <c>DrawGameBatchPreview()</c>：**Main.DoDraw 之后**自己 Begin 一批 SpriteBatch 画真图块。
    /// 挂点见 <see cref="CheatRuntime"/>（`On.Terraria.Main.DoDraw` 里 orig 之后调用）——
    /// 与 Terraria-Imgui 的 MainDoDrawHOOK 完全同一时机：游戏本帧不会再覆盖，批次也已 End。
    ///
    /// 坐标口径 1:1 抄原版：
    /// <list type="bullet">
    /// <item>锚点世界像素 → <c>WorldToScreen</c>（物理像素），与绿框同源；</item>
    /// <item>批次用**单位矩阵**（无变换），所以批次坐标是帧缓冲**逻辑像素**；</item>
    /// <item>换算：<c>逻辑 = 物理 × (Main.screenWidth / DisplaySize.x)</c>，
    /// 起点 = a0×s，每格步长 = (WorldToScreen(锚点+1格) − a0)×s。</item>
    /// </list>
    /// 以前这里用 <c>格*16 − Main.screenPosition</c> + 相机矩阵：口径与覆盖层物理像素不是一回事，
    /// 所以预览位置/大小都对不上（这也是「预览和圈的不一样」的根因之一）。
    /// </summary>
    public override void DrawWorldTiles(SpriteBatch batch)
    {
        _texturePreviewDrawn = false;
        if (!_enabled || batch is null) return;
        if (Main.gameMenu || Main.mapFullscreen) return;   // 地图态：世界批次预览无意义
        if (!_section.Ready) return;                        // 原版：ShowTexturePreview 恒真 + ready
        if (_pasting) return;                               // 粘贴中由黄框指示，不叠贴图

        // 锚点：只认触摸（手指按住跟手；抬起保持最后位置，绝不用准心兜底，避免回弹）。
        // 原版 DrawGameBatchPreview 与 DrawSelectionPreview 各更新一次，这里同样两处都更新。
        var touchTile = WorldEdit.AimTilePos();
        if (WorldEdit.IsWorldHeld())
        {
            _anchorX = (int)touchTile.X;
            _anchorY = (int)touchTile.Y;
        }
        if (!_hasAnchor) { _anchorX = (int)touchTile.X; _anchorY = (int)touchTile.Y; _hasAnchor = true; }

        var ax = _anchorX + _pasteOffsetX;
        var ay = _anchorY + _pasteOffsetY;
        if (!WorldEdit.InWorld(ax, ay)) return;

        // 已有活批次不乱动，安全跳过（原版 SB_BeginWorld 前的 `SBBegun` 判定）
        if (batch._beginCalled)
        {
            if (!_previewReported)
            {
                _previewReported = true;
                CheatMenuMod.Log?.Warn("[copypaste] 贴图预览跳过：spriteBatch 已有活动批次");
            }
            return;
        }
        // ---- 坐标基准：与绿框同源（WorldToScreen = 覆盖层物理像素）----
        // 批次(帧缓冲逻辑像素) = 物理像素 × (游戏逻辑分辨率 / 覆盖层物理分辨率)（全屏线性拉伸假设）
        var a0 = WorldEdit.WorldToScreen(new XnaVector2(ax * 16f, ay * 16f));
        var ax1 = WorldEdit.WorldToScreen(new XnaVector2((ax + 1) * 16f, ay * 16f));
        var ay1 = WorldEdit.WorldToScreen(new XnaVector2(ax * 16f, (ay + 1) * 16f));

        var physW = ImGui.GetIO().DisplaySize.X;
        var physH = ImGui.GetIO().DisplaySize.Y;
        var logW = (float)Main.screenWidth;
        var logH = (float)Main.screenHeight;
        var sx = physW > 0f && logW > 0f ? logW / physW : 1f;
        var sy = physH > 0f && logH > 0f ? logH / physH : 1f;

        // 锚点在批次空间的起点 + 每格步长（物理 px × 逻辑/物理 比），再叠用户微调偏移
        var originX = a0.X * sx;
        var originY = a0.Y * sy;
        var stepX = (ax1.X - a0.X) * sx;
        var stepY = (ay1.Y - a0.Y) * sy;

        if (!WorldEdit.BeginWorldBatch(batch))
        {
            if (!_previewReported)
            {
                _previewReported = true;
                CheatMenuMod.Log?.Warn("[copypaste] 贴图预览跳过：SpriteBatch.Begin(单位矩阵) 失败");
            }
            return;
        }

        var w = _section.W;
        var h = _section.H;
        var drawn = 0;

        try
        {
            // 墙 pass（32×32 源框，位置 −8 格像素）
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var t = _section.Tiles[y * w + x];
                    if (t.wall <= 0) continue;
                    if (WorldEdit.DrawWallTextureAt(batch, originX + x * stepX - 8f * sx,
                            originY + y * stepY - 8f * sy, t.wall, t.wallFrameX, t.wallFrameY, t.wallColor))
                        drawn++;
                }
            }

            // 方块 pass（16×16 源框）
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var t = _section.Tiles[y * w + x];
                    if (!t.active) continue;
                    if (WorldEdit.DrawTileTextureAt(batch, originX + x * stepX, originY + y * stepY,
                            t.type, t.frameX, t.frameY, t.color))
                        drawn++;
                }
            }
        }
        finally
        {
            WorldEdit.EndWorldBatch(batch);
        }

        _texturePreviewDrawn = drawn > 0;
        if (!_previewReported)
        {
            _previewReported = true;
            CheatMenuMod.Log?.Info($"[copypaste] 贴图预览首帧：{w}x{h} 格 → 画 {drawn} 格；" +
                $"锚点 ({ax},{ay})，起点 ({originX:0},{originY:0})，步长 ({stepX:0.00},{stepY:0.00})，" +
                $"逻辑 {logW:0}x{logH:0} / 物理 {physW:0}x{physH:0}，s=({sx:0.0000},{sy:0.0000})");
        }
    }

    /// <summary>截图/日志排查用：把当前预览状态打成一行（复制完成后点一次就够）。</summary>
    public static string PreviewDiagnostics()
    {
        var inst = CheatFeatureHost.All;
        for (var i = 0; i < inst.Count; i++)
        {
            if (inst[i] is WorldEditCopyPasteFeature f) return f.DiagnoseSelf();
        }
        return "copypaste 未注册";
    }

    private string DiagnoseSelf() =>
        $"enabled={_enabled} ready={_section.Ready} {_section.W}x{_section.H} " +
        $"anchor=({_anchorX},{_anchorY}) offset=({_pasteOffsetX},{_pasteOffsetY}) " +
        $"texturePreviewDrawn={_texturePreviewDrawn} pasting={_pasting}";

    // ==================== UI ====================

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("区域复制粘贴", "COPY PASTE");

        RowToggle("##copypasteenabled", "启用", cfg.CopyPasteEnabled, v => cfg.CopyPasteEnabled = v);

        ModernUI.SectionTitle("复制");
        ModernUI.DimText("按下触摸起点，拖动圈范围，抬手即复制");
        if (!_selectArmed && !_selecting)
        {
            if (ModernUI.PrimaryButton("开始复制（按下拖动抬手）")) ArmSelect();
        }
        else
        {
            if (_selectArmed)
            {
                ModernUI.DimText("等待触摸：按下 = 起点，拖动预览，抬起即复制");
            }
            else
            {
                ModernUI.BeginRow();
                ModernUI.RowLabel("选区",
                    $"({_selStartX},{_selStartY}) → ({_selEndX},{_selEndY})");
                ModernUI.EndRow();
            }
            if (ModernUI.GhostButton("取消复制")) CancelSelect();
        }

        ModernUI.SectionTitle("粘贴");
        ModernUI.DimText("点击按钮，就地粘贴到预览位置");
        if (!_pasting && ModernUI.PrimaryButton("开始粘贴（预览落点）"))
        {
            StartPaste(_anchorX + _pasteOffsetX, _anchorY + _pasteOffsetY);
        }
        RowToggle("##pasteclear", "粘贴后清除复制", cfg.PasteClearAfter, v => cfg.PasteClearAfter = v);

        var mode = cfg.PastePlaceMode;
        ModernUI.BeginRow();
        ModernUI.RowLabel("粘贴方式");
        if (ModernUI.ComboField("##pastemode", ["直写 (SendTileRect)", "发包 (TileManipulation)"], ref mode))
            cfg.PastePlaceMode = Math.Clamp(mode, 0, 1);
        ModernUI.EndRow();
        if (cfg.PastePlaceMode == 1)
        {
            var tps = (float)cfg.PasteTilesPerSecond;
            if (ModernUI.RowSlider("##tps", "格/秒", "60-1800", ref tps, 60f, 1800f, "0"))
            {
                cfg.PasteTilesPerSecond = (int)Math.Clamp(tps, 60f, 1800f);
                CheatFeatureHost.MarkDirty();
            }
        }

        if (_section.Ready)
        {
            ModernUI.BeginRow();
            ModernUI.RowLabel("粘贴位置微调", "（格）");
            ModernUI.EndRow();
            var offX = (float)_pasteOffsetX;
            if (ModernUI.RowSlider("##pasteoffx", "左右", $"-{_section.W}-0", ref offX, -_section.W, 0f, "0"))
                _pasteOffsetX = (int)offX;
            var offY = (float)_pasteOffsetY;
            if (ModernUI.RowSlider("##pasteoffy", "上下", $"-{_section.H}-0", ref offY, -_section.H, 0f, "0"))
                _pasteOffsetY = (int)offY;

            ModernUI.BeginRow();
            ModernUI.RowLabel("已复制", $"{_section.W}x{_section.H} 格");
            ModernUI.EndRow();
            if (ModernUI.GhostButton("清除复制")) ClearCopied();
        }

        if (_pasting)
        {
            ModernUI.BeginRow();
            ModernUI.RowLabel("粘贴中", $"{_pasteDone}/{_pasteTotal}");
            ModernUI.EndRow();
            if (ModernUI.GhostButton("取消粘贴")) CancelPaste();
        }

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