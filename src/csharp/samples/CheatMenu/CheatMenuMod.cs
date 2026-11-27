using Pylon.ModLoader;
using Pylon.ModLoader.Logging;

namespace CheatMenu;

/// <summary>
/// 作弊菜单入口：注册一个根 ImGui 页，功能实现全部在 <see cref="Features.CheatFeatureHost"/>。
/// 不在这里做任何依赖游戏状态的事——那属于 <see cref="CheatRuntime"/> 的晚阶段。
/// </summary>
public sealed class CheatMenuMod : Mod
{
    internal static CheatMenuMod Instance { get; private set; }

    internal static ModLogger Log { get; private set; }

    public override void Load()
    {
        Instance = this;
        Log = Logger;

        RegisterImGuiTab(new UI.CheatRootTab());
        Logger.Info("[cheat] CheatMenu 已加载：菜单里的「作弊菜单」页");
    }

    public override void Unload()
    {
        Infra.IconStore.OnModUnload();
    }
}