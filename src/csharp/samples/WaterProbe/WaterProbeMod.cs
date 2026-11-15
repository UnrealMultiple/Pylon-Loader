using Pylon.ModLoader;

namespace WaterProbe
{
    public sealed class WaterProbeMod : Mod
    {
        public override void Load() => Logger.Info("[waterprobe] WaterProbe 已加载");
    }

    /// <summary>
    /// 只用来让 WaterTable 多一个样式，使 WaterPipeline.Build 里的
    /// DrawWatersLimits.Build 真正走到两次 PatchLoopEnd 写入。
    /// </summary>
    public sealed class WaterProbeStyle : ModWaterStyle
    {
        private static bool _logged;

        public override string TexturePath => "assets/water/WaterProbe.png";

        public override void SetStaticDefaults()
        {
            if (_logged) return;
            _logged = true;
            Mod.Logger.Info($"[waterprobe] 水样式已发号 type={Type}");
        }
    }
}
