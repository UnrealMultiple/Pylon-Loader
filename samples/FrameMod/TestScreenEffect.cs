// 屏幕效果示例：进世界后，在屏幕中央开一个"裂隙"（Rift.frag）。
// 展示新 API 的用法：ScreenShader.FromFile + 每帧 ScreenFrame.Submit。
// 事件（菜单/世界）时不同步提交；没有 submit 的帧不画。
using System;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Terraria;
using UnityEngine;

namespace FrameMod
{
    internal static class TestScreenEffect
    {
        private static ScreenShader _shader;
        private static bool _registered;

        internal static void Register(Mod mod)
        {
            try
            {
                _shader = ScreenShader.FromFile(mod, "assets/effects/Rift.frag");
                _registered = true;
                mod.Logger.Info("[fx] 屏幕效果示例已注册（Rift.frag；进世界后屏幕中央开裂隙）");
            }
            catch (Exception e)
            {
                mod.Logger.Warn($"[fx] 屏幕效果示例注册失败：{e.Message}");
            }
        }

        /// <summary>每帧提交（FrameMod.AfterUpdate 里调；只进世界后）。</summary>
        internal static void Tick(ModLogger log)
        {
            if (!_registered || Main.gameMenu || !GameContent.Installed) return;
            var player = Main.LocalPlayer;
            if (player == null || !player.active) return;

            // 效果坐标是相机 viewport 的分数（v 从下往上）：玩家世界坐标 → 屏幕分数。
            var rel = player.Center - Main.screenPosition;
            var center = new Vector2(
                Mathf.Clamp01(rel.X / Main.screenWidth),
                Mathf.Clamp01(1f - rel.Y / Main.screenHeight));

            var frame = new ScreenFrame
            {
                // History：效果可以读上一帧世界（ScreenTexture.History）。Rift 用不到，示例展示开启。
                History = true,
            };
            frame.Add(new ScreenEffect(_shader)
            {
                Layer = ScreenEffectLayer.World,   // 界面之下：UI 保持清晰
                Center = center,
                Radius = 0.35f,                    // 高度的 35%
                Intensity = 1.2f,
                Color = new Color(0.4f, 0.9f, 1f, 1f),
            }.Uniform("uWarp", 0.06f));
            frame.Submit();
        }
    }
}
