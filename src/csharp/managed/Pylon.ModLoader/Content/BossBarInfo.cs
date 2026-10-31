namespace Pylon.ModLoader;

/// <summary>血条信息：ModifyInfo 里看/改要瞄哪个 NPC。</summary>
public sealed class BossBarInfo
{
    /// <summary>要盯着看的 NPC 下标（初值是游戏正在跟的那个；改成别的可瞄虫子的另一节）。</summary>
    public int npcIndexToAimAt;
}
