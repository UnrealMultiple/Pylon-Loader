namespace Pylon.ModLoader;

/// <summary>GlobalRecipe 的注册表与分派：配方没有实体，钩子自己按 Recipe 挑。</summary>
internal static class GlobalRecipeRegistry
{
    private static readonly GlobalRegistry<GlobalRecipe> Registry = new();

    internal static void Register(GlobalRecipe global) => Registry.Register(global);

    internal static IReadOnlyList<GlobalRecipe> All => Registry.All;

    internal static List<GlobalRecipe> For(HookId hook) => Registry.For(hook);
}
