using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Pylon.ModLoader.Audio;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using SoundStyle = Pylon.ModLoader.Audio.SoundStyle;

namespace XiheBoss
{
    internal static class XiheSounds
    {
        private static SoundStyle _laser;

        internal static void Init(ModLogger log)
        {
            try
            {

                _laser = new SoundStyle("Sounds/Laser")
                {
                    Volume = 1.5f,
                    MaxInstances = 2,
                    SoundLimitBehavior = 1,
                };
                log?.Info("[sound] MEAC Laser.wav 已登记");
            }
            catch (Exception e)
            {
                log?.Warn("[sound] Laser.wav 登记失败：" + e);
            }
        }

        internal static void PlayLaser(Vector2 position)
        {
            if (_laser != null) SoundEngine.PlaySound(_laser, position);
        }
    }
}
