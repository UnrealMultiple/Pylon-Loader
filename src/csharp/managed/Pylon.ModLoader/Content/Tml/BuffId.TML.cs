using Pylon.ModLoader.Pipelines;

namespace Pylon.ModLoader.Content;
/// <summary>BuffID.TML.cs 的等价扩展成员：鞭类 tag 增益标记与免疫继承链，底层表由 NpcImmunity 维护。</summary>
public static class BuffIdTml
{
    extension(Terraria.ID.BuffID.Sets)
    {
        /// <summary>这个增益是不是鞭类 tag 增益（原版 310/362 已内置）。</summary>
        public static ref bool[] IsATagBuff => ref NpcImmunity.TagBuffs;

        /// <summary>免疫继承链：GrantImmunityWith[buff] 里的效果免疫时，buff 跟着免疫。</summary>
        public static ref List<int>[] GrantImmunityWith => ref NpcImmunity.ImmunityParents;
    }
}
