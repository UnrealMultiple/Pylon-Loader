#nullable enable
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;

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

    public static void Reset()
    {
        Counting = false;
        CtResetEffects = CtPreUpdate = CtPostUpdate = CtUpdateEquips = CtPostUpdateEquips = 0;
        CtUpdateLifeRegen = CtUpdateBadLifeRegen = CtModifyMaxStats = CtProcessTriggers = 0;
        CtFrameEffects = CtKill = CtOnRespawn = CtUpdateDead = 0;
    }

    private static void Bump(ref int counter)
    {
        if (Counting) counter++;
    }

    public override void ResetEffects() => Bump(ref CtResetEffects);
    public override void PreUpdate() => Bump(ref CtPreUpdate);
    public override void PostUpdate() => Bump(ref CtPostUpdate);
    public override void UpdateEquips() => Bump(ref CtUpdateEquips);
    public override void PostUpdateEquips() => Bump(ref CtPostUpdateEquips);
    public override void UpdateLifeRegen() => Bump(ref CtUpdateLifeRegen);
    public override void UpdateBadLifeRegen() => Bump(ref CtUpdateBadLifeRegen);
    public override void ModifyMaxStats(out StatModifier health, out StatModifier mana)
    {
        Bump(ref CtModifyMaxStats);
        health = StatModifier.Default;
        mana = StatModifier.Default;
    }
    public override void ProcessTriggers(TriggersSet triggersSet) => Bump(ref CtProcessTriggers);
    public override void FrameEffects() => Bump(ref CtFrameEffects);
    public override void UpdateDead() => Bump(ref CtUpdateDead);

    public override void Kill(double damage, int hitDirection, bool pvp, PlayerDeathReason damageSource)
    {
        Bump(ref CtKill);
        PT.Info($"Kill 触发：伤害={damage} pvp={pvp}");
    }

    public override void OnRespawn()
    {
        Bump(ref CtOnRespawn);
        PT.Info($"OnRespawn 触发：位置=({Player.position.X:F0},{Player.position.Y:F0})");
    }
}
