using PipelineTest.Content;
using Terraria;

namespace PipelineTest.Tests;

internal static class PlayerUpdateTests
{
    public static void Register() => PT.Group("player-update-w", Phase.World, Run);

    private static void Run()
    {
        var player = Main.LocalPlayer;
        var savedLuck = player.luck;
        PlayerUpdateProbe.Reset();
        PlayerUpdateProbe.Active = true;
        try
        {
            PT.Case("PLAYER-U1", c =>
            {
                PlayerUpdateProbe.Veto = true;
                player.RecalculateLuck();
                c.Eq(PlayerUpdateProbe.PreLuck, 1, "幸运前置经游戏入口调用一次");
                c.Eq(PlayerUpdateProbe.PostLuck, 1, "否决原版后仍调用幸运后置");
                c.Eq(player.luck, 0.75f, "否决保留前置值并应用后置修改");
                PlayerUpdateProbe.Veto = false;
            });
            PT.Case("PLAYER-U2", c =>
            {
                player.CapAttackSpeeds();
                c.Eq(PlayerUpdateProbe.Misc, 1, "攻击速度计算后实际派发");
            });
        }
        finally
        {
            player.luck = savedLuck;
            PlayerUpdateProbe.Active = false;
            PlayerUpdateProbe.Reset();
        }
    }
}
