using System.Text;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.Mod;
using Terraria;

namespace Pylon.ModLoader.UI;

/// <summary>
/// 加载器 UI 的挂载点：游戏里开两个钩子（更新 / 绘制），把工作分派给
/// ImGui 覆盖层（ImGuiOverlay）与 SpriteBatch 面板（ModPanel）。
/// </summary>
public static class ModUI
{
    private static bool _hooked;
    private static ModLogger? _logger;
    private static readonly List<ModPanel> Panels = [];

    public static bool Visible
    {
        get => ImGuiOverlay.Visible;
        set => ImGuiOverlay.Visible = value;
    }

    internal static ModLogger? Logger => _logger;

    /// <summary>晚阶段调用（游戏 UI 就绪后）。</summary>
    internal static void EnsureInitialized(ModLogger logger)
    {
        if (_hooked) return;
        _logger = logger;
        _hooked = true;
        InstallHooks(logger);
        ModConfigMenu.Install(logger, forMainMenu: true);
        TouchButtonHooks.RequestInstall(logger);
    }

    internal static void Register(ModPanel panel)
    {
        if (!Panels.Contains(panel)) Panels.Add(panel);
    }

    /// <summary>Mod 注册 ImGui 菜单 Tab（Mod.RegisterImGuiTab 转到这里，进全局注册表）。</summary>
    internal static void RegisterImGuiTab(IModImGuiTab tab) => ImGuiTabRegistry.Register(tab);

    internal static void Update(GameTime gameTime)
    {
        ModConfigMenu.TryInstallPending();
        TouchButtonHooks.TryInstallPending();
        ImGuiOverlay.Update();
    }

    internal static void Draw(SpriteBatch? spriteBatch, GameTime? gameTime)
    {
        if (!Visible) return;
        foreach (var panel in Panels) panel.Draw(spriteBatch);
    }

    private static void InstallHooks(ModLogger logger)
    {
        On.Terraria.Main.UpdateUIStates += UpdateUIStatesHook;
        logger.Info("UI 驱动已挂：Main.UpdateUIStates(GameTime)");

        // 绘制走界面层管线（和作者层同一条链，位置可调，不再占原版某个绘制点的钩子）。
        InterfaceLayerPipeline.RegisterInternal(
            new GameInterfaceLayer("Pylon: Loader UI", DrawLoaderUi, Terraria.UI.InterfaceScaleType.UI));
        logger.Info("UI 绘制层已登记：Pylon: Loader UI");
    }

    private static bool DrawLoaderUi()
    {
        Draw(Main.spriteBatch, null);
        return true;
    }

    private static void UpdateUIStatesHook(On.Terraria.Main.orig_UpdateUIStates orig, GameTime gameTime)
    {
        orig(gameTime);
        Update(gameTime);
    }

    /// <summary>把异常链压成一行：保留"谁抛的"，不刷堆栈。</summary>
    internal static string Flatten(Exception e)
    {
        var text = new StringBuilder();
        Exception? current = e;
        for (var depth = 0; current != null && depth < 6; depth++, current = current.InnerException)
        {
            if (depth > 0) text.Append(" <- ");
            text.Append(current.GetType().Name).Append(": ").Append(current.Message);
        }
        return text.ToString();
    }
}