using System.Collections.Generic;

namespace Pylon.ModLoader
{
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

        /// <summary>理发师处的解锁条件（Pylon.Condition 的 IsMet 就是参考里的判定）；全满足才算解锁，默认没有条件。</summary>
        public virtual List<Condition> GetUnlockConditions() => new List<Condition>();

        /// <summary>创建角色时看 AvailableDuringCharacterCreation；理发师处看 GetUnlockConditions 是否全满足。Mod 可以直接改写这个方法，自己定一切。</summary>
        public virtual bool IsUnlocked(bool isAtCharacterCreation, bool isAtStylist)
        {
            if (isAtCharacterCreation) return AvailableDuringCharacterCreation;
            var conditions = GetUnlockConditions() ?? new List<Condition>();
            foreach (var condition in conditions)
            {
                if (!condition.IsMet()) return false;
            }
            return true;
        }

        // register 没翻：占号、装 PlayerHair/PlayerHairAlt 贴图、调 SetStaticDefaults、存档/net 那套都是管线的事
    }
}