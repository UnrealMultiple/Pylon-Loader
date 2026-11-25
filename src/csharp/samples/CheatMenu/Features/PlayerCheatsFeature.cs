using System;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.Localization;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 玩家作弊。1:1 对应原版 <c>PlayerCheatsTool</c>：
/// 上帝模式 / 无限召唤 / 无限范围 / 无限魔力 / 自动攻击(+4 个子项) / 司命赐我 / 禁止被传送。
/// 行为、默认值、选项文案与原版一致。
/// </summary>
public sealed class PlayerCheatsFeature : CheatFeature
{
    private static readonly string[] Priorities =
    [
        "最近", "最高最大生命", "最低最大生命", "最高生命", "最低生命",
    ];

    private static readonly Regex BestowPattern =
        new(@"司命赐我(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private static int _framesSinceLastLifePacket;
    private static Item _probe;

    public override string Id => "player.cheats";
    public override string Title => "玩家作弊";
    public override CheatCategory Category => CheatCategory.Main;
    public override string SubPage => "主要";

    public override void Install()
    {
        On.Terraria.Player.Hurt += Hurt;
        On.Terraria.Player.KillMe += KillMe;
        On.Terraria.Player.ResetEffects += ResetEffects;
        On.Terraria.Player.Teleport += Teleport;
        On.Terraria.Player.ItemCheckWrapped += ItemCheckWrapped;
        On.Terraria.Chat.ChatHelper.DisplayMessage += DisplayMessage;
    }

    public override void Tick()
    {
        if (_framesSinceLastLifePacket > 0) _framesSinceLastLifePacket--;
    }

    // ---------------- 上帝模式 ----------------

    private static double Hurt(On.Terraria.Player.orig_Hurt orig, Player self,
        PlayerDeathReason reason, int damage, int hitDirection, bool crit, bool pvp, bool quiet,
        int cooldownCounter, bool dodgeable)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { GodMode: true } && Game.IsLocal(self))
        {
            self.statLife = self.statLifeMax2;
            if (_framesSinceLastLifePacket == 0)
            {
                _framesSinceLastLifePacket = 6;
                NetMessage.SendData(16, -1, -1, null, self.whoAmI, 0f, 0f, 0f, 0, 0, 0);
            }
            return 0.0;
        }
        return orig(self, reason, damage, hitDirection, crit, pvp, quiet, cooldownCounter, dodgeable);
    }

    private static void KillMe(On.Terraria.Player.orig_KillMe orig, Player self,
        PlayerDeathReason reason, double damage, int hitDirection, bool pvp)
    {
        if (CheatFeatureHost.Config is { GodMode: true } && Game.IsLocal(self)) return;
        orig(self, reason, damage, hitDirection, pvp);
    }

    // ---------------- 每帧属性（ResetEffects 之后） ----------------

    private static void ResetEffects(On.Terraria.Player.orig_ResetEffects orig, Player self)
    {
        orig(self);
        if (!Game.IsLocal(self)) return;

        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        if (cfg.GodMode) self.statLife = self.statLifeMax;
        if (cfg.InfiniteMinions)
        {
            var cap = int.MaxValue - 100000;
            self.maxMinions = cap;
            self.maxTurrets = cap;
        }
        if (cfg.InfiniteMana)
        {
            self.statMana = self.statManaMax2;
            self.manaCost = 0f;
        }
        if (cfg.InfiniteReach)
        {
            Player.tileRangeX = Main.screenWidth / 32 + 8;
            Player.tileRangeY = Main.screenHeight / 32 + 8;
        }
    }

    // ---------------- 禁止被传送 ----------------

    private static void Teleport(On.Terraria.Player.orig_Teleport orig, Player self,
        Vector2 newPos, int style, int extraInfo)
    {
        // 原版字段 AllowTeleport = true 时拦下传送（UI 标签「禁止被传送」）。
        if (CheatFeatureHost.Config is { AllowTeleport: true }) return;
        orig(self, newPos, style, extraInfo);
    }

    // ---------------- 自动攻击 ----------------

    private static void ItemCheckWrapped(On.Terraria.Player.orig_ItemCheckWrapped orig, Player self, int i)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { AutoAim: true } && Game.IsLocal(self) && self.itemTime <= 0 &&
            TryFindTarget(self, out var center, cfg.AttackRange * 16f,
                cfg.RequireLineOfSight, cfg.FavorBosses, cfg.TargetPriority))
        {
            var mouseX = Main.mouseX;
            var mouseY = Main.mouseY;

            // 原版注释：瞄准点 = 目标世界坐标 - 相机左上角，全程与视野缩放无关。
            var aim = center - Main.screenPosition;
            Main.mouseX = (int)aim.X;
            Main.mouseY = (int)aim.Y;

            self.controlUseItem = true;
            orig(self, i);
            Main.mouseX = mouseX;
            Main.mouseY = mouseY;
            return;
        }
        orig(self, i);
    }

    /// <summary>原版 Player::FindTarget 的 1:1 移植（含 DDA 视线检测与 (Boss, Score) 排序）。</summary>
    private static bool TryFindTarget(Player player, out Vector2 center, float maxRange,
        bool requireLineOfSight, bool favorBosses, int targetPriority)
    {
        center = default;
        var npcs = Main.npc;
        if (npcs is null) return false;

        var playerCenter = player.Center;
        var maxRangeSq = maxRange * maxRange;

        Il2CppStructArray<bool> solid = null;
        Il2CppStructArray<bool> solidTop = null;
        var maxX = 0;
        var maxY = 0;
        if (requireLineOfSight)
        {
            solid = Main.tileSolid;
            solidTop = Main.tileSolidTop;
            if (solid is null || solidTop is null) return false;
            maxX = Main.maxTilesX;
            maxY = Main.maxTilesY;
        }

        var bestScore = 0f;
        var bestDistSq = maxRangeSq;
        var bestNotBoss = true;
        var found = false;

        for (var index = 0; index < npcs.Length; index++)
        {
            var npc = npcs[index];
            if (npc is null || !npc.active || npc.friendly || npc.immortal || npc.dontTakeDamage) continue;

            var npcCenter = npc.Center;
            var delta = npcCenter - playerCenter;
            var distSq = delta.X * delta.X + delta.Y * delta.Y;
            if (distSq > maxRangeSq) continue;

            if (requireLineOfSight &&
                !LineOfSightClear(playerCenter, npcCenter, solid, solidTop, maxX, maxY)) continue;

            var isBoss = npc.boss;
            float score;
            switch (targetPriority)
            {
                case 1: score = -npc.lifeMax; break;
                case 2: score = npc.lifeMax; break;
                case 3: score = -npc.life; break;
                case 4: score = npc.life; break;
                default: score = distSq; break;
            }

            var notBoss = favorBosses && !isBoss;
            bool better;
            if (!found) better = true;
            // 原版是 (NotBoss, Score) 元组比较：false(0) 优先，即 Boss 优先。
            else if (notBoss != bestNotBoss) better = !notBoss && bestNotBoss;
            else if (score != bestScore) better = score < bestScore;
            else better = distSq < bestDistSq;

            if (!better) continue;

            center = npcCenter;
            bestScore = score;
            bestDistSq = distSq;
            bestNotBoss = notBoss;
            found = true;
        }

        return found;
    }

    private static bool LineOfSightClear(Vector2 a, Vector2 b, Il2CppStructArray<bool> solid,
        Il2CppStructArray<bool> solidTop, int maxX, int maxY)
    {
        const float tile = 16f;
        var x0 = (int)MathF.Floor(a.X / tile);
        var y0 = (int)MathF.Floor(a.Y / tile);
        var x1 = (int)MathF.Floor(b.X / tile);
        var y1 = (int)MathF.Floor(b.Y / tile);

        var dx = x1 - x0;
        if (dx < 0) dx = -dx;
        var dy = y1 - y0;
        if (dy < 0) dy = -dy;
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        var x = x0;
        var y = y0;
        while (true)
        {
            var endpoint = (x == x0 && y == y0) || (x == x1 && y == y1);
            if (!endpoint)
            {
                if (x < 0 || x >= maxX || y < 0 || y >= maxY) return false;
                var type = Game.TileType(x, y);
                if (type >= 0 && type < solid.Length &&
                    Game.TileActive(x, y) && !Game.TileInActive(x, y) &&
                    solid[type] && !solidTop[type])
                {
                    return false;
                }
            }
            if (x == x1 && y == y1) break;

            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
        return true;
    }

    // ---------------- 司命赐我 ----------------

    private static void DisplayMessage(On.Terraria.Chat.ChatHelper.orig_DisplayMessage orig,
        NetworkText text, Color color, byte messageAuthor, bool something)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { Bestow: true } && text is not null)
        {
            var message = text.ToString();
            var match = BestowPattern.Match(message ?? string.Empty);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
            {
                var players = Main.player;
                if (players is not null && messageAuthor < players.Length)
                {
                    var author = players[messageAuthor];
                    if (author is { active: true })
                    {
                        SpawnItem(author.position, id);
                    }
                }
            }
        }
        orig(text, color, messageAuthor, something);
    }

    private static void SpawnItem(Vector2 center, int type)
    {
        if (type <= 0) return;

        var stack = 1;
        try
        {
            _probe ??= new Item();
            _probe.SetDefaults(type, default);
            var max = _probe.maxStack;
            if (max > 0 && stack > max) stack = max;
        }
        catch
        {
            // 探测失败就按 1 个生成，不影响主路径。
        }

        var index = Item.NewItem(Projectile.GetNoneSource(), center, type, stack, 0,
            NewItemOwnership.None, default, null, false);
        if (Main.netMode == 1)
        {
            NetMessage.SendData(21, -1, -1, null, index, 1f, 0f, 0f, 0, 0, 0);
        }
    }

    // ---------------- UI（文案与原版一致） ----------------

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("玩家作弊", "PLAYER CHEATS");

        RowToggle("上帝模式", "##godmode", cfg.GodMode, v => cfg.GodMode = v);
        RowToggle("无限召唤", "##infminions", cfg.InfiniteMinions, v => cfg.InfiniteMinions = v);
        RowToggle("无限范围", "##infreach", cfg.InfiniteReach, v => cfg.InfiniteReach = v);
        RowToggle("无限魔力", "##infmana", cfg.InfiniteMana, v => cfg.InfiniteMana = v);
        RowToggle("自动攻击", "##autoaim", cfg.AutoAim, v => cfg.AutoAim = v);

        if (cfg.AutoAim)
        {
            var range = cfg.AttackRange;
            if (ModernUI.RowSlider("##attackrange", "攻击范围(格)", "6-125", ref range, 6f, 125f, "0"))
            {
                cfg.AttackRange = Math.Clamp(range, 6f, 125f);
                CheatFeatureHost.MarkDirty();
            }

            RowToggle("视线检测", "##los", cfg.RequireLineOfSight, v => cfg.RequireLineOfSight = v);
            RowToggle("优先Boss", "##favorboss", cfg.FavorBosses, v => cfg.FavorBosses = v);

            ModernUI.BeginRow();
            ModernUI.RowLabel("目标优先级");
            var priority = cfg.TargetPriority;
            if (ModernUI.ComboField("##priority", Priorities, ref priority))
            {
                cfg.TargetPriority = Math.Clamp(priority, 0, Priorities.Length - 1);
                CheatFeatureHost.MarkDirty();
            }
            ModernUI.EndRow();
        }

        RowToggle("司命赐我", "##bestow", cfg.Bestow, v => cfg.Bestow = v);
        RowToggle("禁止被传送", "##allowtp", cfg.AllowTeleport, v => cfg.AllowTeleport = v);

        ModernUI.EndCard();
    }

    private static void RowToggle(string label, string id, bool value, Action<bool> set)
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