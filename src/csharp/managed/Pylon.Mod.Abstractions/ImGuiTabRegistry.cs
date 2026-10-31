namespace Pylon.Mod;

/// <summary>全局 ImGui 菜单页注册表；框架每帧遍历 <see cref="All"/>。</summary>
public static class ImGuiTabRegistry
{
    private static readonly List<IModImGuiTab> Tabs = [];

    /// <summary>已注册的页，按注册顺序。注册只发生在启动阶段，读路径不加锁。</summary>
    public static IReadOnlyList<IModImGuiTab> All => Tabs;

    /// <summary>注册一页；重复注册同一实例会被忽略。</summary>
    /// <returns>真的加入时返回 true。</returns>
    public static bool Register(IModImGuiTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (Tabs.Contains(tab)) return false;
        Tabs.Add(tab);
        return true;
    }
}
