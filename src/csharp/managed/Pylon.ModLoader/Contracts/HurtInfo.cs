namespace Pylon.ModLoader;

/// <summary>玩家挨的这一下（OnHurt、OnHitByNPC、OnHitPlayer、OnHitPvp... 收到）；Damage 是游戏 Hurt 实际扣的血。</summary>
public class HurtInfo
{
    /// <summary>从一个 values 字典建出来（没给的键按参考的默认值）。</summary>
    public HurtInfo(Dictionary<string, object> values = null!)
    {
        DamageSource = Value(values, "DamageSource");
        PvP = Truthy(Value(values, "PvP"));
        CooldownCounter = Int(Value(values, "CooldownCounter"), -1);
        Dodgeable = Value(values, "Dodgeable") is not false;
        SourceDamage = Math.Max(Int(Value(values, "SourceDamage"), 0), 1);
        Damage = Math.Max(Int(Value(values, "Damage"), 0), 1);
        HitDirection = Int(Value(values, "HitDirection"), 0);
        Knockback = Float(Value(values, "Knockback"));
        Cancelled = Truthy(Value(values, "Cancelled"));
        DustDisabled = Truthy(Value(values, "DustDisabled"));
        SoundDisabled = Truthy(Value(values, "SoundDisabled"));
        Quiet = Truthy(Value(values, "Quiet"));
        Crit = Truthy(Value(values, "Crit"));
    }

    /// <summary>伤害来源（没给就是 null）；本游戏无 PlayerDamageSource，先用 object。</summary>
    public object DamageSource { get; set; }

    /// <summary>是不是玩家对玩家（PvP 伤害）。</summary>
    public bool PvP { get; set; }

    /// <summary>伤害冷却计数器（默认 -1 = 不参与冷却）。</summary>
    public int CooldownCounter { get; set; }

    /// <summary>这次伤害能不能被闪避（默认 true，只有显式 false 才关）。</summary>
    public bool Dodgeable { get; set; }

    /// <summary>过滤前的原始伤害（至少 1）。</summary>
    public int SourceDamage { get; set; }

    /// <summary>实际扣的血（游戏 Hurt 的最终结果，至少 1）。</summary>
    public int Damage { get; set; }

    /// <summary>受击方向（-1 左 / 1 右，默认 0）。</summary>
    public int HitDirection { get; set; }

    /// <summary>击退力度（默认 0）。</summary>
    public float Knockback { get; set; }

    /// <summary>这次伤害算作取消（默认 false）。</summary>
    public bool Cancelled { get; set; }

    /// <summary>不播受伤的尘特效。</summary>
    public bool DustDisabled { get; set; }

    /// <summary>不播受伤的音效。</summary>
    public bool SoundDisabled { get; set; }

    /// <summary>安静模式（不弹伤害文字之类）。</summary>
    public bool Quiet { get; set; }

    /// <summary>这次是不是暴击。</summary>
    public bool Crit { get; set; }

    /// <summary>取出键的值（没键或 null 就返回 null）。</summary>
    private static object Value(Dictionary<string, object> values, string key) =>
        values != null && values.TryGetValue(key, out var value) ? value : null!;

    /// <summary>JS 的 !!：真值判断（bool 用本身，其它非 null 都算 true）。</summary>
    private static bool Truthy(object value) => value is bool b ? b : value != null;

    /// <summary>JS 的 |0：整数化，不能转就返回 fallback。</summary>
    private static int Int(object value, int fallback) => value is int i ? i : fallback;

    /// <summary>JS 的 Number()||0：数值化，不能转就 0。</summary>
    private static float Float(object value) => value switch
    {
        float f => f,
        int i => i,
        double d => (float)d,
        _ => 0f
    };
}
