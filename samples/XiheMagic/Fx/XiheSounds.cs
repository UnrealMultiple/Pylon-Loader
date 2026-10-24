// 音效：直接用 MEAC 的 Sounds/Customs/Laser.wav（原素材，不再自己合成）。
// 播放/解码由 SoundLoader（Android SoundPool）负责。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;

namespace XiheMagic
{
    internal static class XiheSounds
    {
        private static SoundStyle _laser;

        internal static void Init(ModLogger log)
        {
            try
            {
                // 路径从 Assets/ 开始、不带扩展名；文件在 assets/sounds/Laser.wav。
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
