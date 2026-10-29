using System;
using System.Collections.Generic;

namespace Pylon.ModLoader
{
    // 顺序同 tModLoader：源伤害、加成、防御、暴击、最终，算完交给游戏 StrikeNPC；AddableFloat/MultipliableFloat 在这边就是普通数字。
    /// <summary>打在 NPC 身上的这一下（ModifyHit*、ModifyIncomingHit 收到的修改器）；改字段 = 改这次伤害。</summary>
    public class HitModifiers
    {
        /// <summary>基数伤害（旧名字 damage 读写它）。</summary>
        private float _baseDamage;

        /// <summary>基数击退（旧名字 knockBack 读写它）。</summary>
        private float _baseKnockback;

        /// <summary>基数暴击（旧名字 crit 没覆盖时读它）。</summary>
        private bool _baseCrit;

        /// <summary>显式设过的暴击（null = 跟着基数，SetCrit/DisableCrit/旧名字 crit 写它）。</summary>
        private bool? _crit;

        /// <summary>伤害上限（JS 的 Infinity 用 float.PositiveInfinity 表示）。</summary>
        private float _limit = float.PositiveInfinity;

        /// <summary>DisableKnockback() 之后为 true。</summary>
        private bool _noKnockback;

        /// <summary>SetInstantKill() 之后为 true。</summary>
        private bool _instantKill;

        /// <summary>HideCombatText() 之后为 true。</summary>
        private bool _hideText;

        /// <summary>这一下：默认伤害 0、无上限、防御进 Defense.Base。</summary>
        public HitModifiers(float baseDamage = 0, float baseKnockback = 0, int hitDirection = 0, bool crit = false, float defense = 0, DamageClass damageType = null)
        {
            _baseDamage = baseDamage;
            _baseKnockback = baseKnockback;
            _baseCrit = crit;
            DamageType = damageType;
            HitDirection = hitDirection;
            SuperArmor = false;
            SourceDamage = new StatModifier();
            FlatBonusDamage = 0f;
            ScalingBonusDamage = 0f;
            TargetDamageMultiplier = 1f;
            Defense = new StatModifier(1f, 1f, 0f, defense);
            ArmorPenetration = 0f;
            ScalingArmorPenetration = 0f;
            DefenseEffectiveness = 0.5f;
            CritDamage = new StatModifier(2f, 1f);
            NonCritDamage = new StatModifier();
            FinalDamage = new StatModifier();
            Knockback = new StatModifier();
            HitDirectionOverride = null;
        }

        /// <summary>这次的伤害职业（没给就是 null）。</summary>
        public DamageClass DamageType { get; set; }

        /// <summary>原始受击方向（-1 左 / 1 右；旧名字 hitDirection 没覆盖时读它）。</summary>
        public int HitDirection { get; set; }

        /// <summary>霸体：不看防御，伤害固定为暴击倍率（或 1），上限 4。</summary>
        public bool SuperArmor { get; set; }

        /// <summary>防御前的伤害修正（基础伤害套它）。</summary>
        public StatModifier SourceDamage { get; set; }

        /// <summary>直接加的平值伤害（防御前）。</summary>
        public float FlatBonusDamage { get; set; }

        /// <summary>按当前伤害的比例加成（防御前，乘在伤害上）。</summary>
        public float ScalingBonusDamage { get; set; }

        /// <summary>防御前的总倍率（默认 1）。</summary>
        public float TargetDamageMultiplier { get; set; }

        /// <summary>这次的防御（Base 装着原始防御，加平值还能再改）。</summary>
        public StatModifier Defense { get; set; }

        /// <summary>直接穿透的防御（数值型，加在比例穿透之后）。</summary>
        public float ArmorPenetration { get; set; }

        /// <summary>按防御比例的穿透（先夹到 0..1 再乘防御）。</summary>
        public float ScalingArmorPenetration { get; set; }

        /// <summary>防御生效的比例（默认 0.5，和游戏一致）。</summary>
        public float DefenseEffectiveness { get; set; }

        /// <summary>暴击时的伤害修正（默认 ×2）。</summary>
        public StatModifier CritDamage { get; set; }

        /// <summary>非暴击时的伤害修正（默认无）。</summary>
        public StatModifier NonCritDamage { get; set; }

        /// <summary>防御和暴击之后的最终修正。</summary>
        public StatModifier FinalDamage { get; set; }

        /// <summary>这一下的击退修正。</summary>
        public StatModifier Knockback { get; set; }

        /// <summary>覆盖的受击方向（null = 用 HitDirection；旧名字 hitDirection 写的就是它）。</summary>
        public int? HitDirectionOverride { get; set; }

        /// <summary>把这一下的伤害上限往下压（和现有的取更小，最少 1）。</summary>
        public void SetMaxDamage(int limit)
        {
            _limit = Math.Min(_limit, Math.Max(limit, 1));
        }

        /// <summary>这一下不能暴击。</summary>
        public void DisableCrit() { _crit = false; }

        /// <summary>这一下强制暴击（没反悔过才生效）。</summary>
        public void SetCrit() { if (_crit == null) _crit = true; }

        /// <summary>这一下没有击退。</summary>
        public void DisableKnockback() { _noKnockback = true; }

        /// <summary>这一下直接秒杀（伤害按 1 算）。</summary>
        public void SetInstantKill() { _instantKill = true; }

        /// <summary>不弹伤害数字。</summary>
        public void HideCombatText() { _hideText = true; }

        /// <summary>旧名字：基数伤害（SourceDamage 之前）。</summary>
        public float damage { get => _baseDamage; set => _baseDamage = value; }

        /// <summary>旧名字：基数击退（Knockback 套它）。</summary>
        public float knockBack { get => _baseKnockback; set => _baseKnockback = value; }

        /// <summary>旧名字：受击方向（读 = HitDirectionOverride 优先，写 = 设 HitDirectionOverride）。</summary>
        public int hitDirection { get => HitDirectionOverride ?? HitDirection; set => HitDirectionOverride = value; }

        /// <summary>旧名字：这次是不是暴击（读 = 显式值或基数，写 = 显式覆盖）。</summary>
        public bool crit { get => _crit ?? _baseCrit; set => _crit = value; }

        /// <summary>算这一下的伤害：源伤害 + 加成，扣防御后乘 Crit/NonCrit，最后套 FinalDamage 并夹 1..上限（霸体走独立公式）。</summary>
        public int GetDamage(float baseDamage, bool crit)
        {
            crit = _crit ?? crit;
            if (SuperArmor)
            {
                var damage = crit ? CritDamage.Additive * CritDamage.Multiplicative : 1f;
                return (int)Math.Max(1f, Math.Min(Math.Truncate(damage), Math.Min(_limit, 4f)));
            }
            var value = SourceDamage.ApplyTo(baseDamage);
            value += FlatBonusDamage + ScalingBonusDamage * value;
            value *= TargetDamageMultiplier;
            var defense = Math.Max(Defense.ApplyTo(0f), 0f);
            var penetration = defense * Math.Min(Math.Max(ScalingArmorPenetration, 0f), 1f) + ArmorPenetration;
            defense = Math.Max(defense - penetration, 0f);
            value = Math.Max(value - defense * DefenseEffectiveness, 1f);
            value = (crit ? CritDamage : NonCritDamage).ApplyTo(value);
            return (int)Math.Max(1f, Math.Min(Math.Truncate(FinalDamage.ApplyTo(value)), _limit));
        }

        /// <summary>算这一下的击退（禁了就是 0，不然 Knockback 套完夹到不低于 0）。</summary>
        public float GetKnockback(float baseKnockback)
        {
            return _noKnockback ? 0f : Math.Max(Knockback.ApplyTo(baseKnockback), 0f);
        }

        /// <summary>包装成 HitInfo 给 OnHit* 收（不传参数 = 用构造时的基数）。</summary>
        public HitInfo ToHitInfo() => ToHitInfo(_baseDamage, _baseCrit, _baseKnockback);

        /// <summary>按给定的基数包装成 HitInfo；秒杀伤害算 1，非有限值按 0。</summary>
        public HitInfo ToHitInfo(float baseDamage, bool crit, float baseKnockback)
        {
            var sourceDamage = SourceDamage.ApplyTo(baseDamage);
            var damage = GetDamage(baseDamage, crit);
            var knockback = GetKnockback(baseKnockback);
            return new HitInfo(
                DamageType,
                Math.Max((int)Math.Truncate(IsFinite(sourceDamage) ? sourceDamage : 0f), 1),
                _instantKill ? 1 : (IsFinite(damage) ? damage : 0),
                _crit ?? crit,
                hitDirection,
                IsFinite(knockback) ? knockback : 0f,
                _instantKill,
                _hideText);
        }

        /// <summary>Number.isFinite 的对应：不是 NaN 也不是无穷。</summary>
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}