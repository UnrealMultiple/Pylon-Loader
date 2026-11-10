using Terraria;

namespace Pylon.ModLoader;

/// <summary>任务鱼按类型使用注册模板;文案和每日可用性不能依赖某个背包物品实例。</summary>
internal static class ItemAnglerQuest
{
    private const int MaxSwaps = 1000;
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Main.AnglerQuestSwap += Swap;
        On.Terraria.Lang.AnglerQuestChat += Chat;
    }

    /// <summary>静态默认值之后补入任务鱼;保留现有顺序与重复内容,只追加尚未在池中的类型。</summary>
    internal static void Build()
    {
        var native = Main.anglerQuestItemNetIDs;
        if (native is null) return;

        var known = new HashSet<int>();
        for (var i = 0; i < native.Length; i++) known.Add(native[i]);
        var added = new List<int>();
        foreach (var item in ModContent.GetContent<ModItem>())
        {
            if (!known.Add(item.Type)) continue;
            try
            {
                if (item.IsQuestFish()) added.Add(item.Type);
            }
            catch (Exception e)
            {
                Report(item, nameof(ModItem.IsQuestFish), e);
            }
        }

        if (added.Count == 0) return;
        var grown = new int[native.Length + added.Count];
        for (var i = 0; i < native.Length; i++) grown[i] = native[i];
        for (var i = 0; i < added.Count; i++) grown[native.Length + i] = added[i];
        // 先填满托管缓冲再转换，避免在跨运行时数组上逐格写入。
        Main.anglerQuestItemNetIDs = grown;
    }

    private static bool Unavailable(int type)
    {
        var unavailable = false;
        if (ModContent.Find(ContentKind.Item, type) is ModItem item)
        {
            try
            {
                unavailable |= !item.IsAnglerQuestAvailable();
            }
            catch (Exception e)
            {
                Report(item, nameof(ModItem.IsAnglerQuestAvailable), e);
            }
        }

        // 每个模板都收到查询;一位作者的否决或异常不跳过后续作者。
        foreach (var global in GlobalItemRegistry.All)
        {
            try
            {
                unavailable |= !global.IsAnglerQuestAvailable(type);
            }
            catch (Exception e)
            {
                Report(global, nameof(GlobalItem.IsAnglerQuestAvailable), e);
            }
        }
        return unavailable;
    }

    private static void Swap(On.Terraria.Main.orig_AnglerQuestSwap orig)
    {
        // 世界初始化可能重建任务鱼数组;抽签之前幂等补回,不把非任务鱼加入原版抽签。
        Build();
        orig();
        for (var tries = 0; tries < MaxSwaps; tries++)
        {
            var ids = Main.anglerQuestItemNetIDs;
            if (Main.anglerQuestFinished || ids is null || Main.anglerQuest < 0 ||
                Main.anglerQuest >= ids.Length || !Unavailable(ids[Main.anglerQuest])) return;
            orig();
        }

        // 所有候选被否决也必须终止,避免错误可用性逻辑卡死游戏线程。
        Main.anglerQuest = 0;
        LoaderHost.Current.Log.Warn("[angler] 任务鱼抽签超过重试上限,请检查 IsAnglerQuestAvailable");
    }

    private static string Chat(On.Terraria.Lang.orig_AnglerQuestChat orig, bool turnIn)
    {
        var ids = Main.anglerQuestItemNetIDs;
        if (turnIn || Main.anglerQuestFinished || ids is null || Main.anglerQuest < 0 ||
            Main.anglerQuest >= ids.Length) return orig(turnIn);

        var type = ids[Main.anglerQuest];
        var description = "";
        var location = "";
        if (ModContent.Find(ContentKind.Item, type) is ModItem item)
        {
            try
            {
                item.AnglerQuestChat(ref description, ref location);
            }
            catch (Exception e)
            {
                Report(item, nameof(ModItem.AnglerQuestChat), e);
            }
        }

        foreach (var global in GlobalItemRegistry.All)
        {
            try
            {
                global.AnglerChat(type, ref description, ref location);
            }
            catch (Exception e)
            {
                Report(global, nameof(GlobalItem.AnglerChat), e);
            }
        }

        if (string.IsNullOrEmpty(description) || string.IsNullOrEmpty(location)) return orig(turnIn);
        Main.npcChatCornerItem = type;
        return description + "\n\n(" + location + ")";
    }

    private static void Report(ModType author, string hook, Exception e) =>
        ContentErrors.Report($"[angler] {author.Name}.{hook} 异常:{Safe.Describe(e)}");
}
