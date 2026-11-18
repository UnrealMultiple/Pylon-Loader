using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>一份配方（在 AddRecipes 里 new 出来）：链式填完再 Register 交给游戏。</summary>
public class ModRecipe
{
    public delegate void IngredientQuantityCallback(Recipe recipe, int type, ref int amount, bool isDecrafting);

    [Obsolete("Replaced by IngredientQuantityCallback")]
    public delegate void ConsumeItemCallback(Recipe recipe, int type, ref int amount);

    public delegate void OnCraftCallback(Recipe recipe, Item item, List<Item> consumedItems, Item destinationStack);

    public static class IngredientQuantityRules
    {
        public static IngredientQuantityCallback Alchemy = (Recipe recipe, int type, ref int amount, bool isDecrafting) =>
        {
            if (!Main.LocalPlayer.alchemyTable && !isDecrafting) return;
            for (var i = amount; i > 0; i--)
            {
                if (Main.rand.Next(3) == 0) amount--;
            }
        };
    }

    /// <summary>一份配方最多几个材料（游戏的 requiredItem 表长）。</summary>
    public const int MaxIngredients = 15;

    public ModRecipe()
    {
        Recipe = AllocateRecipe();
        CraftingStation = -1;
        CustomShimmerResults = [];
        Conditions = [];
        DecraftConditions = [];
        ConsumeCallbacks = [];
        CraftCallbacks = [];
    }

    public static ModRecipe Create(int result, int amount = 1) => new ModRecipe().SetResult(result, amount);

    /// <summary>游戏这条配方（构造时抓的 Recipe.currentRecipe）。</summary>
    public Recipe Recipe { get; }

    public Mod? Mod { get; internal set; }

    /// <summary>已经加进去的材料数（AddIngredient 自增）。</summary>
    public int Ingredients { get; private set; }

    /// <summary>工作台的 tile 编号（-1 = 还没有）。</summary>
    public int CraftingStation { get; private set; }

    /// <summary>AddCustomShimmerResult 换来的那几个结果物品。</summary>
    public List<Item> CustomShimmerResults { get; }

    /// <summary>条件：不满足时这条配方从制作菜单里消失（向导那里还在）。Register 之后改也生效。</summary>
    public List<Condition> Conditions { get; }

    public List<Condition> DecraftConditions { get; }

    /// <summary>材料消耗回调；Register 之后由管线读。</summary>
    public List<IngredientQuantityCallback> ConsumeCallbacks { get; }

    /// <summary>制作完成回调；Register 之后由管线读。</summary>
    public List<OnCraftCallback> CraftCallbacks { get; }

    public int RecipeIndex { get; internal set; } = -1;

    public bool Disabled { get; private set; }

    public bool DecraftDisabled => Recipe is not null && Recipe.notDecraftable;

    /// <summary>这条配方出什么、出几个。</summary>
    public ModRecipe SetResult(int type, int stack = 1)
    {
        var item = Recipe.createItem;
        item.SetDefaults(type, null);
        item.stack = ClampStack(item, stack);
        return this;
    }

    /// <summary>加一件材料，并把它标成游戏的材料；超过 MaxIngredients 就丢掉。</summary>
    public ModRecipe AddIngredient(int type, int stack = 1)
    {
        if (Ingredients >= MaxIngredients)
            throw new InvalidOperationException($"一份配方最多 {MaxIngredients} 个材料；{type} 放不下");

        var item = Recipe.requiredItem[Ingredients++];
        item.SetDefaults(type, null);
        item.stack = ClampStack(item, stack);
        ItemID.Sets.IsAMaterial[type] = true;
        return this;
    }

    public ModRecipe AddIngredient(Mod mod, string itemName, int stack = 1)
    {
        mod ??= Mod!;
        if (!ModContent.TryFind(mod.Name, itemName, out ModItem? item))
            throw new ArgumentException($"Mod {mod.Name} 里没有物品 {itemName}", nameof(itemName));

        return AddIngredient(item.Type, stack);
    }

    public ModRecipe AddIngredient(ModItem item, int stack = 1) => AddIngredient(item.Type, stack);

    public ModRecipe AddIngredient<T>(int stack = 1) where T : ModItem =>
        AddIngredient(ModContent.ItemType<T>(), stack);

    /// <summary>这一份材料接受整个配方组；组里已有材料就不重复加。</summary>
    public ModRecipe AddRecipeGroup(RecipeGroup group, int stack = 1)
    {
        if (group == null) return this;

        var covered = false;
        for (var i = 0; i < Ingredients && !covered; i++) covered = group.Contains(Recipe.requiredItem[i].type);
        if (!covered) AddIngredient(group.GetPlaceholderItemType(), stack);

        Recipe.RequireGroup(group);
        return this;
    }

    /// <summary>按名字找组（mod 自建的组优先，然后是游戏自己的 Terraria.ID.RecipeGroups）。</summary>
    public ModRecipe AddRecipeGroup(string name, int stack = 1) => AddRecipeGroup(GetGroup(name), stack);

    /// <summary>按游戏的组号找组（RecipeGroup.recipeGroups）。</summary>
    public ModRecipe AddRecipeGroup(int groupId, int stack = 1) => AddRecipeGroup(GetGroup(groupId), stack);

    /// <summary>工作台；一份配方只有一个（游戏只存一个编号），第二个丢掉。</summary>
    public ModRecipe AddTile(int tileType)
    {
        if (tileType < 0) throw new ArgumentOutOfRangeException(nameof(tileType), "工作台编号不能是负数");
        if (CraftingStation != -1) return this;

        Recipe.SetCraftingStation(tileType);
        CraftingStation = tileType;
        return this;
    }

    public ModRecipe AddTile<T>() where T : ModTile => AddTile(ModContent.TileType<T>());

    /// <summary>丢进微光时出这个（type &gt; 0 才算）。</summary>
    public ModRecipe AddCustomShimmerResult(int type, int stack = 1)
    {
        if (type > 0) CustomShimmerResults.Add(Recipe.AddCustomShimmerResult(type, stack));
        return this;
    }

    /// <summary>加一条条件（描述键 + 判定函数）。</summary>
    public ModRecipe AddCondition(Condition condition)
    {
        if (condition != null) Conditions.Add(condition);
        return this;
    }

    /// <summary>加一条条件（描述文本/键 + 判定函数）。</summary>
    public ModRecipe AddCondition(string description, Func<bool> predicate) =>
        AddCondition(new Condition(description, predicate));

    public ModRecipe AddCondition(LocalizedText description, Func<bool> predicate) =>
        AddCondition(new Condition(description, predicate));

    public ModRecipe AddCondition(params Condition[] conditions) =>
        AddCondition((IEnumerable<Condition>)conditions);

    public ModRecipe AddCondition(IEnumerable<Condition> conditions)
    {
        foreach (var condition in conditions)
            if (condition != null) Conditions.Add(condition);

        return this;
    }

    public ModRecipe AddDecraftCondition(Condition condition)
    {
        if (condition != null) DecraftConditions.Add(condition);
        return this;
    }

    public ModRecipe AddDecraftCondition(string description, Func<bool> predicate) =>
        AddDecraftCondition(new Condition(description, predicate));

    public ModRecipe AddDecraftCondition(LocalizedText description, Func<bool> predicate) =>
        AddDecraftCondition(new Condition(description, predicate));

    public ModRecipe AddDecraftCondition(params Condition[] conditions) =>
        AddDecraftCondition((IEnumerable<Condition>)conditions);

    public ModRecipe AddDecraftCondition(IEnumerable<Condition> conditions)
    {
        foreach (var condition in conditions)
            if (condition != null) DecraftConditions.Add(condition);

        return this;
    }

    public ModRecipe ApplyConditionsAsDecraftConditions()
    {
        foreach (var condition in Conditions)
            if (!DecraftConditions.Contains(condition)) DecraftConditions.Add(condition);

        return this;
    }

    public ModRecipe DisableDecraft()
    {
        Recipe.notDecraftable = true;
        return this;
    }

    /// <summary>去掉一条条件（Register 之后也生效，管线读的是同一个列表）；返回是否真的找到并去掉了。</summary>
    public bool RemoveCondition(Condition condition) => condition != null && Conditions.Remove(condition);

    /// <summary>加一个材料消耗回调。</summary>
    public ModRecipe AddConsumeIngredientCallback(IngredientQuantityCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        ConsumeCallbacks.Add(callback);
        return this;
    }

    [Obsolete("Replaced by AddConsumeIngredientCallback")]
    public ModRecipe AddConsumeItemCallback(ConsumeItemCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        ConsumeCallbacks.Add((recipe, type, ref amount, isDecrafting) => callback(recipe, type, ref amount));
        return this;
    }

    /// <summary>加一个制作完成回调。</summary>
    public ModRecipe AddOnCraftCallback(OnCraftCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        CraftCallbacks.Add(callback);
        return this;
    }

    public ModRecipe Clone()
    {
        var clone = new ModRecipe { Mod = Mod };
        var result = Recipe.createItem;
        if (result is not null && result.type > 0) clone.SetResult(result.type, result.stack);

        var required = Recipe.requiredItem;
        for (var i = 0; i < Ingredients && i < required.Length; i++)
        {
            var item = required[i];
            if (item is null || item.type <= 0) break;
            clone.AddIngredient(item.type, item.stack);
        }

        var groups = Recipe.acceptedGroups;
        if (groups is not null)
        {
            for (var i = 0; i < groups.Length; i++)
            {
                if (groups[i] < 0) continue;
                if (RecipeGroup.recipeGroups.ContainsKey(groups[i]))
                    clone.Recipe.RequireGroup(RecipeGroup.recipeGroups[groups[i]]);
            }
        }

        clone.Recipe.requiredTile = Recipe.requiredTile;
        clone.CraftingStation = CraftingStation;
        clone.Recipe.notDecraftable = Recipe.notDecraftable;
        clone.Recipe.needHoney = Recipe.needHoney;
        clone.Recipe.needWater = Recipe.needWater;
        clone.Recipe.needLava = Recipe.needLava;
        clone.Recipe.needTorchGodsFavor = Recipe.needTorchGodsFavor;
        clone.Recipe.alchemy = Recipe.alchemy;
        clone.Recipe.needSnowBiome = Recipe.needSnowBiome;
        clone.Recipe.needGraveyardBiome = Recipe.needGraveyardBiome;
        clone.Recipe.needMechdusa = Recipe.needMechdusa;
        clone.Recipe.crimson = Recipe.crimson;
        clone.Recipe.corruption = Recipe.corruption;
        clone.Conditions.AddRange(Conditions);
        clone.DecraftConditions.AddRange(DecraftConditions);
        clone.ConsumeCallbacks.AddRange(ConsumeCallbacks);
        clone.CraftCallbacks.AddRange(CraftCallbacks);
        return clone;
    }

    public bool HasResult(int itemID) => Recipe.createItem is { } item && item.type == itemID;

    public bool HasResult(ModItem item) => HasResult(item.Type);

    public bool HasResult<T>() where T : ModItem => HasResult(ModContent.ItemType<T>());

    public bool HasIngredient(int itemID) => IngredientSlot(itemID) >= 0;

    public bool HasIngredient(ModItem item) => HasIngredient(item.Type);

    public bool HasIngredient<T>() where T : ModItem => HasIngredient(ModContent.ItemType<T>());

    public bool HasRecipeGroup(int id)
    {
        var groups = Recipe.acceptedGroups;
        if (groups is null) return false;
        for (var i = 0; i < groups.Length; i++)
            if (groups[i] == id) return true;

        return false;
    }

    public bool HasRecipeGroup(RecipeGroup group) => group is not null && HasRecipeGroup(group.RegisteredId);

    public bool HasTile(int tileID) => Recipe.requiredTile == tileID;

    public bool HasCondition(Condition condition) => condition is not null && Conditions.Contains(condition);

    public bool HasShimmerCondition(Condition condition) =>
        condition is not null && DecraftConditions.Contains(condition);

    public bool TryGetResult(int itemID, out Item result)
    {
        result = Recipe.createItem;
        return result is not null && result.type == itemID;
    }

    public bool TryGetResult(ModItem item, out Item result) => TryGetResult(item.Type, out result);

    public bool TryGetResult<T>(out Item result) where T : ModItem => TryGetResult(ModContent.ItemType<T>(), out result);

    public bool TryGetIngredient(int itemID, out Item ingredient)
    {
        var slot = IngredientSlot(itemID);
        ingredient = slot >= 0 ? Recipe.requiredItem[slot] : null!;
        return slot >= 0;
    }

    public bool TryGetIngredient(ModItem item, out Item ingredient) => TryGetIngredient(item.Type, out ingredient);

    public bool TryGetIngredient<T>(out Item ingredient) where T : ModItem =>
        TryGetIngredient(ModContent.ItemType<T>(), out ingredient);

    public bool RemoveIngredient(Item item)
    {
        if (item is null) return false;
        var required = Recipe.requiredItem;
        for (var i = 0; i < Ingredients && i < required.Length; i++)
        {
            if (required[i] is null || required[i].type != item.type) continue;
            RemoveIngredientAt(i);
            return true;
        }

        return false;
    }

    public bool RemoveIngredient(int itemID)
    {
        var removed = false;
        int slot;
        while ((slot = IngredientSlot(itemID)) >= 0)
        {
            RemoveIngredientAt(slot);
            removed = true;
        }

        return removed;
    }

    public bool RemoveTile(int tileID)
    {
        if (Recipe.requiredTile != tileID) return false;
        Recipe.requiredTile = -1;
        CraftingStation = -1;
        return true;
    }

    public bool RemoveRecipeGroup(int groupID)
    {
        var groups = Recipe.acceptedGroups;
        if (groups is null) return false;
        for (var i = 0; i < groups.Length; i++)
        {
            if (groups[i] != groupID) continue;
            for (var j = i; j < groups.Length - 1; j++) groups[j] = groups[j + 1];
            groups[groups.Length - 1] = -1;
            return true;
        }

        return false;
    }

    public bool RemoveShimmerCondition(Condition condition) =>
        condition is not null && DecraftConditions.Remove(condition);

    public void DisableRecipe()
    {
        Disabled = true;
        RecipeTable.SetDisabled(Recipe, true);
    }

    public void ReplaceResult(int itemID, int stack = 1)
    {
        Recipe.createItem.SetDefaults(itemID, null);
        Recipe.createItem.stack = ClampStack(Recipe.createItem, stack);
    }

    public void ReplaceResult(ModItem item, int stack = 1) => ReplaceResult(item.Type, stack);

    public void ReplaceResult<T>(int stack = 1) where T : ModItem =>
        ReplaceResult(ModContent.ItemType<T>(), stack);

    private int IngredientSlot(int itemID)
    {
        var required = Recipe.requiredItem;
        for (var i = 0; i < Ingredients && i < required.Length; i++)
        {
            var item = required[i];
            if (item is null || item.type <= 0) return -1;
            if (item.type == itemID) return i;
        }

        return -1;
    }

    private void RemoveIngredientAt(int slot)
    {
        var required = Recipe.requiredItem;
        for (var i = slot; i < Ingredients - 1 && i < required.Length - 1; i++)
            required[i] = required[i + 1];

        var last = Ingredients - 1;
        if (last >= 0 && last < required.Length)
        {
            var item = required[last];
            if (item is not null)
            {
                item.SetDefaults(0, null);
                item.stack = 0;
            }
        }

        if (Ingredients > 0) Ingredients--;
    }

    /// <summary>写游戏 Recipe 上的开关（needWater、notDecraftable、alchemy 那批 bool）。</summary>
    public ModRecipe SetProperty(string name, bool value)
    {
        typeof(Recipe).GetProperty(name)?.SetValue(Recipe, value);
        return this;
    }

    /// <summary>把配方交给游戏（表不够先扩容，超出的配方仍能被分解/向导看到）。</summary>
    public ModRecipe Register()
    {
        var index = Terraria.Recipe.numRecipes;
        var recipes = Main.recipe;
        if (recipes == null || index >= recipes.Length) Main.recipe = Grow(recipes, index + 1);

        Terraria.Recipe.currentRecipe = Recipe;
        Terraria.Recipe.AddRecipe();
        RecipeTable.Added(index, CraftingStation, this);
        return this;
    }

    /// <summary>已经是组对象就原样返回。</summary>
    public static RecipeGroup GetGroup(RecipeGroup group) => group;

    /// <summary>按游戏的组号找（没有就是 null）。</summary>
    public static RecipeGroup GetGroup(int groupId)
    {
        var groups = RecipeGroup.recipeGroups;
        return groups.ContainsKey(groupId) ? groups[groupId] : null!;
    }

    /// <summary>按名字找组（自己用 CreateRecipeGroup 建的，或游戏自己的 'IronBar'、'Wood'…）。</summary>
    public static RecipeGroup GetGroup(string name)
    {
        if (string.IsNullOrEmpty(name)) return null!;
        var mine = RecipeTable.Group(name);
        if (mine != null) return mine;

        // 游戏那批组是 Terraria.ID.RecipeGroups 的静态属性，只能按名字反射；
        // 属性 getter 跨进原生，抛什么由游戏决定，只能整段兜住。
        try
        {
            return (typeof(RecipeGroups).GetProperty(name)?.GetValue(null) as RecipeGroup)!;
        }
        catch (Exception)
        {
            return null!;
        }
    }

    /// <summary>旧名字，转给 GetGroup。</summary>
    public static RecipeGroup GetGroupByName(string name) => GetGroup(name);

    /// <summary>新建（或复用）一个配方组；返回的组要在 AddRecipeGroup 里用。</summary>
    public static RecipeGroup CreateRecipeGroup(string name, int[] itemTypes = null!)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

        var known = RecipeTable.Group(name);
        if (known != null) return known;

        itemTypes ??= Array.Empty<int>();
        var group = new RecipeGroup(ModLocalization.Key("RecipeGroups." + name), itemTypes);
        foreach (var type in itemTypes) ItemID.Sets.IsAMaterial[type] = true;

        group.Register();
        Safe.Run("grupo de receita " + name, group.SortDecraftingEntries);
        RecipeTable.SetGroup(name, group);
        return group;
    }

    /// <summary>堆叠数夹到 1..物品上限（0 = 用 9999）。</summary>
    private static int ClampStack(Item item, int stack) =>
        Math.Max(1, Math.Min(stack, item.maxStack > 0 ? item.maxStack : 9999));

    private static Recipe AllocateRecipe()
    {
        var recipe = new Recipe();

        if (recipe.createItem is null) recipe.createItem = new Item();

        if (recipe.requiredItem is null)
        {
            var items = new Il2CppReferenceArray<Item>(MaxIngredients);
            for (var i = 0; i < items.Length; i++) items[i] = new Item();
            recipe.requiredItem = items;
        }

        if (recipe.acceptedGroups is null)
        {
            var groups = new Il2CppStructArray<int>(MaxIngredients);
            for (var i = 0; i < groups.Length; i++) groups[i] = -1;
            recipe.acceptedGroups = groups;
        }

        recipe.requiredTile = -1;
        return recipe;
    }

    /// <summary>配方表扩容：旧表的内容搬过去，多的留空。</summary>
    private static Il2CppReferenceArray<Recipe> Grow(Il2CppReferenceArray<Recipe>? recipes, int length)
    {
        var grown = new Il2CppReferenceArray<Recipe>(length);
        if (recipes != null) for (var i = 0; i < recipes.Length && i < length; i++) grown[i] = recipes[i];
        return grown;
    }
}