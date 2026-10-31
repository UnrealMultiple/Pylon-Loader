using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>自定义场景效果，像 tModLoader：对玩家激活时，按 Priority + GetWeight 抢音乐、表层/地下背景、水和全屏地图背景。</summary>
public abstract class ModSceneEffect : ModType
{
    /// <summary>0 到 1：同 Priority 的效果谁赢（大者优先）。</summary>
    public virtual float GetWeight(Player player) => 0.5f;

    /// <summary>这帧这个效果是否激活。</summary>
    public virtual bool IsSceneEffectActive(Player player) => false;

    /// <summary>全屏地图背景的颜色（传入的默认值：表层是天空色，地下是白；配 MapBackgroundFullbright 也是白）。</summary>
    public virtual void MapBackgroundColor(ref Color color) { }

    /// <summary>每帧，激活与否都调；开关屏幕滤镜放这里。</summary>
    public virtual void SpecialVisuals(Player player, bool isActive) { }

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>赢的时候放的音乐；-1 = 不选（用游戏的），0 = 静音。</summary>
    public virtual int Music => -1;

    /// <summary>抢到这个场景时用的表层背景风格（ModSurfaceBackgroundStyle 实例）；null = 不换。</summary>
    public virtual ModSurfaceBackgroundStyle SurfaceBackgroundStyle => null!;

    /// <summary>抢到时用的地下背景风格（ModUndergroundBackgroundStyle 实例）；null = 不换。</summary>
    public virtual ModUndergroundBackgroundStyle UndergroundBackgroundStyle => null!;

    /// <summary>抢到时用的水样式（ModWaterStyle 实例）；null = 不换。</summary>
    public virtual ModWaterStyle WaterStyle => null!;

    /// <summary>全屏地图背景的贴图路径；null = 不换。</summary>
    public virtual string MapBackground => null!;

    /// <summary>地图背景画成全亮（白）。</summary>
    public virtual bool MapBackgroundFullbright => false;

    /// <summary>优先级（原版 SceneEffectPriority.None = 0）；都激活时高的抢到音乐/背景/水。</summary>
    public virtual int Priority => 0;
}
