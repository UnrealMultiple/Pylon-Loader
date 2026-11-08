using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 配方管线。
/// 注册（AddRecipes 阶段）：ModRecipe 链式注册两条测试配方 —— 普通配方 + 带条件/消耗折扣/制作回调的配方。
/// recipe（Ready）：配方进表、材料/结果/工作台正确、条件对象就位。
/// recipe-w（World）：条件过滤（不满足时从制作菜单消失）、材料折扣回调、制作回调计数。
/// </summary>
internal static class RecipeTests
{
    /// <summary>配方 1（无条件）：10 木头 → 1 测试方块物品。</summary>
    public static ModRecipe Basic;

    /// <summary>配方 2（条件 + 回调）：5 石头 → 5 测试物品；条件永远可翻转。</summary>
    public static ModRecipe Conditional;

    public static bool ConditionMet = true;
    public static int ConsumeCalls, CraftCalls;
    public static int DiscountTo = -1;

    public static void AddRecipes()
    {
        var blockItem = ModContent.ItemType<Content.TestTileBlockItem>();
        var testItem = ModContent.ItemType<Content.TestItem>();

        Basic = new ModRecipe()
            .SetResult(blockItem, 1);
        Basic.AddIngredient(ItemID.Wood, 10);
        Basic.AddTile(TileID.WorkBenches);
        Basic.Register();

        Conditional = new ModRecipe()
            .SetResult(testItem, 5);
        Conditional.AddIngredient(ItemID.StoneBlock, 5);
        Conditional.AddTile(TileID.WorkBenches);
        Conditional.AddCondition("Conditions.TestRecipe", () => ConditionMet);
        Conditional.AddConsumeIngredientCallback((recipe, type, ref amount, isDecrafting) =>
        {
            ConsumeCalls++;
            if (DiscountTo >= 0) amount = DiscountTo;
        });
        Conditional.AddOnCraftCallback((recipe, item, consumed, destination) => CraftCalls++);
        Conditional.Register();

        PT.Info($"配方已注册：Basic=#{RecipeIndexOf(Basic)} Conditional=#{RecipeIndexOf(Conditional)}");
    }

    public static void Register()
    {
        // 配方在 Mod.AddRecipes 里注册（安装管线的 Recipes 阶段，晚于 PostSetupContent），
        // 所以断言全部放 World 组；"recipe" 组注册成 World 阶段保持组名兼容。
        PT.Group("recipe", Phase.World, RunReady);
        PT.Group("recipe-w", Phase.World, RunWorld);
    }

    private static int RecipeIndexOf(ModRecipe recipe) =>
        Terraria.Recipe.numRecipes > 0 && Main.recipe is { } table
            ? FindIndex(table, recipe)
            : -1;

    private static int FindIndex(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Recipe> table, ModRecipe mod)
    {
        for (var i = 0; i < table.Length; i++)
        {
            var r = table[i];
            if (r is not null && r.createItem is { } item && mod.Recipe.createItem is { } want &&
                item.type == want.type) return i;
        }
        return -1;
    }

    private static void RunReady()
    {
        PT.Case("RECIPE-R1", c =>
        {
            c.NotNull(Basic, "Basic 配方");
            c.NotNull(Conditional, "Conditional 配方");
            if (Basic is null || Conditional is null) return;

            // 材料与结果
            var basic = Basic.Recipe;
            c.Eq(basic.createItem.type, ModContent.ItemType<Content.TestTileBlockItem>(), "Basic 结果");
            c.Eq(basic.createItem.stack, 1, "Basic 结果数量");
            c.True(Basic.Ingredients == 1, $"Basic 材料数={Basic.Ingredients}");
            c.Eq(basic.requiredItem[0].type, ItemID.Wood, "Basic 材料");
            c.Eq(basic.requiredItem[0].stack, 10, "Basic 材料数量");
            c.Eq(Basic.CraftingStation, TileID.WorkBenches, "Basic 工作台");

            var cond = Conditional.Recipe;
            c.Eq(cond.createItem.type, ModContent.ItemType<Content.TestItem>(), "Conditional 结果");
            c.True(Conditional.Conditions.Count == 1, $"Conditional 条件数={Conditional.Conditions.Count}");
            c.True(Conditional.ConsumeCallbacks.Count == 1, "Consume 回调已登记");
            c.True(Conditional.CraftCallbacks.Count == 1, "Craft 回调已登记");
        });

        PT.Case("RECIPE-R2", c =>
        {
            // 配方进了游戏总表（Register 后 numRecipes 覆盖两条）。
            var table = Main.recipe;
            c.NotNull(table, "Main.recipe");
            c.True(Terraria.Recipe.numRecipes >= 2, $"配方总数={Terraria.Recipe.numRecipes}");
            c.True(RecipeIndexOf(Basic) >= 0, $"Basic 在表（下标 {RecipeIndexOf(Basic)}）");
            c.True(RecipeIndexOf(Conditional) >= 0, $"Conditional 在表（下标 {RecipeIndexOf(Conditional)}）");

            // 材料被标记为制作材料。
            c.True(ItemID.Sets.IsAMaterial[ItemID.Wood], "木头标记为材料");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("RECIPE-W1", "玩家还没就绪");
            return;
        }

        // 制作菜单按"附近环境 + 背包材料"过滤：放工作台、塞材料，再刷。
        var px0 = (int)(player.Center.X / 16f);
        var py0 = (int)(player.Center.Y / 16f);
        WorldGen.PlaceTile(px0, py0 + 1, TileID.WorkBenches, true, true, -1, 0);
        player.adjTile[TileID.WorkBenches] = true;
        GiveMaterial(player, ItemID.Wood, 99, 47);
        GiveMaterial(player, ItemID.StoneBlock, 99, 48);

        PT.Case("RECIPE-W1", c =>
        {
            // 刷新可用配方：条件满足时 Conditional 应在 availableRecipe 里。
            ConditionMet = true;
            Terraria.Recipe.FindRecipes(false);
            var idx = RecipeIndexOf(Conditional);
            var available = AvailableRecipes();
            c.True(available.Contains(idx), $"条件满足时在制作菜单（available={available.Count} 条）");

            // 翻转条件：再刷新就消失。
            ConditionMet = false;
            Terraria.Recipe.FindRecipes(false);
            var available2 = AvailableRecipes();
            c.True(!available2.Contains(idx), $"条件不满足时从制作菜单消失（available={available2.Count} 条）");
            ConditionMet = true;
            Terraria.Recipe.FindRecipes(false);
        });

        PT.Case("RECIPE-W2", c =>
        {
            // 折扣回调：问一次"这块材料实际消耗多少"（游戏算制作次数时就是这条路）。
            var cond = Conditional.Recipe;
            var entries = new Il2CppSystem.Collections.Generic.List<Terraria.Recipe.RequiredItemEntry>();
            cond.GetIngredientsForOneCraft(player, entries);
            c.True(entries.Count > 0, $"材料 entry 数={entries.Count}（stack 已填）");
            if (entries.Count == 0) return;

            var before = ConsumeCalls;
            var discount = cond.GetIngredientCraftingDiscount(player, entries[0]);
            c.True(ConsumeCalls > before, $"Consume 回调被调（{ConsumeCalls - before} 次）");

            // 回调把消耗改成 2 → 游戏拿到的是折扣量 = 需要量(5) - 消耗(2) = 3。
            DiscountTo = 2;
            var discounted = cond.GetIngredientCraftingDiscount(player, entries[0]);
            DiscountTo = -1;
            c.Eq(discounted, 3, $"折扣量（需要 {entries[0].stack} 消耗 2）={discounted}");

            // 制作回调：直接走发物环节（游戏点制作按钮后最终调的就是它）。
            var crafts = CraftCalls;
            var result = new Item();
            result.SetDefaults(ModContent.ItemType<Content.TestItem>(), null);
            result.stack = 5;
            Main.CraftItem_GrantItem(cond, result, false);
            c.True(CraftCalls > crafts, $"Craft 回调被调（{CraftCalls - crafts} 次）");
        });
    }

    private static void GiveMaterial(Player player, int type, int stack, int slot)
    {
        var item = new Item();
        item.SetDefaults(type, null);
        item.stack = stack;
        player.inventory[slot] = item;
    }

    private static System.Collections.Generic.List<int> AvailableRecipes()
    {
        var list = new System.Collections.Generic.List<int>();
        var available = Main.availableRecipe;
        if (available is null) return list;
        for (var i = 0; i < Main.numAvailableRecipes && i < available.Length; i++) list.Add(available[i]);
        return list;
    }

    private static bool HasItem(Player player, int type)
    {
        var inv = player.inventory;
        if (inv is null) return false;
        for (var i = 0; i < inv.Length; i++)
        {
            var item = inv[i];
            if (item is not null && item.type == type && item.stack > 0) return true;
        }
        return false;
    }
}
