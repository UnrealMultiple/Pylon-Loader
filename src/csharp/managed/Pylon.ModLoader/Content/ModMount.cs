using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>坐骑贴图的分层（Back* 画在玩家后面，Front* 画在前面）。</summary>
public enum MountTextureType
{
    Back,
    BackGlow,
    BackExtra,
    BackExtraGlow,
    Front,
    FrontGlow,
    FrontExtra,
    FrontExtraGlow,
}

/// <summary>自定义坐骑：一个类型一份实例；骑上/每帧/绘制等钩子由 Harmony 层按 `_type` 派发。</summary>
public abstract class ModMount : ModType
{
    /// <summary>相对 ModMount 覆写了哪些钩子：安装时算一次挂在模板上，分派期只做位运算。</summary>
    internal HookMask Hooks { get; set; }

    /// <summary>这个钩子要不要分派给本实例。</summary>
    internal bool HookEnabled(HookId hook) => Hooks.Has(hook);

    /// <summary>这匹坐骑的 Terraria.Mount.MountData（SetStaticDefaults 之前由管线建好，直接改它的字段）。</summary>
    public Mount.MountData MountData { get; internal set; } = null!;

    /// <summary>贴图基础名（无扩展名）：存在的层是它加 _Back/_BackGlow/_BackExtra/_BackExtraGlow/_Front/_FrontGlow/_FrontExtra/_FrontExtraGlow。</summary>
    public override string TexturePath => $"assets/mounts/{Name}";

    /// <summary>某一层贴图的路径（默认在基础名后加层名）。</summary>
    protected virtual string GetExtraTexture(MountTextureType textureType) => TexturePath + "_" + textureType;

    /// <summary>占号之后、所有 MountData 都进 Mount.mounts 之后一次；只放共享数值（改 MountData 的字段、MountID.Sets 等）。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>游戏算完跳跃高度后；jumpHeight 就是这次的高度（可改）。</summary>
    public virtual void JumpHeight(Player mountedPlayer, ref int jumpHeight, float xVelocity) { }

    /// <summary>游戏算完跳跃速度后；jumpSpeed 就是这次的速度（可改）。</summary>
    public virtual void JumpSpeed(Player mountedPlayer, ref float jumpSpeed, float xVelocity) { }

    /// <summary>每帧，玩家骑着的时候（在游戏自己的效果之前）。</summary>
    public virtual void UpdateEffects(Player player) { }

    /// <summary>每帧选帧前；false = 跳过游戏动画（state：0 停 1 走 2 空中 3 飞 4 游 5 冲刺）。</summary>
    public virtual bool UpdateFrame(Player mountedPlayer, int state, Vector2 velocity) => true;

    /// <summary>按能力键时（toggleOn = 这帧按下去了）。</summary>
    public virtual void UseAbility(Player player, Vector2 mousePosition, bool toggleOn) { }

    /// <summary>每帧瞄准（蓄力/瞄准中的能力）；在原版判定之前跑。</summary>
    public virtual void AimAbility(Player player, Vector2 mousePosition) { }

    /// <summary>骑上去时（在游戏尘土之前）；skipDust = true 就跳过游戏那阵尘土。</summary>
    public virtual void SetMount(Player player, ref bool skipDust) { }

    /// <summary>下来时；skipDust = true 就跳过游戏那阵尘土。</summary>
    public virtual void Dismount(Player player, ref bool skipDust) { }

    /// <summary>每层一次（drawType 0 Back、1 BackExtra、2 Front、3 FrontExtra）；false = 这层不画；改了 ref 的值就用新的画。</summary>
    public virtual bool Draw(PlayerDrawSet playerDrawData, int drawType, Player drawPlayer,
        ref Texture2D texture, ref Texture2D glowTexture, ref Vector2 drawPosition, ref Rectangle frame,
        ref Color drawColor, ref Color glowColor, ref float rotation, ref SpriteEffects spriteEffects,
        ref Vector2 drawOrigin, ref float drawScale, float shadow) => true;
}

// 未移植 register 与 MountTextureType：占号与反查走 Pylon 的自动发现 + ModContent；各层贴图探测/上传归 Loader。
