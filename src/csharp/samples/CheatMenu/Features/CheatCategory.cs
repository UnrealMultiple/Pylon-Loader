namespace CheatMenu.Features;

/// <summary>
/// 与原版 Terraria-Imgui 的 <c>MenuTab</c> 一一对应（只保留有工具、会显示的页）。
/// 顺序就是原版侧栏顺序；没有功能的分类自动隐藏，和原版「空 Tab 省略」一致。
/// </summary>
public enum CheatCategory
{
    Main,
    World,
    Physical,
    Item,
    Misc,
    Network,
    Settings,
    Visual,
    Inspector,
}