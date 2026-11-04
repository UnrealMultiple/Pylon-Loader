using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 一条配方的侧表项。默认值全是 null / 0：普通配方不分配任何东西。
/// 用 struct + 密集数组（下标 = Main.recipe 的下标）而不是每条一个对象。
/// </summary>
internal struct RecipeEntry
{
    public int Station;
    public List<Condition>? Conditions;
    public List<ModRecipe.ConsumeIngredientCallback>? Consume;
    public List<ModRecipe.OnCraftCallback>? Craft;
}

/// <summary>
/// 配方侧表与配方组登记。按配方下标密集存放，判定路径上取 ref，不复制、不装箱、不查字典。
/// </summary>
internal static class RecipeTable
{
    private sealed class GroupInfo
    {
        public RecipeGroup Group = null!;
        public string Name = "";
    }

    private static RecipeEntry[] _entries = new RecipeEntry[512];
    private static readonly Dictionary<nint, int> IndexOf = [];
    private static readonly Dictionary<string, GroupInfo> Groups = new(StringComparer.Ordinal);

    internal static int Count { get; private set; }

    /// <summary>这条配方有侧表数据吗（没有就走原版快路径）。</summary>
    internal static bool HasAny { get; private set; }

    internal static void Added(int index, int station, ModRecipe recipe)
    {
        if (index < 0) return;

        if (index >= _entries.Length)
        {
            var grown = new RecipeEntry[Math.Max(index + 1, _entries.Length * 2)];
            Array.Copy(_entries, grown, _entries.Length);
            _entries = grown;
        }

        ref var entry = ref _entries[index];
        entry.Station = station;

        if (recipe.Conditions.Count > 0) entry.Conditions = recipe.Conditions;
        if (recipe.ConsumeCallbacks.Count > 0) entry.Consume = recipe.ConsumeCallbacks;
        if (recipe.CraftCallbacks.Count > 0) entry.Craft = recipe.CraftCallbacks;

        var pointer = recipe.Recipe?.Pointer ?? 0;
        if (pointer != 0) IndexOf[pointer] = index;

        if (index + 1 > Count) Count = index + 1;
        if (entry.Conditions is not null || entry.Consume is not null || entry.Craft is not null) HasAny = true;
    }

    internal static ref RecipeEntry At(int index) => ref _entries[index];

    internal static bool TryIndex(Recipe recipe, out int index)
    {
        index = -1;
        return recipe is not null && IndexOf.TryGetValue(recipe.Pointer, out index);
    }

    /// <summary>按对象取侧表项；不是 mod 配方就返回一个空的（调用方改不到真表）。</summary>
    internal static ref RecipeEntry EntryOf(Recipe recipe, out bool owned)
    {
        owned = TryIndex(recipe, out var index);
        return ref owned ? ref _entries[index] : ref None;
    }

    private static RecipeEntry None;

    #region 判定

    /// <summary>这条配方现在能不能出现在制作菜单里。</summary>
    internal static bool Available(int index, bool applyConditions)
    {
        var recipes = Main.recipe;
        if (recipes is null || index < 0 || index >= recipes.Length) return true;

        var recipe = recipes[index];
        if (recipe is null) return true;

        if (applyConditions && index < _entries.Length)
        {
            ref var entry = ref _entries[index];
            var conditions = entry.Conditions;
            if (conditions is not null)
            {
                for (var i = 0; i < conditions.Count; i++)
                {
                    bool met;
                    try
                    {
                        met = conditions[i].IsMet();
                    }
                    catch (Exception e)
                    {
                        Safe.Report("recipe.condition", e);
                        met = false;
                    }

                    if (!met) return false;
                }
            }
        }

        var globals = GlobalRecipeRegistry.For(HookId.RecipeAvailable);
        for (var i = 0; i < globals.Count; i++)
        {
            try
            {
                if (!globals[i].RecipeAvailable(recipe)) return false;
            }
            catch (Exception e)
            {
                // 宁可显示也不静默吞掉配方。
                Safe.Report("recipe." + globals[i].Name + ".RecipeAvailable", e);
            }
        }

        return true;
    }

    #endregion

    #region 配方组

    internal static RecipeGroup? Group(string name) =>
        name is not null && Groups.TryGetValue(name, out var info) ? info.Group : null;

    internal static void SetGroup(string name, RecipeGroup group, string display = "")
    {
        if (string.IsNullOrEmpty(name) || group is null) return;
        Groups[name] = new GroupInfo { Group = group, Name = display };
    }

    /// <summary>自定义组的显示名；不是我们的组返回 null。</summary>
    internal static string? NameOfGroup(int registeredId)
    {
        foreach (var info in Groups.Values)
        {
            try
            {
                if (info.Group.RegisteredId != registeredId) continue;

                return string.IsNullOrEmpty(info.Name) ? null : info.Name;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    #endregion

    /// <summary>所有配方加完之后把游戏那几张派生表重建一遍：每张单独兜，缺一张只影响一小块界面。</summary>
    internal static void Finish(IModLog log)
    {
        if (Count == 0) return;

        Step(log, "必需材料索引", Recipe.CreateRequiredItemQuickLookups);
        Step(log, "材料表", Recipe.UpdateMaterialFieldForAllRecipes);
        Step(log, "材料标记", Recipe.UpdateWhichItemsAreMaterials);
        Step(log, "可制作表", Recipe.UpdateWhichItemsAreCrafted);
        Step(log, "微光表", Terraria.GameContent.ShimmerTransforms.UpdateRecipeSets);
        Step(log, "样例物品", Terraria.ID.ContentSamples.FixItemsAfterRecipesAreAdded);

        log.Info($"[recipe] 收尾：mod 配方 {Count} 条（游戏总数 {Recipe.numRecipes}）");

        if (Recipe.numRecipes > Recipe.maxRecipes)
        {
            log.Warn($"[recipe] 配方总数 {Recipe.numRecipes} 超过游戏上限 {Recipe.maxRecipes}：超出的部分仍可分解/可被向导查到，但制作菜单里看不到");
        }
    }

    private static void Step(IModLog log, string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            log.Warn($"[recipe] 重建「{what}」失败：{e.Message}");
        }
    }
}
