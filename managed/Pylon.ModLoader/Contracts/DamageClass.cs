using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>自定义伤害职业：决定它继承谁的加成、算作哪个职业、收哪些词缀；原版类取 DamageClass.Melee…，Mod 类被自动注册。</summary>
    public abstract class DamageClass : ModType
    {
        /// <summary>无职业：不吃任何加成（陷阱伤害、泼水）；原版实例由管线造好再填进来。</summary>
        public static DamageClass Default { get; internal set; }

        /// <summary>所有职业的公共基底："+X% 全伤害" 进这里，继承它的都吃到。</summary>
        public static DamageClass Generic { get; internal set; }

        /// <summary>近战。</summary>
        public static DamageClass Melee { get; internal set; }

        /// <summary>不吃攻速的近战（矛）。</summary>
        public static DamageClass MeleeNoSpeed { get; internal set; }

        /// <summary>远程。</summary>
        public static DamageClass Ranged { get; internal set; }

        /// <summary>魔法。</summary>
        public static DamageClass Magic { get; internal set; }

        /// <summary>召唤（小兵、哨兵）：无暴击。</summary>
        public static DamageClass Summon { get; internal set; }

        /// <summary>鞭子：吃近战攻速的召唤。</summary>
        public static DamageClass SummonMeleeSpeed { get; internal set; }

        /// <summary>同时算魔法和召唤。</summary>
        public static DamageClass MagicSummonHybrid { get; internal set; }

        /// <summary>投掷：原版没有物品用它，给 Mod 之间共享。</summary>
        public static DamageClass Throwing { get; internal set; }

        /// <summary>tooltip 里职业的名字（空 = 用本地化键或拆开的类名）。</summary>
        public virtual string DisplayName { get; set; } = "";

        // GetModifierInheritance 故意没翻：它收/返回的 StatInheritanceData 是本版游戏没有的自造类型，继承语义等管线定形。

        /// <summary>true：算作 damageClass，吃它的游戏效果（岩浆石、幽魂套）。</summary>
        public virtual bool GetEffectInheritance(DamageClass damageClass) => false;

        /// <summary>true：收 damageClass 的词缀；默认跟效果继承一样。</summary>
        public virtual bool GetPrefixInheritance(DamageClass damageClass) => GetEffectInheritance(damageClass);

        /// <summary>占号之后一次；只放共享数值，不要碰游戏对象。</summary>
        public override void SetStaticDefaults() { }

        /// <summary>每帧 ResetEffects 后、非原版类跑；这个职业必有的加成（player.GetDamage(this) 之类）。</summary>
        public virtual void SetDefaultStats(Player player) { }

        /// <summary>false：这个职业不算暴击（tooltip 也不出暴击行）。</summary>
        public virtual bool UseStandardCritCalcs => true;

        /// <summary>false：藏起 tooltip 的这行（lineName 是 'Damage'、'CritChance'、'Speed' 或 'Knockback'）。</summary>
        public virtual bool ShowStatTooltipLine(Player player, string lineName) => true;

        /// <summary>问这个职业算不算 damageClass（等于它，或效果继承它）；Resolve 与缓存归管线。</summary>
        public bool CountsAsClass(DamageClass damageClass) => this == damageClass || GetEffectInheritance(damageClass);

        /// <summary>问这个职业收不收 damageClass 的词缀（等于它，或词缀继承它）；Resolve 与 Safe.Run 归管线。</summary>
        public bool GetsPrefixesFor(DamageClass damageClass) => this == damageClass || GetPrefixInheritance(damageClass);

        // static register 故意没翻：JS 的手动注册入口；Pylon 的 ModType 派生类由管线自动注册，Mod 侧不调用。
    }
}
