using System;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.GameContent.NetModules;
using Terraria.Net;
using Terraria.UI;
using Terraria.Map;
using Terraria.ObjectData;

namespace Pylon.ModLoader;

/// <summary>
/// 传送晶塔管线：把 mod 晶塔塞进游戏自己的晶塔列表（<c>Main.PylonSystem._pylons</c>），
/// 让传送、附近判定、地图图标全部走原版逻辑；只有"列表里没有"这件事我们要补上。
/// 另加：CanPlacePylon 的放置许可、NPC 数量检查转发给作者、地图上的连线与图标、点开地图。
/// </summary>
internal sealed class PylonPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static PylonPipeline Instance { get; } = new();

    public string Name => "pylon";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>晶塔占的格数（3x4 的物体，判定"在附近"按这个方框算）。</summary>
    private const int PylonSpan = 4;

    private static List<(int X, int Y, int Type)> _current = [];
    private static List<(int X, int Y, int Type)>? _stripped;
    private static int _requests;
    private static int _npcChecks;

    public bool Install()
    {
        On.Terraria.GameContent.TeleportPylonsSystem.UpdatePylonsListAndBroadcastChanges += UpdateList;
        On.Terraria.GameContent.TeleportPylonsSystem.IsPlayerNearAPylon += NearAPylon;
        On.Terraria.GameContent.TeleportPylonsSystem.HandleTeleportRequest += HandleRequest;
        On.Terraria.GameContent.TeleportPylonsSystem.DoesPositionHaveEnoughNPCs += PositionHasEnoughNPCs;
        On.Terraria.Map.TeleportPylonsMapLayer.Draw += DrawMap;
        On.Terraria.TileObject.CanPlace += CanPlace;

        Log.Info("[pylon] 已挂（列表注入 / 附近判定 / 传送请求 / NPC 检查 / 放置许可 / 地图图标与连线）；晶石走 tiledraw 的 SpecialDraw");
        return true;
    }

    public void Refresh() => PylonTable.Assign();

    /// <summary>
    /// 安装期一次：只发号（PylonType 必须早于 <c>方块安装</c> 的 SetStaticDefaults 定下来），
    /// 不再自己跑一遍作者的 SetStaticDefaults —— 那一步由 <see cref="TileTable.Build"/> 统一做；
    /// 这里再跑会撞上 "Tile data is locked"，把晶塔的 3x4 形状挡在 TileObjectData 之外。
    /// </summary>
    internal void Build()
    {
        PylonTable.Assign();

        if (PylonTable.Count > 0) Log.Info($"[pylon] 安装：{PylonTable.Count} 个晶塔（类型号 {PylonTable.Base} 起）");
    }

    /// <summary>
    /// 立刻重算一次游戏晶塔列表：<c>RequestImmediateUpdate</c> 只是打标记，要等下一帧
    /// <c>TeleportPylonsSystem.Update</c> 才真正刷新。放置/拆除后想马上看到结果时用这里。
    /// </summary>
    internal static void RefreshNow()
    {
        var system = Main.PylonSystem;
        if (system is null) return;

        try
        {
            system.RequestImmediateUpdate();
            system.Update();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[pylon] 立刻刷新晶塔列表异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>在 (x, y) 建晶塔实体（作者继承的那个 TEModdedPylon）；没有可用的返回 -1。</summary>
    internal static int PlaceEntity(int x, int y)
    {
        foreach (var entity in TileEntityBinding.All)
        {
            if (entity is not TEModdedPylon template) continue;
            if (TileEntityBinding.Find(template, x, y) >= 0) return -1;
            if (!template.IsTileValidForEntity(x, y)) continue;
            return TileEntityBinding.Place(template, x, y);
        }

        return -1;
    }

    // ===================== 列表抓取 =====================

    private static Il2CppSystem.Collections.Generic.List<TeleportPylonInfo> List(TeleportPylonsSystem system) => system._pylons;

    private static TeleportPylonInfo Info((int X, int Y, int Type) entry) => new()
    {
        PositionInTiles = new Point16(entry.X, entry.Y),
        TypeOfPylon = (TeleportPylonType)entry.Type,
    };

    /// <summary>世界里所有 mod 晶塔：左上角格 + 类型号（从实体表里取，方块帧不可靠时也能定位）。</summary>
    private static List<(int X, int Y, int Type)> Current()
    {
        var found = new List<(int, int, int)>();
        foreach (var entity in TileEntityBinding.All)
        {
            if (entity is not TEModdedPylon pylon) continue;
            if (!TileEntityBinding.IsLive(pylon)) continue;
            if (pylon.TryGetModPylon() is not { } modPylon) continue;

            found.Add((pylon.Position.X, pylon.Position.Y, modPylon.PylonType));
        }

        return found;
    }

    /// <summary>把 mod 晶塔从游戏列表里摘掉（原版遍历列表时会崩，必须先摘）。</summary>
    private static List<(int X, int Y, int Type)> Strip(Il2CppSystem.Collections.Generic.List<TeleportPylonInfo> list)
    {
        var removed = new List<(int, int, int)>();
        for (var k = list.Count - 1; k >= 0; k--)
        {
            var info = list[k];
            if ((int)info.TypeOfPylon < PylonTable.Base) continue;

            removed.Add((info.PositionInTiles.X, info.PositionInTiles.Y, (int)info.TypeOfPylon));
            list.RemoveAt(k);
        }

        return removed;
    }

    private static void Send((int X, int Y, int Type) entry, NetTeleportPylonModule.SubPacketType kind)
    {
        if (Main.netMode == 0) return; // 单人没有要广播的对象

        try
        {
            NetManager.Instance.BroadcastOrLoopback(NetTeleportPylonModule.SerializePylonWasAddedOrRemoved(Info(entry), kind));
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[pylon] 广播晶塔变化异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>游戏刷新自己的晶塔列表：先摘 mod 的，跑原版，再把 mod 的补回去并广播增删。</summary>
    private static void UpdateList(On.Terraria.GameContent.TeleportPylonsSystem.orig_UpdatePylonsListAndBroadcastChanges orig,
                                   TeleportPylonsSystem system)
    {
        Strip(List(system));
        orig(system);

        if (PylonTable.Count == 0) return;

        try
        {
            var list = List(system);
            var now = Current();
            var before = _current;

            foreach (var entry in now) list.Add(Info(entry));

            foreach (var entry in now)
                if (!before.Contains(entry)) Send(entry, NetTeleportPylonModule.SubPacketType.PylonWasAdded);

            foreach (var entry in before)
                if (!now.Contains(entry)) Send(entry, NetTeleportPylonModule.SubPacketType.PylonWasRemoved);

            _current = now;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[pylon] 刷新晶塔列表异常：{Safe.Describe(e)}");
        }
    }

    // ===================== 附近判定 =====================

    private static bool NearAPylon(On.Terraria.GameContent.TeleportPylonsSystem.orig_IsPlayerNearAPylon orig, Player player)
    {
        if (orig(player)) return true;
        if (PylonTable.Count == 0) return false;

        var reach = TileReachCheckSettings.Pylons;
        reach.GetTileRegion(player, out var lx, out var ly, out var hx, out var hy);

        foreach (var entity in TileEntityBinding.All)
        {
            if (entity is not TEModdedPylon pylon) continue;
            if (!TileEntityBinding.IsLive(pylon)) continue;

            var x = pylon.Position.X;
            var y = pylon.Position.Y;
            if (x <= hx && x + PylonSpan > lx && y <= hy && y + PylonSpan > ly) return true;
        }

        return false;
    }

    // ===================== 传送 =====================

    /// <summary>目的地生物群系检查：围着晶塔扫一次场景，再问作者。</summary>
    private static bool AcceptsBiome(TeleportPylonsSystem system, ModPylon pylon, TeleportPylonInfo info)
    {
        var metrics = system._sceneMetrics;
        var settings = new SceneMetricsScanSettings
        {
            BiomeScanCenterPositionInWorld = info.PositionInTiles.ToWorldCoordinates(),
        };
        metrics.Scan(settings);
        return pylon.ValidTeleportCheck_BiomeRequirements(info, metrics);
    }

    /// <summary>传送请求：mod 晶塔先做生物群系检查，再临时塞回列表让原版走完整流程。</summary>
    private static void HandleRequest(On.Terraria.GameContent.TeleportPylonsSystem.orig_HandleTeleportRequest orig,
                                      TeleportPylonsSystem system, TeleportPylonInfo info, int playerIndex)
    {
        var pylon = PylonTable.Of((int)info.TypeOfPylon);
        if (pylon is not null)
        {
            try
            {
                if (!AcceptsBiome(system, pylon, info)) return;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[pylon] {pylon.Name}.ValidTeleportCheck_BiomeRequirements 异常：{Safe.Describe(e)}");
                return;
            }
        }

        var back = _stripped;
        if (back is not null)
        {
            var list = List(system);
            foreach (var entry in back) list.Add(Info(entry));
        }

        _requests++;
        try
        {
            orig(system, info, playerIndex);
        }
        finally
        {
            _requests--;
            if (back is not null) Strip(List(system));
        }
    }

    /// <summary>NPC 数量检查：原版只认自己列表里的晶塔，mod 晶塔转发给作者。</summary>
    private static bool PositionHasEnoughNPCs(On.Terraria.GameContent.TeleportPylonsSystem.orig_DoesPositionHaveEnoughNPCs orig,
                                              int count, Point16 center)
    {
        if (_requests == 0 || _npcChecks > 0) return orig(count, center);

        foreach (var entry in _current)
        {
            if (entry.X != center.X || entry.Y != center.Y) continue;
            if (PylonTable.Of(entry.Type) is not { } pylon) continue;

            _npcChecks++;
            try
            {
                return pylon.ValidTeleportCheck_NPCCount(Info(entry), count);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[pylon] {pylon.Name}.ValidTeleportCheck_NPCCount 异常：{Safe.Describe(e)}");
                return false;
            }
            finally
            {
                _npcChecks--;
            }
        }

        return orig(count, center);
    }

    // ===================== 放置许可 =====================

    /// <summary>CanPlacePylon：一个类型世界里只留一座。</summary>
    private static bool CanPlace(On.Terraria.TileObject.orig_CanPlace orig, int x, int y, int type, int style, int dir,
                                 out TileObject objectData, bool onlyCheck, Il2CppSystem.Nullable<int> forcedRandom)
    {
        if (ModContent.Find(ContentKind.Tile, type) is ModPylon pylon)
        {
            bool allowed;
            try
            {
                allowed = pylon.CanPlacePylon();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[pylon] {pylon.Name}.CanPlacePylon 异常：{Safe.Describe(e)}");
                allowed = false;
            }

            if (!allowed)
            {
                objectData = default!;
                return false;
            }
        }

        return orig(x, y, type, style, dir, out objectData, onlyCheck, forcedRandom);
    }

    // ===================== 地图 =====================

    private static void DrawMap(On.Terraria.Map.TeleportPylonsMapLayer.orig_Draw orig, TeleportPylonsMapLayer layer,
                                ref MapOverlayDrawContext context, ref string text)
    {
        List<(int X, int Y, int Type)> mine = [];

        if (PylonTable.Count > 0)
        {
            try
            {
                mine = Strip(List(Main.PylonSystem));
                _stripped = mine.Count > 0 ? mine : null;
                if (mine.Count > 0) DrawLines(mine, ref context);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[pylon] 地图连线异常：{Safe.Describe(e)}");
            }
        }

        // 原版必须画：它是整个地图图层的骨架，抛在这里地图就关不掉。
        try
        {
            orig(layer, ref context, ref text);
        }
        finally
        {
            _stripped = null;
            if (mine.Count > 0)
            {
                try
                {
                    var list = List(Main.PylonSystem);
                    foreach (var entry in mine) list.Add(Info(entry));
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[pylon] 地图列表回填异常：{Safe.Describe(e)}");
                }
            }
        }

        if (mine.Count == 0) return;

        try
        {
            var near = TeleportPylonsSystem.IsPlayerNearAPylon(Main.LocalPlayer);
            var color = near ? new Color(255, 255, 255, 255) : new Color(64, 64, 64, 127);

            var mouseOver = text;
            var before = mouseOver;
            foreach (var entry in mine)
            {
                var info = Info(entry);
                if (!TeleportPylonsMapLayer.IsRevealed(info)) continue;
                if (PylonTable.Of(entry.Type) is not { } pylon) continue;

                try
                {
                    pylon.DrawMapIcon(ref context, ref mouseOver, info, near, color, 1f, 2f);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[pylon] {pylon.Name}.DrawMapIcon 异常：{Safe.Describe(e)}");
                }
            }

            if (!string.Equals(mouseOver, before, StringComparison.Ordinal)) text = mouseOver;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[pylon] 地图图标异常：{Safe.Describe(e)}");
        }
    }

    private static void DrawLines(List<(int X, int Y, int Type)> entries, ref MapOverlayDrawContext context)
    {
        var from = Main.MapPylonTile;
        if (!Main.mapFullscreen || from.X == -1 || from.Y == -1) return;

        var texture = TextureAssets.Extra[182].Value;
        var frame = new SpriteFrame(11, 1, 0, 0) { PaddingY = 0 };
        var start = Center(ref context, texture, frame, from.X, from.Y);

        foreach (var entry in entries)
        {
            if ((entry.X == from.X && entry.Y == from.Y) || !TeleportPylonsMapLayer.IsRevealed(Info(entry))) continue;

            var end = Center(ref context, texture, frame, entry.X, entry.Y);
            TeleportPylonsMapLayer.DrawLine(Main.spriteBatch, start, end, new Color(0, 0, 0, 255), 6);
            TeleportPylonsMapLayer.DrawLine(Main.spriteBatch, start, end, new Color(255, 255, 255, 255), 2);
        }
    }

    private static Vector2 Center(ref MapOverlayDrawContext context, Texture2D texture, SpriteFrame frame, int x, int y)
    {
        var region = context.GetUnclampedDrawRegion(texture, new Vector2(x + 1.5f, y + 2f), frame, 1f, Alignment.Center);
        return new Vector2(region.X + region.Width / 2, region.Y + region.Height / 2);
    }

    /// <summary>点晶塔打开全屏地图（ModPylon.RightClick 走这里）：原点按格子帧折，并清掉自动平移/重置，把视野落在晶塔上。</summary>
    internal static bool OpenMap(int i, int j)
    {
        try
        {
            var origin = TopLeftOf(i, j, Main.tile[i, j]);

            if (!Main.mapFullscreen) Main.LocalPlayer.TryOpeningFullscreenMap();
            if (!Main.mapFullscreen) return false;

            Main.MapPylonTile = origin;
            Main.PanTargetMapFullscreen = false;
            Main.resetMapFull = false;
            Main.mapFullscreenPos = new Vector2(origin.X + 1.5f, origin.Y + 2f);
            return true;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[pylon] 打开地图异常：{Safe.Describe(e)}");
            return false;
        }
    }

    /// <summary>
    /// 这一格所属多格物体的左上角：按格子的 frameX/frameY 折（<c>TileObjectData.Origin</c>
    /// 只对放置锚点那一格成立，点塔身中间会算偏一格）。
    /// </summary>
    private static Point16 TopLeftOf(int i, int j, Tile tile)
    {
        var data = TileObjectData.GetTileData(tile);
        if (data is null || data.CoordinateFullWidth <= 0 || data.CoordinateFullHeight <= 0)
            return new Point16(i, j);

        return new Point16(i - tile.frameX % data.CoordinateFullWidth / 18,
                           j - tile.frameY % data.CoordinateFullHeight / 18);
    }

}
