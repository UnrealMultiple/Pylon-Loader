using System.Collections.Generic;

namespace Pylon.ModLoader;

/// <summary>自定义发型；一个类型一个实例（创建角色与理发师里选，类型在游戏 228 个之后）。</summary>
public abstract class ModHair : ModType
{
    /// <summary>发型贴图相对 Mod 目录的路径（默认 assets/hairs/&lt;Name&gt;.png）。</summary>
    public override string TexturePath => $"assets/hairs/{Name}.png";

    /// <summary>戴帽子版的贴图路径（默认同目录的 &lt;Name&gt;_Alt.png；只有主贴图时就用主贴图）。</summary>
    public virtual string AltTexture => $"assets/hairs/{Name}_Alt.png";

    /// <summary>创建角色里能不能选（默认能；理发师处走 GetUnlockConditions）。</summary>
    public virtual bool AvailableDuringCharacterCreation => true;

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    public virtual IEnumerable<Condition> GetUnlockConditions() => [];
}

// 未移植 register：占号、装 PlayerHair/PlayerHairAlt 贴图、调 SetStaticDefaults、存档/net 那套都是管线的事。