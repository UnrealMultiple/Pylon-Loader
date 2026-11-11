using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 配方管线：条件过滤 / 向导过滤 / 配方组名 / 材料折扣 / 制作回调。
/// 侧表在 RecipeTable（密集 struct 数组，判定路径取 ref）。
/// </summary>
internal sealed class RecipePipeline : ILoaderModule
{
    public static RecipePipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];

    public string Name => "recipe";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Recipe.FindRecipes += FindRecipes;
        On.GUICraftGuidePopup.FindRecipes += GuideRecipes;
        On.Terraria.Recipe.ProcessGroupsForText += GroupText;
        On.Terraria.Recipe.GetIngredientCraftingDiscount += Discount;
        On.Terraria.Main.CraftItem_GrantItem += Crafted;

        Log.Info("[recipe] 已挂（条件过滤 / 向导过滤 / 配方组名 / 材料折扣 / 制作回调）");
        return true;
    }

    #region 过滤

    /// <summary>原位压缩：满足的往前挪，最后改计数并让游戏按原来那条重新聚焦。</summary>
    private void FindRecipes(On.Terraria.Recipe.orig_FindRecipes orig, bool canDelayCheck)
    {
        orig(canDelayCheck);

        var available = Main.availableRecipe;
        if (available is null) return;

        var count = Main.numAvailableRecipes;
        if (count <= 0 || count > available.Length) return;

        // 先记下玩家正在看的那条，压缩之后要按它重新聚焦。
        var focus = Main.focusRecipe;
        var focused = focus >= 0 && focus < count ? available[focus] : -1;

        var kept = 0;
        var dropped = false;

        for (var i = 0; i < count; i++)
        {
            var index = available[i];
            if (!RecipeTable.Available(index, true))
            {
                dropped = true;
                continue;
            }

            if (kept != i) available[kept] = index;
            kept++;
        }

        if (!dropped) return;

        Main.numAvailableRecipes = kept;

        // 不重新聚焦的话，玩家会看到光标跳到另一条配方上。
        if (focused < 0) return;

        for (var i = 0; i < kept; i++)
        {
            if (available[i] != focused) continue;

            Main.focusRecipe = i;
            return;
        }

        Main.focusRecipe = 0;
    }

    /// <summary>向导对话只滤全局否决：条件在向导里藏掉会让玩家查不到这条配方。</summary>
    private void GuideRecipes(On.GUICraftGuidePopup.orig_FindRecipes orig, GUICraftGuidePopup self)
    {
        orig(self);

        var available = Main.availableRecipe;
        var count = Main.numAvailableRecipes;
        if (available is null || count <= 0 || count > available.Length) return;

        // 压缩会改变下标，正在看的那条要重新定位。
        var focus = Main.focusRecipe;
        var focused = focus >= 0 && focus < count ? available[focus] : -1;

        var kept = 0;
        var dropped = false;

        for (var i = 0; i < count; i++)
        {
            var index = available[i];
            if (!RecipeTable.Available(index, false))
            {
                dropped = true;
                continue;
            }

            if (kept != i) available[kept] = index;
            kept++;
        }

        if (!dropped) return;

        Main.numAvailableRecipes = kept;

        if (focused < 0) return;
        for (var i = 0; i < kept; i++)
        {
            if (available[i] != focused) continue;
            Main.focusRecipe = i;
            return;
        }

        Main.focusRecipe = 0;
    }

    #endregion

    #region 配方组

    private bool GroupText(On.Terraria.Recipe.orig_ProcessGroupsForText orig, Recipe self, int type,
                           out string theText)
    {
        var handled = orig(self, type, out theText);
        if (handled) return true;

        var name = RecipeTable.NameOfGroup(type);
        if (name is null) return false;

        theText = name;
        return true;
    }

    #endregion

    #region 消耗与制作

    /// <summary>原版折扣之后让作者继续改，最后夹回 [0, 手上有的数量]：不夹会出现负数消耗或超量消耗。</summary>
    private int Discount(On.Terraria.Recipe.orig_GetIngredientCraftingDiscount orig, Recipe self, Player player,
                         Recipe.RequiredItemEntry req)
    {
        var amount = orig(self, player, req);

        ref var entry = ref RecipeTable.EntryOf(self, out var owned);
        var consume = entry.Consume;
        var globals = GlobalRecipeRegistry.For(HookId.ConsumeItem);
        if (!owned && globals.Count == 0) return amount;

        // 语义：回调里的 amount 是"实际消耗多少"；这个方法游戏拿返回值当"折扣量"（从消耗里减）。
        // 所以先算当前消耗（需要量 - 原版折扣），让作者改，最后换算回折扣量返回。
        var type = req.itemIdOrRecipeGroup;
        var result = req.stack - amount;

        if (consume is not null)
        {
            for (var i = 0; i < consume.Count; i++)
            {
                try
                {
                    consume[i](self, type, ref result, false);
                }
                catch (Exception e)
                {
                    Fail("recipe.consume", HookId.ConsumeItem, e);
                }
            }
        }

        for (var i = 0; i < globals.Count; i++)
        {
            try
            {
                globals[i].ConsumeItem(self, type, ref result);
            }
            catch (Exception e)
            {
                Fail(globals[i].Name, HookId.ConsumeItem, e);
            }
        }

        var consumed = result;
        if (consumed > req.stack) consumed = req.stack;
        if (consumed < 0) consumed = 0;

        return req.stack - consumed;
    }

    /// <summary>先回调、再发物品：作者要在物品进背包之前改它。</summary>
    private void Crafted(On.Terraria.Main.orig_CraftItem_GrantItem orig, Recipe recipe, Item result, bool quickCraft)
    {
        if (recipe is not null && result is not null)
        {
            ref var entry = ref RecipeTable.EntryOf(recipe, out var owned);
            var craft = entry.Craft;

            if (craft is not null)
            {
                var consumed = new List<Item>();
                for (var i = 0; i < craft.Count; i++)
                {
                    try
                    {
                        craft[i](recipe, result, consumed, null!);
                    }
                    catch (Exception e)
                    {
                        Fail("recipe.craft", HookId.OnCraft, e);
                    }
                }
            }

            if (owned || GlobalRecipeRegistry.For(HookId.OnCraft).Count > 0)
            {
                var globals = GlobalRecipeRegistry.For(HookId.OnCraft);
                for (var i = 0; i < globals.Count; i++)
                {
                    try
                    {
                        globals[i].OnCraft(result, recipe);
                    }
                    catch (Exception e)
                    {
                        Fail(globals[i].Name, HookId.OnCraft, e);
                    }
                }
            }
        }

        orig(recipe, result, quickCraft);
    }

    #endregion

    /// <summary>同一 (名字, 钩子) 只记一条：制作界面会连着问很多次。</summary>
    private void Fail(string name, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(name.GetHashCode(), hook))) return;
        ContentErrors.Report($"[recipe] {hook} 异常：{Safe.Describe(exception)}");
    }
}
