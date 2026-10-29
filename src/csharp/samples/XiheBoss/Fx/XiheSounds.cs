// 音效：MEAC/Sounds/Customs/Laser.wav（Boss 的光柱点火音），其余全部用原版 SoundID。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;

namespace XiheBoss
{
    internal static class XiheSounds
    {
        private static SoundStyle _laser;

        internal static void Init(ModLogger log)
        {
            try
            {
                _laser = new SoundStyle("Sounds/Laser", new SoundStyle.Options
                {
                    Volume = 1f,
                    MaxInstances = 2,
                    SoundLimitBehavior = 1,
                });
                log?.Info("[sound] MEAC Laser.wav 已登记");
            }
            catch (Exception e)
            {
                log?.Warn("[sound] Laser.wav 登记失败：" + e.Message);
            }
        }

        internal static void PlayLaser(Vector2 position)
        {
            if (_laser != null) SoundEngine.PlaySound(_laser, position);
        }
    }
}
