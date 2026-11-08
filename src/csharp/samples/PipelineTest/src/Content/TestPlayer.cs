using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>
///     进世界后把测试物品塞到热键栏第 1 格：没有这个物品就没法用肉眼验证物品介绍/介绍行。
///     只在 Player.PostUpdate 里补一次（拿到就是拿到了，之后不再动背包）。
/// </summary>
public sealed class TestPlayer : ModPlayer
{
    private bool _given;
    private bool _worldRan;
    private int _npcProbeFrames = -1;
    private int _npcRespawnTimer;
    private bool _npcRound;
    private int _buffTimer;
    private bool _buffRound;
    private int _lootTimer;
    private bool _lootRound;
    private int _shopTimer;
    private bool _shopRound;

    /// <summary>shop 组：附近没有测试商人就补一只（5 秒检查一次）。</summary>
    private void EnsureMerchant(Player player)
    {
        var type = ModContent.NpcType<TestTownNpc>();
        var all = Main.npc;
        if (all is null) return;

        for (var i = 0; i < all.Length; i++)
        {
            var npc = all[i];
            if (npc is null || !npc.active || npc.type != type) continue;
            if (System.Math.Abs(npc.Center.X - player.Center.X) < 900f) return;
        }

        var idx = NPC.NewNPC(player.GetProjectileSource_Misc(0),
            (int)player.Center.X + 40, (int)player.Center.Y - 20, type, 0, 0f, 0f, 0f, 0f, 0);
        if (idx >= 0)
        {
            Main.npc[idx].GivenName = "小明";
            PT.Info($"已补刷测试商人（index={idx}）");
        }
    }

    /// <summary>loot 组：附近没有测试史莱姆就补两只 1 血的，方便反复验证掉落。</summary>
    private void EnsureLootNpc(Player player)
    {
        var type = ModContent.NpcType<TestNpc>();
        var all = Main.npc;
        if (all is null) return;

        for (var i = 0; i < all.Length; i++)
        {
            var npc = all[i];
            if (npc is null || !npc.active || npc.type != type) continue;
            if (System.Math.Abs(npc.Center.X - player.Center.X) < 800f) return;
        }

        var source = player.GetProjectileSource_Misc(0);
        for (var side = -1; side <= 1; side += 2)
        {
            var idx = NPC.NewNPC(source, (int)player.Center.X + side * 80, (int)player.Center.Y - 40,
                type, 0, 0f, 0f, 0f, 0f, 0);
            if (idx < 0) continue;
            Main.npc[idx].life = 1;
            Main.npc[idx].lifeMax = 1;
        }

        PT.Info("loot 组：附近没有测试史莱姆，已补两只 1 血的");
    }

    /// <summary>每 8 秒补一次测试增益（并打一条状态，方便核对图标表与槽位）。</summary>
    private void KeepTestBuff(Player player)
    {
        var type = ModContent.BuffType<TestBuff>();
        player.AddBuff(type, 600, true);
        // 对照：同时挂一个原版增益，用来区分"buff 条不显示"与"只有 mod 图标不显示"。
        player.AddBuff(Terraria.ID.BuffID.Regeneration, 600, true);

        var slot = player.FindBuffIndex(type);
        var text = slot >= 0 ? $"槽位={slot} 剩余={player.buffTime[slot]} alpha={Main.buffAlpha[type]}" : "没有这个 buff";

        var assets = Terraria.GameContent.TextureAssets.Buff;
        var tex = assets is not null && type < assets.Length ? assets[type]?.Value : null;
        var size = tex is null ? "null" : $"{tex.Width}x{tex.Height}";

        PT.Info($"测试增益状态：type={type} {text} 图标={size} " +
                $"UpdatePlayer 累计={TestBuff.UpdateCount} Apply={TestBuff.ApplyCount} 生命={player.statLife}/{player.statLifeMax2}");
    }

    /// <summary>附近 800 像素内没有测试史莱姆时，在玩家两侧各补一只（带音效、会跳）。</summary>
    private void EnsureTestNpc(Player player)
    {
        var type = ModContent.NpcType<TestNpc>();
        var all = Main.npc;
        if (all is null) return;

        for (var i = 0; i < all.Length; i++)
        {
            var npc = all[i];
            if (npc is null || !npc.active || npc.type != type) continue;
            if (System.Math.Abs(npc.Center.X - player.Center.X) < 800f) return;
        }

        var source = player.GetProjectileSource_Misc(0);
        for (var side = -1; side <= 1; side += 2)
        {
            var idx = NPC.NewNPC(source, (int)player.Center.X + side * 100, (int)player.Center.Y - 40,
                type, 0, 0f, 0f, 0f, 0f, 0);
            if (idx < 0) continue;
            Main.npc[idx].timeLeft = 3600;
        }

        PT.Info("附近没有测试史莱姆了，已在玩家两侧各补一只");
    }

    /// <summary>进世界约 2 秒后，把最近的测试史莱姆状态打一条（验证 frame 是否已由管线补上）。</summary>
    private void ProbeTestNpc(Player player)
    {
        if (_npcProbeFrames < 0) _npcProbeFrames = 120;
        if (_npcProbeFrames == 0) return;
        if (--_npcProbeFrames > 0) return;

        var type = ModContent.NpcType<TestNpc>();
        var all = Main.npc;
        if (all is null) return;

        var seen = 0;
        for (var i = 0; i < all.Length; i++)
        {
            var npc = all[i];
            if (npc is null || !npc.active || npc.type != type) continue;

            var distance = System.Math.Abs(npc.Center.X - player.Center.X) + System.Math.Abs(npc.Center.Y - player.Center.Y);
            var playerHit = player.Hitbox;
            var npcHit = npc.Hitbox;
            var overlap = playerHit.Intersects(npcHit);

            PT.Info($"测试史莱姆#{i}(2s)：dist={distance:F0} 相交={overlap} damage={npc.damage} friendly={npc.friendly} " +
                    $"life={npc.life}/{npc.lifeMax} hitSound={(npc.HitSound is null ? "null" : "set")} " +
                    $"player={player.statLife}/{player.statLifeMax2}（受击前 {PipelineTest.Tests.NpcContactTest.BeforeLife}）immune={player.immune}/{player.immuneTime}");
            if (++seen >= 4) return;
        }

        if (seen == 0) PT.Info($"测试史莱姆(2s)：一只都不在了；player={player.statLife}/{player.statLifeMax2}");
    }

    public override void PostUpdate(Player player)
    {
        if (player is null || !player.active) return;

        if (_given)
        {
            // 物品已经在背包里了：下一帧再跑 World 阶段（前缀等用例要改热键栏第 1 格）。
            if (!_worldRan)
            {
                _worldRan = true;
                PT.RunPhase(Phase.World);
            }

            PT.RunTick();
            ProbeTestNpc(player);

            // npc 组：每 15 秒检查一次，附近没有测试史莱姆就补两只，方便反复验证碰撞伤害/音效。
            if (!_npcRound)
            {
                _npcRound = PT.Enabled is { } enabled &&
                            (enabled.Contains("npc") || enabled.Contains("npc-w"));
            }

            if (_npcRound && ++_npcRespawnTimer >= 900)
            {
                _npcRespawnTimer = 0;
                EnsureTestNpc(player);
            }

            if (!_buffRound)
            {
                _buffRound = PT.Enabled is { } enabled &&
                             (enabled.Contains("buff") || enabled.Contains("buff-w"));
            }

            if (_buffRound && ++_buffTimer % 480 == 0) KeepTestBuff(player);

            if (!_lootRound)
            {
                _lootRound = PT.Enabled is { } enabled &&
                             (enabled.Contains("loot") || enabled.Contains("loot-w"));
            }

            if (_lootRound && ++_lootTimer % 600 == 0) EnsureLootNpc(player);

            if (!_shopRound)
            {
                _shopRound = PT.Enabled is { } enabled &&
                             (enabled.Contains("shop") || enabled.Contains("shop-w"));
            }

            if (_shopRound && ++_shopTimer % 300 == 0) EnsureMerchant(player);

            return;
        }

        var type = ModContent.TypeOf<TestItem>();
        if (type <= 0) return;

        var inv = player.inventory;
        if (inv is null || inv.Length == 0) return;

        for (var i = 0; i < inv.Length; i++)
        {
            var slot = inv[i];
            if (slot is not null && slot.type == type)
            {
                _given = true;
                return;
            }
        }

        var item = new Item();
        item.SetDefaults(type, null);
        item.stack = 1;

        inv[0] = item;
        _given = true;

        PT.Info($"已把测试物品放进热键栏第 1 格（type={type} 玩家槽位数={inv.Length}）");
    }
}