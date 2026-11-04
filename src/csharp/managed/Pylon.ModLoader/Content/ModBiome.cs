using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>自定义生物群落（场景效果的一种），像 tModLoader：IsBiomeActive 每帧说玩家在不在里面，进/出与在里面各有钩子，默认 BiomeLow 优先级与静音。</summary>
public abstract class ModBiome : ModSceneEffect
{
    /// <summary>每帧一次（游戏数完周围方块之后）；玩家在不在这个群落里，这一帧抛异常算 false。</summary>
    public virtual bool IsBiomeActive(Player player) => false;

    /// <summary>进群落时一次（刚进去那帧；同帧还会调 OnInBiome）。</summary>
    public virtual void OnEnter(Player player) { }

    /// <summary>在里面的每帧（进去的那帧也算）。</summary>
    public virtual void OnInBiome(Player player) { }

    /// <summary>出群落时一次（带着它退出世界时也补一次）。</summary>
    public virtual void OnLeave(Player player) { }

    /// <summary>赢的时候放的音乐；0 = 静音（默认），-1 = 用游戏的。</summary>
    public override int Music => 0;

    /// <summary>优先级 BiomeLow = 1；都激活时高的抢到音乐/背景/水。</summary>
    public override int Priority => 1;

    /// <summary>图鉴头像背景色；null = 不换（用游戏的）。</summary>
    public virtual Color? BackgroundColor { get; set; }

    /// <summary>本帧玩家在不在这个群落里（生物群系管线每次场景扫描更新一次）。</summary>
    public bool IsActive(Player player) => BiomePipeline.Instance.IsActive(player, this);

    /// <summary>场景效果管线问的就是这句：群落标记本身就是"这个效果激不激活"。</summary>
    public override bool IsSceneEffectActive(Player player) => IsActive(player);
}

// 未移植 BestiaryIcon/BackgroundPath、DisplayName/TownNPCDialogueName：值由注册管线按类名贴图路径与本地化管线给，
// Pylon 尚无取"类内容贴图路径"的设施；player.InModBiome(...) 用 ModBiome.IsActive(player) 代替。
