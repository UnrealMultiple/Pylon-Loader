#nullable enable
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;

namespace PipelineTest.Content;

/// <summary>
/// 玩家生命期探针：计文档点名的每个钩子。Kill/OnRespawn 由世界组的死亡/复活场景触发。
/// 计数字段用 Ct 前缀（与钩子方法名区分开）。开关默认关（世界组开场打开），避免污染其他组。
/// </summary>
public sealed class TestPlayerLifecycle : ModPlayer
{
    public static bool Counting;

    public static int CtResetEffects;
    public static int CtPreUpdate;
    public static int CtPostUpdate;
    public static int CtUpdateEquips;
    public static int CtPostUpdateEquips;
    public static int CtUpdateLifeRegen;
    public static int CtUpdateBadLifeRegen;
    public static int CtModifyMaxStats;
    public static int CtProcessTriggers;
    public static int CtFrameEffects;
    public static int CtKill;
    public static int CtOnRespawn;
    public static int CtUpdateDead;
    public static int CtUpdateManaRegen;

    public static void Reset()
    {
        Counting = false;
        CtResetEffects = CtPreUpdate = CtPostUpdate = CtUpdateEquips = CtPostUpdateEquips = 0;
        CtUpdateLifeRegen = CtUpdateBadLifeRegen = CtModifyMaxStats = CtProcessTriggers = 0;
        CtFrameEffects = CtKill = CtOnRespawn = CtUpdateDead = CtUpdateManaRegen = 0;
    }

    private static void Bump(ref int counter)
    {
        if (Counting) counter++;
    }

    public override void ResetEffects(Player player) => Bump(ref CtResetEffects);
    public override void PreUpdate(Player player) => Bump(ref CtPreUpdate);
    public override void PostUpdate(Player player) => Bump(ref CtPostUpdate);
    public override void UpdateEquips(Player player) => Bump(ref CtUpdateEquips);
    public override void PostUpdateEquips(Player player) => Bump(ref CtPostUpdateEquips);
    public override void UpdateLifeRegen(Player player) => Bump(ref CtUpdateLifeRegen);
    public override void UpdateBadLifeRegen(Player player) => Bump(ref CtUpdateBadLifeRegen);
    public override void UpdateManaRegen(Player player) => Bump(ref CtUpdateManaRegen);
    public override void ModifyMaxStats(Player player) => Bump(ref CtModifyMaxStats);
    public override void ProcessTriggers(Player player, TriggersSet triggersSet) => Bump(ref CtProcessTriggers);
    public override void FrameEffects(Player player) => Bump(ref CtFrameEffects);
    public override void UpdateDead(Player player) => Bump(ref CtUpdateDead);

    public override void Kill(Player player, PlayerDeathReason damageSource, double damage, int hitDirection, bool pvp)
    {
        Bump(ref CtKill);
        PT.Info($"Kill 触发：伤害={damage} pvp={pvp}");
    }

    public override void OnRespawn(Player player)
    {
        Bump(ref CtOnRespawn);
        PT.Info($"OnRespawn 触发：位置=({player.position.X:F0},{player.position.Y:F0})");
    }
}
