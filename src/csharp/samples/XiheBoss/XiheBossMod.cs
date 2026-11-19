using System;
using System.Linq;
using System.Reflection;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Audio;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipelines;

namespace XiheBoss
{
    public sealed class XiheBossMod : Mod
    {
        internal static XiheBossMod Instance { get; private set; }
        internal static ModLogger Log { get; private set; }

        public override void Load()
        {
            Instance = this;
            Log = Logger;

            XiheFx.Init(this, Logger);
            XiheBigLaserFx.Init(this, Logger);
            XiheSounds.Init(Logger);
            XiheSky.Install();
            Logger.Info($"[fx] shader status prims={(XiheFx.PrimsShader != null)} post={(XiheFx.PostShader != null)} ring={(XiheFx.RingShader != null)} line={(XiheFx.LineShader != null)} bigLaser={(XiheBigLaserFx.Shader != null)}");

            try
            {
                Wand.XiheWandFx.Shader = ScreenGraphics.Shader.FromFile(this, "assets/effects/XiheBeam.frag");
                Logger.Info("[fx] XiheBeam.frag 已注册（激光柱屏幕着色器）");
            }
            catch (Exception e)
            {
                Logger.Error("[fx] XiheBeam.frag 加载失败，激光柱将没有主体画面：" + e.Message);
            }

            RegisterImGuiTab(new XiheBossImGuiTab());

            Logger.Info("[build] 2026-11-20 04:05；羲和 Boss 已加载（复刻 MEAC.Bosses.EoL.XiHe；二合一：含魔杖；绿线修复+MEAC bloom 移植）");
        }
    }

    public sealed class XiheBossSystem : ModSystem
    {
        private static bool _submitFromUpdate;
        private static bool _cameraHookInstalled;

        public override void PostSetupContent()
        {
            SetupContent();
            SetupHooks();

            XiheBossNpc.LoadVisualAssets();

            XiheBossNpc.ResolveMusic(XiheBossMod.Instance);

            Wand.XiheWandFx.Reset();
            Wand.XiheWandFx.BindTextures(XiheBossMod.Instance);
            Wand.XiheWandFx.Probe();
            ModContent.GetInstance<Wand.XiHeMagicItem>()?.ApplyIcon();
            ModContent.GetInstance<XiheSummonItem>()?.ApplyIcon();
        }

        private static void SetupContent()
        {
            XiheFx.Reset();
            XiheVisuals.Clear();
            XiheTextures.Bind(XiheBossMod.Instance, XiheBossMod.Log);

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

            XiheFx.RegisterVisual<Wand.XiheMagicControler>(Wand.XiheWandFx.PushControllerVisual);
            XiheFx.RegisterVisual<Wand.XiheLaserPillar>(static _ => { });
            XiheFx.RegisterVisual<XiheSummonProj>(XiheSummonProj.PushVisual);
            XiheBossMod.Log.Info($"[fx] 弹幕视觉 {XiheFx.VisualCount} 种（EoLProj type={ModContent.ProjectileType<EoLProj>()}、EoLLance type={ModContent.ProjectileType<EoLLance>()}）");
        }

        public override void AddRecipes()
        {
            try
            {
                var recipe = new ModRecipe();
                recipe.SetResult(ModContent.ItemType<Wand.XiHeMagicItem>());
                recipe.AddIngredient(ItemID.Sunfury);
                recipe.AddIngredient(ItemID.FragmentSolar, 20);
                recipe.AddIngredient(ItemID.GoldBar, 10);
                recipe.AddTile(TileID.LunarCraftingStation);
                recipe.Register();
                XiheBossMod.Log.Info("[recipe] 羲和魔杖配方已注册");
            }
            catch (Exception e)
            {
                XiheBossMod.Log.Warn("[recipe] 羲和魔杖配方注册失败：" + e.Message);
            }
        }

        private static void SetupHooks()
        {

            try
            {
                On.Terraria.Main.DoDraw += BeforeDraw;
                XiheBossMod.Log.Info("[fx] Main.DoDraw 已挂（屏幕效果按绘制帧提交）");
            }
            catch (Exception e)
            {
                _submitFromUpdate = true;
                XiheBossMod.Log.Warn("[fx] Main.DoDraw 挂载失败，改为 PostUpdateEverything 提交：" + e.Message);
            }

            try
            {
                On.Terraria.Main.DoDraw_UpdateCameraPosition += AfterCameraUpdate;
                _cameraHookInstalled = true;
                XiheBossMod.Log.Info("[fx] Main.DoDraw_UpdateCameraPosition 已挂：UseScreenPosition 镜头锁生效");
            }
            catch (Exception e) { XiheBossMod.Log.Warn("[fx] 镜头钩子失败：" + e.Message); }

            try { ScreenGraphics.Install(); }
            catch (Exception e) { XiheBossMod.Log.Warn("[fx] 屏幕效果安装失败：" + e.Message); }

            try
            {
                On.Terraria.Main.UpdateAudio_DecideOnNewMusic += AfterDecideOnNewMusic;
                XiheBossMod.Log.Info("[music] Main.UpdateAudio_DecideOnNewMusic 已挂：Boss 存在时压制原版 Boss 曲");
            }
            catch (Exception e) { XiheBossMod.Log.Warn("[music] DecideOnNewMusic 挂载失败：" + e.Message); }
        }

        private static void AfterDecideOnNewMusic(On.Terraria.Main.orig_UpdateAudio_DecideOnNewMusic orig, Main self)
        {
            orig(self);
            try
            {
                if (Main.gameMenu) return;

                if (XiheBossNpc.SpawnCountdown > 0)
                {
                    Main.newMusic = 0;
                    return;
                }
                var boss = XiheBossNpc.Current;
                if (boss == null || !boss.NpcAlive) return;
                if (XiheBossNpc.MusicSlot <= 0) return;
                Main.newMusic = 0;
            }
            catch (Exception e) { XiheBossMod.Log?.Warn("[music] 压原版曲失败：" + e.Message); }
        }

        private static void BeforeDraw(On.Terraria.Main.orig_DoDraw orig, Main self, GameTime gameTime)
        {

            if (!_cameraHookInstalled)
            {
                XiheFx.Submit();
                Wand.XiheWandFx.Submit();
            }
            orig(self, gameTime);
        }

        private static void AfterCameraUpdate(On.Terraria.Main.orig_DoDraw_UpdateCameraPosition orig)
        {
            orig();
            XiheFx.ApplyCamera();
            XiheFx.Submit();
            Wand.XiheWandFx.Submit();
        }

        public override void PostUpdateEverything()
        {
            XiheSky.Tick();
            XiheFx.Tick();
            XiheBossNpc.TickSpawnCountdown();
            Wand.XiheWandFx.Update();
            if (_submitFromUpdate)
            {
                XiheFx.Submit();
                Wand.XiheWandFx.Submit();
            }
        }

        public override void ModifySunLightColor(ref Il2CppMicrosoft.Xna.Framework.Graphics.Color tileColor,
            ref Il2CppMicrosoft.Xna.Framework.Graphics.Color backgroundColor) => XiheSky.Light(ref tileColor, ref backgroundColor);

        public override void OnWorldUnload()
        {
            XiheSky.Reset();
            XiheFx.Reset();
            XiheVisuals.Clear();
            Wand.XiheWandFx.Reset();
            XiheBossNpc.Current = null;
            XiheBossNpc.SpawnCountdown = -1;
        }
    }
}
