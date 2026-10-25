// 羲和 Boss Mod 入口：着色器/贴图/音效注册、屏幕效果提交、相机钩子、菜单页。
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Terraria;
using Terraria.ID;

namespace XiheBoss
{
    public sealed class XiheBossMod : Mod
    {
        internal static XiheBossMod Instance { get; private set; }
        internal static ModLogger Log { get; private set; }

        private static bool _submitFromUpdate;
        private static bool _cameraHookInstalled;

        public override void Load()
        {
            Instance = this;
            Log = Logger;

            XiheFx.Init(this, Logger);
            XiheBigLaserFx.Init(this, Logger);
            XiheSounds.Init(Logger);
            RegisterImGuiTab(new XiheBossImGuiTab());

            Logger.Info("[build] 2026-10-25 23:10；羲和 Boss 已加载（复刻 MEAC.Bosses.EoL.XiHe）");
        }

        public override void SetupContent()
        {
            XiheFx.Reset();
            XiheVisuals.Clear();
            XiheTextures.Bind(this, Logger);

            // 弹幕视觉：每种弹幕只把自己的状态喂给屏幕效果，不自己画。
            XiheFx.RegisterVisual<EoLLance>(EoLLance.PushVisual);
            XiheFx.RegisterVisual<EoLProj>(EoLProj.PushVisual);
            XiheFx.RegisterVisual<EoLLaser1>(EoLLaser1.PushVisual);
            XiheFx.RegisterVisual<EoLBigLaser>(EoLBigLaser.PushVisual);
            XiheFx.RegisterVisual<CrystalArrow>(CrystalArrow.PushVisual);
            XiheFx.RegisterVisual<Cycloid>(Cycloid.PushVisual);
            XiheFx.RegisterVisual<EoLLight>(EoLLight.PushVisual);
            XiheFx.RegisterVisual<EoLFire>(EoLFire.PushVisual);
            XiheFx.RegisterVisual<SunProj>(SunProj.PushVisual);
            XiheFx.RegisterVisual<SolarProminence>(SolarProminence.PushVisual);
            Logger.Info($"[fx] 弹幕视觉 {XiheFx.VisualCount} 种（EoLProj type={ModContent.ProjectileType<EoLProj>()}、EoLLance type={ModContent.ProjectileType<EoLLance>()}）");

            // 提交时机：Main.DoDraw 之前（世界画完、screenPosition 定稿，界面还没画）。
            try
            {
                var doDraw = typeof(Main)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "DoDraw" && m.GetParameters().Length == 1);

                if (doDraw != null)
                {
                    Harmony.Patch(doDraw, prefix: new HarmonyMethod(typeof(XiheBossMod).GetMethod(
                        nameof(BeforeDraw), BindingFlags.Static | BindingFlags.NonPublic)));
                    Logger.Info("[fx] Main.DoDraw 已挂（屏幕效果按绘制帧提交）");
                }
                else
                {
                    _submitFromUpdate = true;
                    Logger.Warn("[fx] 找不到 Main.DoDraw，改为 PostUpdateEverything 提交");
                }
            }
            catch (Exception e)
            {
                _submitFromUpdate = true;
                Logger.Warn("[fx] Main.DoDraw 挂载失败，改为 PostUpdateEverything 提交：" + e.Message);
            }

            // 镜头锁：MEAC 在 ModPlayer.ModifyScreenPosition（原版相机算完之后）改 Main.screenPosition。
            // Pylon 没有这个回调，直接挂原版的 DoDraw_UpdateCameraPosition（static/private/无参），
            // 在它算完之后应用 XiheFx 的 UseScreenPosition，否则 sp/放光球时的锁镜头不会生效。
            try
            {
                var methods = typeof(Main).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var updateCam = methods.FirstOrDefault(m =>
                    (m.Name == "DoDraw_UpdateCameraPosition" || m.Name == "ClampScreenPositionToWorld")
                    && m.GetParameters().Length == 0);
                if (updateCam != null)
                {
                    Harmony.Patch(updateCam, postfix: new HarmonyMethod(typeof(XiheBossMod).GetMethod(
                        nameof(AfterCameraUpdate), BindingFlags.Static | BindingFlags.NonPublic)));
                    _cameraHookInstalled = true;
                    Logger.Info($"[fx] Main.{updateCam.Name} 已挂：UseScreenPosition 镜头锁生效");
                }
                else Logger.Warn("[fx] 找不到相机更新方法，镜头锁不可用");
            }
            catch (Exception e) { Logger.Warn("[fx] 镜头钩子失败：" + e.Message); }

            try { ScreenGraphics.Install(); }
            catch (Exception e) { Logger.Warn("[fx] 屏幕效果安装失败：" + e.Message); }

            // MEAC 是在 Main.UpdateAudio_DecideOnNewMusic 里接管 Boss 曲。手机版原版仍会按
            // npc.boss 选 Boss1/Boss2；必须在“选曲决定之后、真正播放之前”把 newMusic 压成 0，
            // 而不是在 UpdateAudio 结束后停 Cue（那样下一帧又会被选回来）。
            try
            {
                var decide = typeof(Main)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "UpdateAudio_DecideOnNewMusic" && m.GetParameters().Length == 0);
                if (decide != null)
                {
                    Harmony.Patch(decide, postfix: new HarmonyMethod(typeof(XiheBossMod).GetMethod(
                        nameof(AfterDecideOnNewMusic), BindingFlags.Static | BindingFlags.NonPublic)));
                    Logger.Info("[music] Main.UpdateAudio_DecideOnNewMusic 已挂：Boss 存在时压制原版 Boss 曲");
                }
                else
                {
                    Logger.Warn("[music] 找不到 Main.UpdateAudio_DecideOnNewMusic");
                }
            }
            catch (Exception e) { Logger.Warn("[music] DecideOnNewMusic 挂载失败：" + e.Message); }
        }

        /// <summary>
        /// 原版 UpdateAudio 在选出 newMusic 后调用它；这里把 newMusic 改成 0，让原版曲目按正常
        /// 淡出逻辑停下。Mod 曲（TakeFlight）由 AndroidAudio/ModMusic 独立播放。
        /// </summary>
        private static void AfterDecideOnNewMusic()
        {
            try
            {
                if (Main.gameMenu) return;
                var boss = XiheBossNpc.Current;
                if (boss == null || !boss.NpcAlive) return;
                if (XiheBossNpc.MusicSlot <= 0) return;
                Main.newMusic = 0;
            }
            catch (Exception e) { Log?.Warn("[music] 压原版曲失败：" + e.Message); }
        }

        public override void PostSetupContent()
        {
            // 音乐注册放到内容就绪之后：音频引擎这时才稳定（失败也只是没 BGM）。
            XiheBossNpc.ResolveMusic(this);
        }

        private static void BeforeDraw()
        {
            // 相机钩子装上了就等 DoDraw_UpdateCameraPosition 之后提交（那时 screenPosition 才是最终值），
            // 否则镜头锁会把世界画面移走、而图元仍按旧 screenPosition 画。
            if (!_cameraHookInstalled) XiheFx.Submit();
        }

        /// <summary>原版相机算完之后再应用镜头锁，并按最终 screenPosition 提交本帧图元。</summary>
        private static void AfterCameraUpdate()
        {
            XiheFx.ApplyCamera();
            XiheFx.Submit();
        }

        public override void PostUpdateEverything()
        {
            XiheFx.Tick();
            if (_submitFromUpdate) XiheFx.Submit();
        }

        public override void OnWorldUnload()
        {
            XiheFx.Reset();
            XiheVisuals.Clear();
            XiheBossNpc.Current = null;
        }

    }
}
