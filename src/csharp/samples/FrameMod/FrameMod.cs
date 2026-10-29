// 屏幕效果专项测试 Mod（FrameMod 已瘦身：只保留着色器测试，去掉全部内容/自检/音乐）。
// 进世界后，屏幕中央围绕玩家显示一个"裂隙"（assets/effects/Rift.frag）。
using System;
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class FrameMod : Mod
    {
        internal static ModLogger Log;
        internal static FrameMod Instance;

        /// <summary>每帧提交（菜单也提交；效果只在进世界后画）。</summary>
        public override void PreUpdateTime()
        {
            TestScreenEffect.Tick(Log);
        }

        public override void Load()
        {
            Instance = this;
            Log = Logger;
            TestScreenEffect.Register(this);
            Logger.Info("[fx] FrameMod 已瘦身为着色器测试（进世界后屏幕中央开裂隙）");
        }

        /// <summary>延迟阶段（游戏内容就绪、游戏线程）：挂 Main.Update（原版之后跑）。</summary>
        public override void SetupContent()
        {
            On.Terraria.Main.Update += AfterUpdate;
            Logger.Info("[fx] 已挂 Main.Update（postfix）");
        }

        private static bool _worldJoined;

        private static void AfterUpdate(On.Terraria.Main.orig_Update orig, Main self,
                                        Il2CppMicrosoft.Xna.Framework.GameTime gameTime)
        {
            orig(self, gameTime);
            if (!_worldJoined && !Main.gameMenu && GameContent.Installed)
            {
                _worldJoined = true;
                Log?.Info("[fx] 进世界了：提交裂隙效果");
            }
        }
    }
}