namespace Pylon.ModLoader;

/// <summary>内容就绪的阶段。枚举顺序即执行顺序。</summary>
internal enum ReadyPhase
{
    /// <summary>数组扩容回调。</summary>
    Resize,

    /// <summary>被"别人请求先就绪"的内容（如 MountData）。</summary>
    Setup,

    /// <summary>AddRecipeGroups：配方可以引用别的 Mod 的组，所以排在最前。</summary>
    Groups,

    /// <summary>Mod.PostSetupContent，按 tModLoader 语义在内容注册之前。</summary>
    Mods,

    /// <summary>各 Loader 的内容注册。</summary>
    Content,

    /// <summary>AddRecipes。</summary>
    Recipes,

    /// <summary>PostAddRecipes。</summary>
    PostRecipes,

    /// <summary>最后一个可运行点（PostSetupRecipes）。</summary>
    Finish,
}
