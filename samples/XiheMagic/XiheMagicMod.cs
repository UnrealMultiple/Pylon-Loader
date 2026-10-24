using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Terraria;
using Terraria.ID;

namespace XiheMagic
{
    public sealed class XiheMagicMod : Mod
    {
        internal static XiheMagicMod Instance { get; private set; }
        internal static ModLogger Log { get; private set; }

        /// <summary>找不到 Main.DoDraw 时退回 PostUpdateEverything 提交（少一帧精度，但不会没效果）。</summary>
        private static bool _submitFromUpdate;

        public override void Load()
        {
            Instance = this;
            Log = Logger;

            try
            {
                XiheFx.Shader = ScreenShader.FromFile(this, "assets/effects/XiheBeam.frag");
                Logger.Info("[fx] XiheBeam.frag 已注册（激光柱屏幕着色器）");
            }
            catch (Exception e)
            {
                Logger.Error("[fx] XiheBeam.frag 加载失败，激光柱将没有主体画面：" + e.Message);
            }

            XiheSounds.Init(Logger);

            // 菜单侧边栏加一页：一键把武器塞进背包，方便试效果。
            RegisterImGuiTab(new XiheImGuiTab());

            Logger.Info("羲和魔杖已加载（复刻 MEAC XiHeMagicItem）");
        }

        public override void SetupContent()
        {
            XiheFx.Reset();
            XiheFx.BindTextures(this);

            // 提交时机：Main.DoDraw 之前。世界已经绘制完、screenPosition 已定稿，
            // 内核在 FireOnPostRender(World) 里执行 World 层效果，界面随后再画（不会被盖住）。
            try
            {
                // 代理把 private 生成为 public，所以 Public|NonPublic 都要带；按名字+1 个参数找。
                var doDraw = typeof(Main)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "DoDraw" && m.GetParameters().Length == 1);

                if (doDraw != null)
                {
                    Harmony.Patch(doDraw, prefix: new HarmonyMethod(typeof(XiheMagicMod).GetMethod(
                        nameof(BeforeDraw), BindingFlags.Static | BindingFlags.NonPublic)));
                    Logger.Info("[fx] Main.DoDraw 已挂（激光柱按绘制帧提交）");
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

            try { ScreenGraphics.Install(); }
            catch (Exception e) { Logger.Warn("[fx] 屏幕效果安装失败：" + e.Message); }

            // 立刻编译一次着色器：错了在日志里就有行号，不用等玩家用武器。
            XiheFx.Probe();
        }

        private static void BeforeDraw() => XiheFx.Submit();

        /// <summary>每帧状态推进（闪白衰减、天幕强度）；没有 DoDraw 钩子时也顺便提交。</summary>
        public override void PostUpdateEverything()
        {
            XiheFx.Update();
            if (_submitFromUpdate) XiheFx.Submit();
        }

        public override void OnWorldUnload() => XiheFx.Reset();

        public override void AddRecipes()
        {
            try
            {
                var recipe = new ModRecipe();
                recipe.SetResult(ModContent.ItemType<XiHeMagicItem>());
                recipe.AddIngredient(ItemID.Sunfury);
                recipe.AddIngredient(ItemID.FragmentSolar, 20);
                recipe.AddIngredient(ItemID.GoldBar, 10);
                recipe.AddTile(TileID.LunarCraftingStation);
                recipe.Register();
                Logger.Info("[recipe] 羲和魔杖配方已注册");
            }
            catch (Exception e)
            {
                Logger.Warn("[recipe] 羲和魔杖配方注册失败：" + e.Message);
            }
        }
    }
}
