using System;
using System.Linq;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using RegressionTest.Content;
using Terraria;
using Terraria.ID;

namespace RegressionTest
{
    /// <summary>
    /// 程序化布景：把测试场景直接建在玩家旁边，避免手搓摆放。
    /// 所有放置失败都会打进日志（[rt] 布景），不抛异常。
    /// </summary>
    internal static class RegTestScene
    {
        private static int _anchorX = -1;
        private static int _anchorY = -1;

        /// <summary>布景水池位置（水样式场景效果的激活锚点）；未建时 (-1,-1)。</summary>
        public static (int X, int Y) WaterPos;

        /// <summary>布景门位置（门区监视的锚点，门本体底格）；未建时 (-1,-1)。</summary>
        public static (int X, int Y) DoorPos;

        static RegTestScene() => WaterPos = (-1, -1);

        private static void Status(string s)
        {
            RegTestTab.Status(s);
        }

        private static Player Me => Main.player[Main.myPlayer];

        private static bool EnsureAnchor()
        {
            var p = Me;
            if (p == null || !p.active)
            {
                Status("需要先进世界。");
                return false;
            }

            if (_anchorX < 0)
            {
                // 玩家右下方找地面：从玩家所在列向右偏 6 格，向下找第一块实心格。
                var px = (int)(p.position.X / 16f) + 6;
                var py = (int)(p.position.Y / 16f);
                for (var d = 0; d < 200; d++)
                {
                    if (py + d >= Main.maxTilesY - 10) break;
                    var t = Main.tile[px, py + d];
                    if (t is { } tt && tt.active() && Main.tileSolid[tt.type])
                    {
                        _anchorX = px;
                        _anchorY = py + d; // 地面块本身的 Y（顶面 = _anchorY 上一格）
                        break;
                    }
                }
            }

            if (_anchorX < 0)
            {
                Status("没找到地面锚点（往下 200 格无实心块）。");
                return false;
            }
            return true;
        }

        /// <summary>直接把一格变成实心方块（跳过放置校验；石块无 framing 问题）。</summary>
        private static void SetSolid(int i, int j, int type)
        {
            var t = Main.tile[i, j];
            if (t == null) return;
            t.active(true);
            t.type = (ushort)type;
            t.frameX = 0;
            t.frameY = 0;
            t.slope(0);
            t.halfBrick(false);
        }

        private static void ClearTile(int i, int j)
        {
            var t = Main.tile[i, j];
            if (t == null) return;
            t.active(false);
            t.wall = 0;
            t.liquid = 0;
        }

        /// <summary>① 平台（24 宽石台）+ 家具×2（双色对照）+ 作物×4（地表）。</summary>
        public static void BuildPlatformScene()
        {
            if (!EnsureAnchor()) return;
            try
            {
                var gx = _anchorX;
                var gy = _anchorY; // 地面块 Y；顶面行 = gy - 1

                // 清理并铺平台：24 宽、往地下垫 3 行石块
                for (var i = gx; i < gx + 24; i++)
                for (var j = gy - 8; j < gy + 3; j++)
                    ClearTile(i, j);
                for (var i = gx; i < gx + 24; i++)
                for (var j = gy; j < gy + 3; j++)
                    SetSolid(i, j, TileID.Stone);

                // 家具 ×2：左件 (gx+2, gy-1 顶面锚)，右件 (gx+16, gy-1)
                var furn = ModContent.TileType<FurnitureTile>();
                WorldGen.PlaceObject(gx + 2, gy - 1, furn, mute: true);
                WorldGen.PlaceObject(gx + 16, gy - 1, furn, mute: true);

                // 作物 ×4：地表一排（gx+6..gx+9 顶面）
                var crop = ModContent.TileType<CropTile>();
                for (var k = 0; k < 4; k++)
                    WorldGen.PlaceObject(gx + 6 + k, gy - 1, crop, mute: true);

                Status($"平台场景完成：家具@({gx + 2},{gy - 4})x2 作物@({gx + 6}..{gx + 9},{gy - 1})。作物等 RandomUpdate 生长。");
            }
            catch (Exception e)
            {
                Status("布平台失败：" + e.Message);
            }
        }

        /// <summary>地下作物（P1-7 地下触发）：在玩家下方 40 格挖个洞种作物。</summary>
        public static void PlantUndergroundCrop()
        {
            if (!EnsureAnchor()) return;
            try
            {
                var gx = _anchorX;
                var gy = _anchorY + 40;
                for (var i = gx; i < gx + 6; i++)
                for (var j = gy - 4; j < gy + 3; j++)
                    ClearTile(i, j);
                for (var i = gx; i < gx + 6; i++)
                    SetSolid(i, gy, TileID.Stone);

                var crop = ModContent.TileType<CropTile>();
                for (var k = 0; k < 2; k++)
                    WorldGen.PlaceObject(gx + 1 + k, gy - 1, crop, mute: true);

                Status($"地下作物已种 @({gx + 1},{gy - 1})×2");
            }
            catch (Exception e)
            {
                Status("地下作物失败：" + e.Message);
            }
        }

        /// <summary>② 门对：把门砌进一段墙（门楣 + 两侧墙段 + 地板），像房门口一样，不要悬空独门。</summary>
        public static void PlaceDoorPair()
        {
            if (!EnsureAnchor()) return;
            try
            {
                var gx = _anchorX + 12;
                var gy = _anchorY; // 地面行（门底在 gy-1）

                // 清 7×6 区域
                for (var i = gx - 3; i <= gx + 3; i++)
                for (var j = gy - 6; j < gy; j++)
                    ClearTile(i, j);

                // 门楣：门上方一行横梁
                for (var i = gx - 1; i <= gx + 1; i++)
                    SetSolid(i, gy - 4, TileID.Stone);
                // 两侧墙段（开口留出 gx 列）
                for (var j = gy - 3; j < gy; j++)
                {
                    for (var i = gx - 3; i < gx; i++) SetSolid(i, j, TileID.Stone);
                    for (var i = gx + 1; i <= gx + 3; i++) SetSolid(i, j, TileID.Stone);
                }
                // 地板（墙段之下）
                for (var i = gx - 3; i <= gx + 3; i++)
                    SetSolid(i, gy, TileID.Stone);

                var door = ModContent.TileType<DoorClosedTile>();
                var ok = WorldGen.PlaceObject(gx, gy - 1, door, mute: true);
                DoorPos = (gx, gy - 1);
                Status($"门对已砌进墙 @({gx},{gy - 1}) PlaceObject={ok}（门楣+两侧墙段+地板；走近应自动开为 {ModContent.TileType<Content.DoorOpenTile>()}）");
            }
            catch (Exception e)
            {
                Status("布门失败：" + e.Message);
            }
        }

        /// <summary>③ 墙三变体：平台左侧 3 列 mod 墙，每列一种逐格属性（色号/隐形/全亮）。</summary>
        public static void PlaceWallTrio()
        {
            if (!EnsureAnchor()) return;
            try
            {
                var bx = _anchorX - 6; // 放平台左侧，避免与家具重叠
                var gy = _anchorY - 4;
                var plain = ModContent.WallType<Content.PlainWall>();
                var dyed = ModContent.WallType<Content.DyedWall>();
                var glow = ModContent.WallType<Content.GlowWall>();

                for (var row = 0; row < 4; row++)
                {
                    var j = gy + row;

                    // 第 1 列：普通墙
                    WorldGen.PlaceWall(bx, j, plain, mute: true);

                    // 第 2 列：染色墙（wallColor=1）
                    WorldGen.PlaceWall(bx + 1, j, dyed, mute: true);
                    var t2 = Main.tile[bx + 1, j];
                    if (t2 != null) t2.wallColor(1);

                    // 第 3 列：全亮墙（fullbright），上两格再加隐形
                    WorldGen.PlaceWall(bx + 2, j, glow, mute: true);
                    var t3 = Main.tile[bx + 2, j];
                    if (t3 != null)
                    {
                        t3.fullbrightWall(true);
                        if (row < 2) t3.invisibleWall(true);
                    }
                }

                Status($"墙三变体已布 @({bx}..{bx + 2},{gy}..{gy + 3})：普通/染色(wallColor=1)/全亮(隐形)");
            }
            catch (Exception e)
            {
                Status("布墙失败：" + e.Message);
            }
        }

        /// <summary>④ 6×5 木屋 + 商人（P1-9 入住）。</summary>
        public static void BuildHouseAndSpawn()
        {
            if (!EnsureAnchor()) return;
            try
            {
                var gx = _anchorX + 30;
                var gy = _anchorY;
                if (gx + 12 >= Main.maxTilesX - 10) gx = _anchorX - 30;

                // 清区
                for (var i = gx - 1; i < gx + 12; i++)
                for (var j = gy - 9; j < gy + 2; j++)
                    ClearTile(i, j);

                // 地板与墙框（木头）
                for (var i = gx; i < gx + 11; i++)
                {
                    SetSolid(i, gy, TileID.Dirt);
                    SetSolid(i, gy - 6, TileID.WoodBlock);
                }
                for (var j = gy - 5; j < gy; j++)
                {
                    SetSolid(gx, j, TileID.WoodBlock);
                    SetSolid(gx + 10, j, TileID.WoodBlock);
                }

                // 屋内木墙
                for (var i = gx + 1; i < gx + 10; i++)
                for (var j = gy - 5; j < gy; j++)
                    WorldGen.PlaceWall(i, j, WallID.Wood, mute: true);

                // 左墙挖出门洞再放原版门（门 1×3：底行落地 gy-1）
                for (var j = gy - 3; j < gy; j++) ClearTile(gx, j);
                var doorOk = WorldGen.PlaceObject(gx, gy - 3, TileID.ClosedDoor, mute: true);
                if (!doorOk) doorOk = WorldGen.PlaceTile(gx, gy - 1, TileID.ClosedDoor, mute: true, forced: true);

                // 桌 + 椅 + 火把
                WorldGen.PlaceObject(gx + 3, gy - 1, TileID.Tables, mute: true);
                WorldGen.PlaceObject(gx + 6, gy - 1, TileID.Chairs, mute: true);
                WorldGen.PlaceTile(gx + 8, gy - 3, TileID.Torches, mute: true, forced: true);

                // 商人（已有存活的就不再生成，避免每次进世界叠一只）
                var type = ModContent.NpcType<Content.MerchantNpc>();
                var existing = 0;
                for (var i = 0; i < Main.npc.Length; i++)
                {
                    var scan = Main.npc[i];
                    if (scan is not { } sc || !sc.active || sc.type != type) continue;
                    existing++;
                }
                if (existing > 0)
                {
                    Status($"商人已存在 ×{existing}，跳过生成");
                    return;
                }

                var src = NPC.GetSpawnSourceForNaturalSpawn();
                var idx = NPC.NewNPC(src, (gx + 5) * 16, (gy - 2) * 16, type);
                var ok = idx >= 0 && idx < Main.npc.Length && Main.npc[idx] is { } n && n.active;

                Status($"房@({gx},{gy}) 商人生成 idx={idx} ok={ok}（等 housing 入住，聊天页看状态）");
            }
            catch (Exception e)
            {
                Status("建住房失败：" + e.Message);
            }
        }

        /// <summary>⑤ 掉血 Boss。</summary>
        public static void SpawnBoss()
        {
            try
            {
                var p = Me;
                if (p == null || !p.active) { Status("需要先进世界。"); return; }
                var type = ModContent.NpcType<Content.DrainBossNpc>();
                var src = NPC.GetSpawnSourceForNaturalSpawn();
                var idx = NPC.NewNPC(src, (int)p.position.X + 300, (int)p.position.Y - 200, type);
                Status($"Boss 已生成 idx={idx}（开'Boss 掉血'开关观察当帧掉血）");
            }
            catch (Exception e)
            {
                Status("生成 Boss 失败：" + e.Message);
            }
        }

        /// <summary>⑥ 倒水：玩家上方小水池灌 mod 水样式（当前场景水）。</summary>
        public static void PourWater()
        {
            if (!EnsureAnchor()) return;
            try
            {
                var gx = _anchorX;
                var gy = _anchorY;
                // U 型槽：两侧石壁，凹槽灌水 4×3
                for (var j = gy - 5; j < gy; j++)
                {
                    SetSolid(gx, j, TileID.Stone);
                    SetSolid(gx + 5, j, TileID.Stone);
                }
                for (var i = gx; i <= gx + 5; i++)
                    SetSolid(i, gy - 5, TileID.Stone);

                for (var i = gx + 1; i < gx + 5; i++)
                for (var j = gy - 4; j < gy; j++)
                {
                    var t = Main.tile[i, j];
                    if (t == null) continue;
                    ClearTile(i, j);
                    t.liquid = 255;
                    t.liquidType(0); // 水
                }

                WaterPos = (gx + 1, gy - 4);
                Status($"水池已建 @({gx + 1},{gy - 4})（60 格内场景效果激活，水变 mod 样式 #{ModContent.GetInstance<Content.RegTestWater>()?.Type ?? -1}）");
            }
            catch (Exception e)
            {
                Status("倒水失败：" + e.Message);
            }
        }

        /// <summary>⑦ 裸 1×1 mod 方块（TileObjectData 空形状对照，P2-4/域3-B3）。</summary>
        public static void PlaceBareTile()
        {
            if (!EnsureAnchor()) return;
            try
            {
                var bare = ModContent.TileType<Content.BareTile>();
                var ok = WorldGen.PlaceTile(_anchorX + 20, _anchorY - 1, bare, mute: true, forced: true);
                Status($"裸方块（无形状）放置 = {ok} @({_anchorX + 20},{_anchorY - 1})");
            }
            catch (Exception e)
            {
                Status("裸方块失败：" + e.Message);
            }
        }

        // ===================== 一键布景（第二幕全套，按依赖顺序） =====================

        /// <summary>平台+家具+作物 → 门对 → 墙三变体 → 房+商人 → 地下作物 → 裸方块 → 水。每步落一行日志。</summary>
        public static void BuildAllScenes()
        {
            Status("一键布景：开始");
            BuildPlatformScene();
            PlaceDoorPair();
            PlaceWallTrio();
            BuildHouseAndSpawn();
            PlantUndergroundCrop();
            PlaceBareTile();
            PourWater();
            Status("一键布景：完成（按 ①..⑦ 顺序执行，见上方各行 [rt] 日志）");
        }

        // ===================== 进世界自动执行 =====================

        /// <summary>进世界后第 N 帧自动布景 + 发物品（回归免点按）。世界卸载时复位。</summary>
        public static int AutoRunFrame = 180;

        /// <summary>全套回归物品：数据物品×2（原版前缀/无）、mod 前缀×1、火焰对、召唤杖、钓具。</summary>
        public static void GiveAllForRegression()
        {
            GiveDataItems();
            GiveModPrefixItem();
            GiveFlameItems();
            GiveSummonItem();
            GiveFishingGear();

            // 火焰探针：主动触发原版懒加载，看 mod 火焰贴图是否被覆盖（P2-2 排查）
            try
            {
                var ft = ModContent.ItemType<Content.FlameItem>();
                Main.instance.LoadItemFlames(ft);
                var a = Terraria.GameContent.TextureAssets.ItemFlame[ft];
                Status($"火焰探针：ItemFlame[{ft}] name={a?.Name} loaded={a?.IsLoaded} state={a?.State}");
            }
            catch (Exception e)
            {
                Status("火焰探针异常：" + e.Message);
            }

            // 聊天标签测试用的类型号速查（P2-1a：[i:类型]）
            var ids = new[]
            {
                ("数据", ModContent.ItemType<Content.DataItem>()),
                ("火焰", ModContent.ItemType<Content.FlameItem>()),
                ("无焰", ModContent.ItemType<Content.NoFlameItem>()),
                ("召唤", ModContent.ItemType<Content.SummonItem>()),
                ("家具", ModContent.ItemType<Content.FurnitureItem>()),
                ("门", ModContent.ItemType<Content.DoorItem>()),
                ("裸块", ModContent.ItemType<Content.BareTileItem>()),
            };
            Status("物品类型号：" + string.Join(" ", ids.Select(t => $"{t.Item1}={t.Item2}")) + "（聊天输 [i:号] 测 P2-1a）");
        }

        /// <summary>世界卸载：清锚点与一次性标记，下次进世界重跑。</summary>
        public static void ResetForWorldReload()
        {
            _anchorX = -1;
            _anchorY = -1;
            WaterPos = (-1, -1);
            DoorPos = (-1, -1);
            AutoRunFrame = 180;
        }

        /// <summary>门区监视：门周围 6×6 每帧扫一遍，类型或帧有变化就落一行（mod 类型 + 中心列全帧值）。</summary>
        public static void WatchDoor()
        {
            var (dx0, dy0) = DoorPos;
            if (dx0 < 0) return;

            ulong sig = 1469598103934665603UL;
            var parts = new System.Collections.Generic.List<string>();
            for (var dy = -4; dy <= 1; dy++)
            for (var dx = -2; dx <= 3; dx++)
            {
                var t = Main.tile[dx0 + dx, dy0 + dy];
                var type = t is { } tt && tt.active() ? (int)tt.type : 0;
                sig = (sig ^ (ulong)type) * 1099511628211UL;
                var isMod = type > 0 && ModContent.Find(ContentKind.Tile, type) is not null;
                if (isMod || (dx == 0 && dy >= -3))
                {
                    var fx = t is { } tf ? tf.frameX : (short)0;
                    var fy = t is { } ff ? ff.frameY : (short)0;
                    parts.Add($"({dx},{dy})={type}/{fx}/{fy}");
                }
            }
            if (sig == _lastDoorSig) return;
            _lastDoorSig = sig;
            RegressionTestMod.Log?.Info("[rt] 门区变化：x0=" + dx0 + " y0=" + dy0 + "  " + string.Join(" ", parts));
        }

        private static ulong _lastDoorSig;

        // ===================== 发物品 =====================

        private static Item Give(int type, int stack = 1, int prefix = 0)
        {
            var p = Me;
            var item = new Item();
            item.SetDefaults(type, null);
            item.stack = stack;
            if (prefix > 0) item.prefix = (byte)prefix;
            p.QuickSpawnItem(p.GetItemSource_Misc(0), item);
            return item;
        }

        /// <summary>数据物品 ×2：一件原版前缀（Heavy）、一件无前缀。</summary>
        public static void GiveDataItems()
        {
            try
            {
                var type = ModContent.ItemType<Content.DataItem>();
                var a = Give(type, prefix: PrefixID.Heavy);
                var b = Give(type);
                Status($"数据物品已发：#1 前缀=Heavy({a.prefix}) #2 无前缀({b.prefix})。存档后读档看 stamp/note/prefix 恢复。");
            }
            catch (Exception e)
            {
                Status("发数据物品失败：" + e.Message);
            }
        }

        /// <summary>数据物品（本 mod 前缀：试炼之印）。</summary>
        public static void GiveModPrefixItem()
        {
            try
            {
                var type = ModContent.ItemType<Content.DataItem>();
                var pfx = ModContent.PrefixType<Content.RegPrefix>();
                var item = Give(type, prefix: pfx);
                Status($"mod 前缀物品已发：prefix={item.prefix}（期望 {pfx}）。");
            }
            catch (Exception e)
            {
                Status("发 mod 前缀物品失败：" + e.Message);
            }
        }

        /// <summary>数据物品（独立 mod 的前缀：陌生之印）——A-2/A-3 卸载场景专用。</summary>
        public static void GiveStrangerPrefixItem()
        {
            try
            {
                if (!ModContent.TryFind<ModPrefix>("RegTestPrefixMod/StrangerPrefix", out var pfx) || pfx is null)
                {
                    Status("陌生之印没找到（RegTestPrefixMod 没装？）");
                    return;
                }
                var type = ModContent.ItemType<Content.DataItem>();
                var item = Give(type, prefix: pfx.Type);
                Status($"陌生前缀物品已发：prefix={item.prefix}（期望 {pfx.Type}）。存档→移走 RegTestPrefixMod→读档 验证 ？占位/pendingPrefix。");
            }
            catch (Exception e)
            {
                Status("发陌生前缀物品失败：" + e.Message);
            }
        }

        public static void GiveFlameItems()
        {
            try
            {
                Give(ModContent.ItemType<Content.FlameItem>());
                Give(ModContent.ItemType<Content.NoFlameItem>());
                Status("火焰/无火焰物品已发：手持各观察（P2-2）。");
            }
            catch (Exception e)
            {
                Status("发火焰物品失败：" + e.Message);
            }
        }

        public static void GiveSummonItem()
        {
            try
            {
                Give(ModContent.ItemType<Content.SummonItem>());
                Status("召唤杖已发：手持挥动召仆从（P1-3），然后开关快速堆叠/杂项过滤器。");
            }
            catch (Exception e)
            {
                Status("发召唤杖失败：" + e.Message);
            }
        }

        public static void GiveFishingGear()
        {
            try
            {
                Give(ItemID.WoodFishingPole);
                Give(ItemID.ApprenticeBait, 30);
                Status("钓具已发：木钓竿 + 蓝饵×30。到水边甩竿，观察 ConsumeBait 日志（P2-8）。");
            }
            catch (Exception e)
            {
                Status("发钓具失败：" + e.Message);
            }
        }

        // ===================== 染料商 =====================

        /// <summary>找/造一个染料商，调 GetDyeTraderReward（触发 P2-8b 桥 + mod 池手术）。</summary>
        public static void InvokeDyeReward()
        {
            try
            {
                var p = Me;
                if (p == null || !p.active) { Status("需要先进世界。"); return; }

                NPC trader = null;
                for (var i = 0; i < Main.npc.Length; i++)
                {
                    var n = Main.npc[i];
                    if (n is { } nn && nn.active && nn.type == NPCID.DyeTrader) { trader = nn; break; }
                }
                if (trader == null)
                {
                    var src = NPC.GetSpawnSourceForNaturalSpawn();
                    var idx = NPC.NewNPC(src, (int)p.position.X + 100, (int)p.position.Y - 100, NPCID.DyeTrader);
                    trader = idx >= 0 && idx < Main.npc.Length ? Main.npc[idx] : null;
                }
                if (trader == null) { Status("染料商生成失败。"); return; }

                var action = RegressionTestMod.DyeAction;
                p.GetDyeTraderReward(trader);
                Status($"GetDyeTraderReward 已触发（动作={action}，日志看池手术与发放结果）");
            }
            catch (Exception e)
            {
                Status("染料发放失败：" + e.Message);
            }
        }
    }
}
