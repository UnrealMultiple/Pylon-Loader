using System;
using System.Collections.Generic;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.DataStructures;
using Il2CppMicrosoft.Xna.Framework;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 自动钓鱼。1:1 对应原版 <c>FishTool</c>：
/// FishingCheck_RollItemDrop 判定咬钩物品类别后把该鱼漂放进回收队列，
/// Main.Update 每帧驱动回收与补线；多鱼线通过临时隐藏其它 bobber 让原生收杆逻辑只看见目标。
/// </summary>
public sealed class FishFeature : CheatFeature
{
    private const int RecallDelayFrames = 2;
    private const int PlayerControlsMessage = 13;

    private readonly List<RecallJob> _recallJobs = new();

    private struct RecallJob
    {
        public Projectile Projectile;
        public IntPtr Pointer;
        public int Delay;
    }

    private int _activeLineCount;
    private int _castCooldown;
    private Vector2 _specialPosition;

    public override string Id => "player.fish";
    public override string Title => "自动钓鱼";
    public override CheatCategory Category => CheatCategory.Main;
    public override string SubPage => "自动钓鱼";

    public override void Install()
    {
        Instance = this;
        On.Terraria.Projectile.FishingCheck_RollItemDrop += RollItemDrop;
        On.Terraria.Main.Update += MainUpdate;
    }

    private static bool Enabled => CheatFeatureHost.Config is { AutoFish: true };

    // ---------------- 咬钩判定 ----------------

    private static void RollItemDrop(On.Terraria.Projectile.orig_FishingCheck_RollItemDrop orig,
        Projectile self, ref FishingAttempt fisher)
    {
        orig(self, ref fisher);

        var cfg = CheatFeatureHost.Config;
        var player = Game.LocalPlayer;
        if (cfg is null || player is null || !cfg.AutoFish || self is null) return;
        if (self.owner != player.whoAmI) return;

        var wantToCatch = false;

        if (fisher.rolledItemDrop > 0 && cfg.AutoFishItem)
        {
            if (!fisher.crate && fisher.questFish == -1 && !fisher.common && !fisher.uncommon &&
                !fisher.rare && !fisher.veryrare && !fisher.legendary && cfg.AutoFishNormal)
            {
                wantToCatch = true;
            }
        }
        if (cfg.AutoFishQuestFish && fisher.questFish != -1) wantToCatch = true;
        if (cfg.AutoFishCrates && fisher.crate) wantToCatch = true;
        if (cfg.AutoFishCommon && fisher.common) wantToCatch = true;
        if (cfg.AutoFishUncommon && fisher.uncommon) wantToCatch = true;
        if (cfg.AutoFishRare && fisher.rare) wantToCatch = true;
        if (cfg.AutoFishVeryRare && fisher.veryrare) wantToCatch = true;
        if (cfg.AutoFishLegendary && fisher.legendary) wantToCatch = true;

        if (fisher.rolledEnemySpawn > 0 && cfg.AutoFishNpc) wantToCatch = true;

        if (!wantToCatch) return;

        var feature = Instance;
        if (feature is null || feature.HasPendingRecall(self.Pointer)) return;
        feature._recallJobs.Add(new RecallJob
        {
            Projectile = self,
            Pointer = self.Pointer,
            Delay = RecallDelayFrames,
        });
    }

    // ---------------- 每帧驱动 ----------------

    private void MainUpdate(On.Terraria.Main.orig_Update orig, Main self, GameTime gameTime)
    {
        AutoFishCheck();
        orig(self, gameTime);
    }

    private void AutoFishCheck()
    {
        if (!Enabled)
        {
            _activeLineCount = 0;
            if (_recallJobs.Count > 0) _recallJobs.Clear();
            return;
        }

        var player = Game.LocalPlayer;
        if (player is null) return;

        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;
        if (cfg.HasSpecialPosition) _specialPosition = Main.MouseWorld;

        // 1) 回收队列：FIFO，每帧最多回收一根。
        while (_recallJobs.Count > 0)
        {
            var job = _recallJobs[0];
            job.Delay--;
            if (job.Delay > 0)
            {
                _recallJobs[0] = job;
                break;
            }
            RecallFishingBobber(player, job.Projectile);
            _recallJobs.RemoveAt(0);
        }

        // 2) 数量不足则补线。
        var active = CountActiveBobbers(player);
        _activeLineCount = active;

        if (_castCooldown > 0) _castCooldown--;
        if (active < TargetLineCount() && _castCooldown <= 0)
        {
            _castCooldown = CastFishingLine(player, cfg.HasSpecialPosition) ? 20 : 5;
        }
    }

    private int TargetLineCount()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null || !cfg.MultiLine) return 1;
        return Math.Clamp(cfg.MultiLineCount, 1, 10);
    }

    private bool HasPendingRecall(IntPtr pointer)
    {
        for (var i = 0; i < _recallJobs.Count; i++)
        {
            if (_recallJobs[i].Pointer == pointer) return true;
        }
        return false;
    }

    private static int CountActiveBobbers(Player player)
    {
        var count = 0;
        var projectiles = Main.projectile;
        if (projectiles is null) return 0;
        var owner = player.whoAmI;
        for (var i = 0; i < projectiles.Length; i++)
        {
            var p = projectiles[i];
            if (p is null || !p.active || p.owner != owner || !p.bobber) continue;
            count++;
        }
        return count;
    }

    /// <summary>隐藏除 keep 之外本玩家所有鱼漂的 bobber 标志，返回被隐藏的列表。</summary>
    private static List<Projectile> HideOtherBobbers(Player player, IntPtr keep)
    {
        var hidden = new List<Projectile>();
        var projectiles = Main.projectile;
        if (projectiles is null) return hidden;
        var owner = player.whoAmI;
        for (var i = 0; i < projectiles.Length; i++)
        {
            var p = projectiles[i];
            if (p is null || !p.active || p.owner != owner || !p.bobber) continue;
            if (p.Pointer == keep) continue;
            p.bobber = false;
            hidden.Add(p);
        }
        return hidden;
    }

    private static void RestoreBobbers(List<Projectile> hidden)
    {
        for (var i = 0; i < hidden.Count; i++)
        {
            var p = hidden[i];
            if (p is not null) p.bobber = true;
        }
    }

    /// <summary>单独回收一根中鱼的鱼漂。</summary>
    private static void RecallFishingBobber(Player player, Projectile target)
    {
        if (target is null || !target.active || !target.bobber) return;
        if (target.owner != player.whoAmI) return;

        var held = player.HeldItem;
        if (held is null || held.fishingPole <= 0) return;

        var hadFishingLine = player.accFishingLine;
        player.accFishingLine = true;

        var hidden = HideOtherBobbers(player, target.Pointer);
        player.ItemCheck_PullFishingBobbers(held);
        RestoreBobbers(hidden);

        player.accFishingLine = hadFishingLine;
    }

    /// <summary>抛出一根新鱼线：临时隐藏全部鱼漂后走原生 ItemCheck。</summary>
    private static bool CastFishingLine(Player player, bool hasSpecialPosition)
    {
        var held = player.HeldItem;
        if (held is null || held.fishingPole <= 0) return false;

        var baitPower = 0;
        var baitType = 0;
        player.Fishing_GetBait(out baitPower, out baitType);
        if (baitPower <= 0 || baitType <= 0) return false;

        var before = CountActiveBobbers(player);
        var hidden = HideOtherBobbers(player, IntPtr.Zero);

        player.controlUseItem = true;
        var mouseX = Main.mouseX;
        var mouseY = Main.mouseY;
        if (hasSpecialPosition)
        {
            var feature = Instance;
            var position = feature?._specialPosition ?? default;
            var aim = position - Main.screenPosition;
            Main.mouseX = (int)aim.X;
            Main.mouseY = (int)aim.Y;
        }

        player.ItemCheck();
        NetMessage.SendData(PlayerControlsMessage, -1, -1, null, Main.myPlayer, 0f, 0f, 0f, 0, 0, 0);

        Main.mouseX = mouseX;
        Main.mouseY = mouseY;

        RestoreBobbers(hidden);
        return CountActiveBobbers(player) > before;
    }

    private static FishFeature Instance { get; set; }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("自动钓鱼", "AUTO FISH");

        RowToggle("启用钓鱼", "##autofish", cfg.AutoFish, v => cfg.AutoFish = v);

        RowToggle("多鱼线", "##multiline", cfg.MultiLine, v => cfg.MultiLine = v);
        if (cfg.MultiLine)
        {
            var text = cfg.MultiLineCount.ToString();
            if (ModernUI.TextInput("多鱼线数量(根) 1-10", ref text))
            {
                if (int.TryParse(text, out var count))
                {
                    cfg.MultiLineCount = Math.Clamp(count, 1, 10);
                    CheatFeatureHost.MarkDirty();
                }
            }
        }
        ModernUI.DimText($"当前鱼线: {_activeLineCount} / {TargetLineCount()}");

        RowToggle("接受物品", "##fishitem", cfg.AutoFishItem, v => cfg.AutoFishItem = v);
        RowToggle("接受npc", "##fishnpc", cfg.AutoFishNpc, v => cfg.AutoFishNpc = v);
        RowToggle("接受宝匣", "##fishcrate", cfg.AutoFishCrates, v => cfg.AutoFishCrates = v);
        RowToggle("接受普通", "##fishnormal", cfg.AutoFishNormal, v => cfg.AutoFishNormal = v);
        RowToggle("接受常见", "##fishcommon", cfg.AutoFishCommon, v => cfg.AutoFishCommon = v);
        RowToggle("接受罕见", "##fishuncommon", cfg.AutoFishUncommon, v => cfg.AutoFishUncommon = v);
        RowToggle("接受稀有", "##fishrare", cfg.AutoFishRare, v => cfg.AutoFishRare = v);
        RowToggle("接受神话", "##fishveryrare", cfg.AutoFishVeryRare, v => cfg.AutoFishVeryRare = v);
        RowToggle("接受传奇", "##fishlegendary", cfg.AutoFishLegendary, v => cfg.AutoFishLegendary = v);
        RowToggle("接受任务鱼", "##fishquest", cfg.AutoFishQuestFish, v => cfg.AutoFishQuestFish = v);

        RowToggle("使用当前光标位置", "##fishspecial", cfg.HasSpecialPosition, v => cfg.HasSpecialPosition = v);

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