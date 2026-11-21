using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>配方管线：条件过滤 / 向导过滤 / 配方组名 / 材料折扣 / 制作回调；侧表在 RecipeTable（密集 struct 数组，判定路径取 ref）。</summary>
internal sealed class RecipePipeline : ILoaderModule
{
    public static RecipePipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];

    public string Name => "recipe";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>recipe 管线的安装门：配方在安装期之后才注册，按注册期可见的 AddRecipes/AddRecipeGroups 覆写与 GlobalRecipe 钩子建门。</summary>
    internal static class Gates
    {
        internal struct Filter : IGate
        {
            public static bool Wanted() => HasRecipes() || GlobalRecipeRegistry.For(HookId.RecipeAvailable).Count > 0;

            public static void Install()
            {
                On.Terraria.Recipe.FindRecipes += Instance.FindRecipes;
                On.GUICraftGuidePopup.FindRecipes += Instance.GuideRecipes;
            }
        }

        internal struct Groups : IGate
        {
            public static bool Wanted() => HasRecipes() || HasRecipeGroups();

            public static void Install() => On.Terraria.Recipe.ProcessGroupsForText += Instance.GroupText;
        }

        internal struct Consume : IGate
        {
            public static bool Wanted() => HasRecipes() || GlobalRecipeRegistry.For(HookId.ConsumeItem).Count > 0;

            public static void Install() => On.Terraria.Recipe.GetIngredientCraftingDiscount += Instance.Discount;
        }

        internal struct Craft : IGate
        {
            public static bool Wanted() =>
                HasRecipes() || GlobalRecipeRegistry.For(HookId.OnCraft).Count > 0 ||
                HookGuard.Wants<ModItem>(HookId.OnCreated) || GlobalItemRegistry.AnyWith(HookId.OnCreated);

            public static void Install()
            {
                On.Terraria.Main.CraftItem_GrantItem += Instance.Crafted;

                Log.Info("[recipe] 已挂（条件过滤 / 向导过滤 / 配方组名 / 材料折扣 / 制作回调）");
            }
        }
    }

    public bool Install() => GateHub<RecipePipeline>.Run();

    private static bool HasRecipes() =>
        HookGuard.Wants<ModItem>(HookId.AddRecipes) ||
        HookGuard.Wants<GlobalItem>(HookId.AddRecipes) ||
        GlobalNpcRegistry.All.Any(static global => global.Plan.Has(HookId.AddRecipes)) ||
        SystemLoader.With(HookId.AddRecipes).Length > 0 ||
        LoaderHost.Current.Mods.Mods.Any(static mod => mod.Plan.Has(HookId.AddRecipes));

    private static bool HasRecipeGroups() =>
        SystemLoader.With(HookId.AddRecipeGroups).Length > 0 ||
        LoaderHost.Current.Mods.Mods.Any(static mod => mod.Plan.Has(HookId.AddRecipeGroups)) ||
        GlobalNpcRegistry.All.Any(static global => global.Plan.Has(HookId.AddRecipeGroups));

    #region 过滤

    private void FindRecipes(On.Terraria.Recipe.orig_FindRecipes orig, bool canDelayCheck)
    {
        orig(canDelayCheck);

        var available = Main.availableRecipe;
        if (available is null) return;

        var count = Main.numAvailableRecipes;
        if (count <= 0 || count > available.Length) return;

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

        if (focused < 0) return;

        for (var i = 0; i < kept; i++)
        {
            if (available[i] != focused) continue;

            Main.focusRecipe = i;
            return;
        }

        Main.focusRecipe = 0;
    }

    private void GuideRecipes(On.GUICraftGuidePopup.orig_FindRecipes orig, GUICraftGuidePopup self)
    {
        orig(self);

        var available = Main.availableRecipe;
        var count = Main.numAvailableRecipes;
        if (available is null || count <= 0 || count > available.Length) return;

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

    private int Discount(On.Terraria.Recipe.orig_GetIngredientCraftingDiscount orig, Recipe self, Player player,
                         Recipe.RequiredItemEntry req)
    {
        var amount = orig(self, player, req);

        ref var entry = ref RecipeTable.EntryOf(self, out var owned);
        var consume = entry.Consume;
        var globals = GlobalRecipeRegistry.For(HookId.ConsumeItem);
        if (!owned && globals.Count == 0) return amount;

        int type;
        try
        {
            type = req.IsRecipeGroup ? req.RecipeGroup.GetPlaceholderItemType() : req.itemIdOrRecipeGroup;
        }
        catch (Exception e)
        {
            Fail("recipe.consume", HookId.ConsumeItem, e);
            return amount;
        }

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

    private void Crafted(On.Terraria.Main.orig_CraftItem_GrantItem orig, Recipe recipe, Item result, bool quickCraft)
    {
        if (recipe is not null && result is not null)
        {
            ref var entry = ref RecipeTable.EntryOf(recipe, out var owned);
            var craft = entry.Craft;
            var consumed = new List<Item>();

            if (craft is not null)
            {
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

            var context = new RecipeItemCreationContext(recipe, consumed, result);

            if (ModContent.IsCustom(ContentKind.Item, result.type))
            {
                var mod = ItemPipeline.Instance.InstanceOf(result);
                if (mod is not null && mod.Plan.Has(HookId.OnCreated))
                {
                    try
                    {
                        mod.OnCreated(context);
                    }
                    catch (Exception e)
                    {
                        Fail(mod.FullKey, HookId.OnCreated, e);
                    }
                }
            }

            foreach (var global in GlobalItemRegistry.For(result, HookId.OnCreated))
            {
                try
                {
                    global.OnCreated(result, context);
                }
                catch (Exception e)
                {
                    Fail(global.FullKey, HookId.OnCreated, e);
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

    private void Fail(string name, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(name.GetHashCode(), hook))) return;
        ContentErrors.Report($"[recipe] {hook} 异常：{Safe.Describe(exception)}");
    }
}
