using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>一份配方（在 AddRecipes 里 new 出来）：链式填完再 Register 交给游戏。</summary>
public class ModRecipe
{
    /// <summary>每件材料实际消耗多少（amount 可写，0 = 不消耗）。</summary>
    public delegate void ConsumeIngredientCallback(Recipe recipe, int type, ref int amount, bool isDecrafting);

    /// <summary>制作完成之前（手机端不给 consumedItems 与 destinationStack，都是 null）。</summary>
    public delegate void OnCraftCallback(Recipe recipe, Item item, List<Item> consumedItems, Item destinationStack);

    /// <summary>一份配方最多几个材料（游戏的 requiredItem 表长）。</summary>
    public const int MaxIngredients = 15;

    /// <summary>抓住当前这条配方；默认值照参考：0 个材料、没有工作台。</summary>
    public ModRecipe()
    {
        Recipe = Terraria.Recipe.currentRecipe;
        CraftingStation = -1;
        CustomShimmerResults = [];
        Conditions = [];
        ConsumeCallbacks = [];
        CraftCallbacks = [];
    }

    /// <summary>游戏这条配方（构造时抓的 Recipe.currentRecipe）。</summary>
    public Recipe Recipe { get; }

    /// <summary>已经加进去的材料数（AddIngredient 自增）。</summary>
    public int Ingredients { get; private set; }

    /// <summary>工作台的 tile 编号（-1 = 还没有）。</summary>
    public int CraftingStation { get; private set; }

    /// <summary>AddCustomShimmerResult 换来的那几个结果物品。</summary>
    public List<Item> CustomShimmerResults { get; }

    /// <summary>条件：不满足时这条配方从制作菜单里消失（向导那里还在）。Register 之后改也生效。</summary>
    public List<Condition> Conditions { get; }

    /// <summary>材料消耗回调；Register 之后由管线读。</summary>
    public List<ConsumeIngredientCallback> ConsumeCallbacks { get; }

    /// <summary>制作完成回调；Register 之后由管线读。</summary>
    public List<OnCraftCallback> CraftCallbacks { get; }

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
        if (Ingredients >= MaxIngredients) return this;

        var item = Recipe.requiredItem[Ingredients++];
        item.SetDefaults(type, null);
        item.stack = ClampStack(item, stack);
        ItemID.Sets.IsAMaterial[type] = true;
        return this;
    }

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
        if (CraftingStation != -1) return this;

        Recipe.SetCraftingStation(tileType);
        CraftingStation = tileType;
        return this;
    }

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

    /// <summary>去掉一条条件（Register 之后也生效，管线读的是同一个列表）；返回是否真的找到并去掉了。</summary>
    public bool RemoveCondition(Condition condition) => condition != null && Conditions.Remove(condition);

    /// <summary>加一个材料消耗回调。</summary>
    public ModRecipe AddConsumeIngredientCallback(ConsumeIngredientCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        ConsumeCallbacks.Add(callback);
        return this;
    }

    /// <summary>旧名字，转给 AddConsumeIngredientCallback。</summary>
    public ModRecipe AddConsumeItemCallback(ConsumeIngredientCallback callback) => AddConsumeIngredientCallback(callback);

    /// <summary>加一个制作完成回调。</summary>
    public ModRecipe AddOnCraftCallback(OnCraftCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        CraftCallbacks.Add(callback);
        return this;
    }

    /// <summary>写游戏 Recipe 上的开关（needWater、notDecraftable、alchemy 那批 bool）。</summary>
    public ModRecipe SetProperty(string name, bool value)
    {
        typeof(Recipe).GetProperty(name)?.SetValue(Recipe, value);
        return this;
    }

    /// <summary>把配方交给游戏（表不够先扩容，超出的配方仍能被分解/向导看到）。</summary>
    public void Register()
    {
        var index = Terraria.Recipe.numRecipes;
        var recipes = Main.recipe;
        if (recipes == null || index >= recipes.Length) Main.recipe = Grow(recipes, index + 1);

        Terraria.Recipe.AddRecipe();
        RecipeLoader.Added(index, CraftingStation, this);
    }

    /// <summary>已经是组对象就原样返回。</summary>
    public static RecipeGroup GetGroup(RecipeGroup group) => group;

    /// <summary>按游戏的组号找（没有就是 null）。</summary>
    public static RecipeGroup GetGroup(int groupId) =>
        RecipeGroup.recipeGroups.TryGetValue(groupId, out var group) ? group : null!;

    /// <summary>按名字找组（自己用 CreateRecipeGroup 建的，或游戏自己的 'IronBar'、'Wood'…）。</summary>
    public static RecipeGroup GetGroup(string name)
    {
        if (string.IsNullOrEmpty(name)) return null!;
        var mine = RecipeLoader.Group(name);
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

        var known = RecipeLoader.Group(name);
        if (known != null) return known;

        itemTypes ??= Array.Empty<int>();
        var group = new RecipeGroup(ModLocalization.Key("RecipeGroups." + name), itemTypes);
        foreach (var type in itemTypes) ItemID.Sets.IsAMaterial[type] = true;

        group.Register();
        Safe.Run("grupo de receita " + name, group.SortDecraftingEntries);
        RecipeLoader.SetGroup(name, group);
        return group;
    }

    /// <summary>堆叠数夹到 1..物品上限（0 = 用 9999）。</summary>
    private static int ClampStack(Item item, int stack) =>
        Math.Max(1, Math.Min(stack, item.maxStack > 0 ? item.maxStack : 9999));

    /// <summary>配方表扩容：旧表的内容搬过去，多的留空。</summary>
    private static Il2CppReferenceArray<Recipe> Grow(Il2CppReferenceArray<Recipe>? recipes, int length)
    {
        var grown = new Il2CppReferenceArray<Recipe>(length);
        if (recipes != null) for (var i = 0; i < recipes.Length && i < length; i++) grown[i] = recipes[i];
        return grown;
    }
}
