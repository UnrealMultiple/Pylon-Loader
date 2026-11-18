using Il2CppMicrosoft.Xna.Framework;
using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace PipelineTest.Tests;

internal static class TownAttackTests
{
    private static int _town = -1;
    private static int _enemy = -1;
    private static int _attempts;

    public static void Register() => PT.Group("townattack-w", Phase.World, Run);

    private static void Run()
    {
        var player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            PT.SkipCase("TOWNATTACK-W1", "玩家还没就绪");
            return;
        }

        PT.Case("TOWNATTACK-W1", c =>
        {
            _town = FindTown();
            if (_town < 0)
            {
                _town = NPC.NewNPC(player.GetProjectileSource_Misc(0),
                    (int)player.Center.X - 80, (int)player.Center.Y - 20,
                    ModContent.NPCType<TestTownNpc>(), 0, 0f, 0f, 0f, 0f, 0);
            }

            if (_town < 0 || _town >= Main.npc.Length || Main.npc[_town] is not { active: true } town)
            {
                c.Fail($"测试商人不可用 index={_town}");
                return;
            }

            town.noGravity = true;
            town.velocity = Vector2.Zero;
            town.position = new Vector2(player.Center.X - 80 - town.width / 2f, player.Center.Y - town.height);

            _enemy = NPC.NewNPC(player.GetProjectileSource_Misc(0),
                (int)player.Center.X + 80, (int)player.Center.Y - 20,
                NPCID.BlueSlime, 0, 0f, 0f, 0f, 0f, 0);
            if (_enemy < 0 || _enemy >= Main.npc.Length || Main.npc[_enemy] is not { active: true } enemy)
            {
                c.Fail($"镇攻目标不可用 index={_enemy}");
                return;
            }

            enemy.aiStyle = 0;
            enemy.noGravity = true;
            enemy.velocity = Vector2.Zero;
            enemy.life = enemy.lifeMax = 9999;
            enemy.damage = 1;
            enemy.friendly = false;
            enemy.position = new Vector2(player.Center.X + 80 - enemy.width / 2f, player.Center.Y - enemy.height);

            TestTownNpc.StrengthCalls = TestTownNpc.CooldownCalls = 0;
            TestTownNpc.ProjCalls = TestTownNpc.SpeedCalls = 0;
            _attempts = 0;
            PT.Info($"镇攻探针：town={_town} enemy={_enemy}");
            c.Pending();            PT.RetryLater("TOWNATTACK-W2", 60, Wait);
        });
    }

    private static void Wait(Check c)
    {
        if (TestTownNpc.StrengthCalls > 0 || TestTownNpc.ProjCalls > 0)
        {
            c.True(true,
                $"镇攻桥命中 strength={TestTownNpc.StrengthCalls} cooldown={TestTownNpc.CooldownCalls} " +
                $"proj={TestTownNpc.ProjCalls} speed={TestTownNpc.SpeedCalls}");
            Cleanup();
            return;
        }

        _attempts++;
        if (_attempts < 15)
        {
            c.Pending();            PT.RetryLater("TOWNATTACK-W2", 60, Wait);
            return;
        }

        var town = _town >= 0 && _town < Main.npc.Length ? Main.npc[_town] : null;
        c.Fail($"60 帧×{_attempts} 未命中镇攻桥：ai={town?.ai[0]}/{town?.ai[1]} target={town?.target} " +
               $"enemyActive={(_enemy >= 0 && _enemy < Main.npc.Length && Main.npc[_enemy].active)}");
        Cleanup();
    }

    private static void Cleanup()
    {
        if (_enemy >= 0 && _enemy < Main.npc.Length && Main.npc[_enemy] is { } enemy) enemy.active = false;
        _enemy = -1;
    }

    private static int FindTown()
    {
        var type = ModContent.NPCType<TestTownNpc>();
        for (var i = 0; i < Main.npc.Length; i++)
        {
            if (Main.npc[i] is { active: true } npc && npc.type == type) return i;
        }

        return -1;
    }
}

