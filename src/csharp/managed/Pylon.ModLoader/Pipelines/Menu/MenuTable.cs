namespace Pylon.ModLoader;

/// <summary>菜单主题表：按注册序排列；当前生效的那一个由管线写 <see cref="ModMenu.IsSelected"/>。</summary>
internal static class MenuTable
{
    private static readonly List<ModMenu> Menus = [];

    internal static int Count => Menus.Count;

    internal static IReadOnlyList<ModMenu> All => Menus;

    internal static ModMenu At(int index) => Menus[index];

    /// <summary>收表；幂等。</summary>
    internal static void Rebuild()
    {
        Menus.Clear();
        foreach (var menu in ModContent.GetContent<ModMenu>()) Menus.Add(menu);
    }

    /// <summary>默认主题：最后注册且 <c>IsAvailable</c> 为真的那个；没有就是 null。</summary>
    internal static ModMenu? Default()
    {
        for (var index = Menus.Count - 1; index >= 0; index--)
        {
            var menu = Menus[index];
            if (Available(menu)) return menu;
        }

        return null;
    }

    internal static bool Available(ModMenu menu)
    {
        try
        {
            return menu.IsAvailable;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.IsAvailable 异常：{Safe.Describe(e)}");
            return false;
        }
    }
}
