using System;
using System.IO;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.Content;
using Terraria;

namespace CheatMenu;

/// <summary>
/// 晚阶段装配点：所有依赖游戏状态的初始化（hook、常驻绘制）都在这里发生。
/// 早阶段 <see cref="CheatMenuMod.Load"/> 只注册 UI，不碰游戏对象。
/// </summary>
public sealed class CheatRuntime : ModSystem
{
    public override void PostSetupContent()
    {
        Features.CheatFeatureHost.Install();
        HookDraw();
    }

    public override void PostUpdateEverything()
    {
        Features.CheatFeatureHost.Tick();
    }

    public override bool HijackGetData(ref byte messageType, ref BinaryReader reader, int playerNumber)
    {
        return Features.CheatFeatureHost.OnGetData(ref messageType, ref reader, playerNumber);
    }

    public override void OnModUnload()
    {
        UnhookDraw();
        // 先撤销常驻绘制：回调持有本程序集的对象，卸载后必须断开，否则下次重载会叠加。
        Features.CheatFeature.UninstallAll();
        Features.CheatFeatureHost.Flush(force: true);
    }

    // ==================== 世界内绘制挂点：Main.DoDraw 之后 ====================

    private static bool _drawHooked;

    /// <summary>
    /// Terraria-Imgui 把工具的世界内绘制（剪贴板真贴图预览）挂在 <c>Main.DoDraw</c> **之后**
    /// （Modify.hpp 的 MainDoDrawHOOK）。这时游戏已经画完世界与界面、本帧不会再覆盖，
    /// 且 SpriteBatch 已 End，可以安全地自建一批单位矩阵批次。
    /// Pylon 侧没有 ModSystem 的「DoDraw 之后」时机，所以直接订同一个钩子，时机完全一致。
    /// </summary>
    private static void HookDraw()
    {
        if (_drawHooked) return;
        try
        {
            On.Terraria.Main.DoDraw += AfterDoDraw;
            _drawHooked = true;
            CheatMenuMod.Log?.Info("[cheat] Main.DoDraw 已挂：世界内贴图预览（原版 Modifier 同挂点）");
        }
        catch (Exception e)
        {
            CheatMenuMod.Log?.Warn("[cheat] Main.DoDraw 挂载失败，世界内贴图预览不可用：" + e.Message);
        }
    }

    private static void UnhookDraw()
    {
        if (!_drawHooked) return;
        _drawHooked = false;
        try { On.Terraria.Main.DoDraw -= AfterDoDraw; }
        catch (Exception) { }
    }

    private static void AfterDoDraw(On.Terraria.Main.orig_DoDraw orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        if (!_tilesLogged)
        {
            _tilesLogged = true;
            CheatMenuMod.Log?.Info("[cheat] DoDraw 后置回调首次运行：世界内贴图预览时机已接通");
        }
        try
        {
            Features.CheatFeatureHost.DrawWorldTiles(Main.spriteBatch);
        }
        catch (Exception e)
        {
            CheatMenuMod.Log?.Warn("[cheat] 世界内贴图预览绘制异常：" + e.Message);
        }
    }

    private static bool _tilesLogged;
}