using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 游戏成就通知的静态事件面：原版通知处理完后触发，作者订阅后驱动自定义条件或计数。
/// OnTileDestroyed 只在进世界且本帧真的在挖掘时触发；OnProgressionEvent 只在非服务器端触发。
/// </summary>
public static class AchievementsHelper
{
    static Action<Player, short, int>? itemPickup;
    static Action<short, int>? itemCraft;
    static Action<Player, ushort>? tileDestroyed;
    static Action<Player, short>? npcKilled;
    static Action<int>? progressionEvent;

    public static event Action<Player, short, int>? OnItemPickup
    {
        add => itemPickup += value;
        remove => itemPickup -= value;
    }

    public static event Action<short, int>? OnItemCraft
    {
        add => itemCraft += value;
        remove => itemCraft -= value;
    }

    public static event Action<Player, ushort>? OnTileDestroyed
    {
        add => tileDestroyed += value;
        remove => tileDestroyed -= value;
    }

    public static event Action<Player, short>? OnNPCKilled
    {
        add => npcKilled += value;
        remove => npcKilled -= value;
    }

    public static event Action<int>? OnProgressionEvent
    {
        add => progressionEvent += value;
        remove => progressionEvent -= value;
    }

    internal static void FireItemPickup(Player player, short itemType, int stack) =>
        Fire("OnItemPickup", () => itemPickup?.Invoke(player, itemType, stack));

    internal static void FireItemCraft(short itemType, int stack) =>
        Fire("OnItemCraft", () => itemCraft?.Invoke(itemType, stack));

    internal static void FireTileDestroyed(Player player, ushort tileType) =>
        Fire("OnTileDestroyed", () => tileDestroyed?.Invoke(player, tileType));

    internal static void FireNPCKilled(Player player, short npcId) =>
        Fire("OnNPCKilled", () => npcKilled?.Invoke(player, npcId));

    internal static void FireProgressionEvent(int eventId) =>
        Fire("OnProgressionEvent", () => progressionEvent?.Invoke(eventId));

    static void Fire(string name, Action action)
    {
        try { action(); }
        catch (Exception e) { ContentErrors.Report($"[achievement] AchievementsHelper.{name} 异常：{Safe.Describe(e)}"); }
    }
}
