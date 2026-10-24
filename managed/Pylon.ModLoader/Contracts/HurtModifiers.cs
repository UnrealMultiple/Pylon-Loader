using System;
using System.Collections.Generic;

namespace Pylon.ModLoader
{
    // 顺序：SourceDamage/IncomingDamageMultiplier 在防御前，ArmorPenetration 扣防御，FinalDamage/SetMaxDamage 在防御后；本版没有可变玩家击退，Knockback 只留读。
    /// <summary>打在玩家身上的这一下（ModifyHurt、ModifyHitByNPC、ModifyHitPlayer... 收到的修改器）；改字段 = 改这次伤害。</summary>
    public class HurtModifiers
    {
        /// <summary>基数伤害（旧名字 damage 读写它）。</summary>
        private int _damage;

        /// <summary>基数安静（旧名字 quiet 读写它）。</summary>
        private bool _quiet;

        /// <summary>基数暴击（旧名字 crit 读写它）。</summary>
        private bool _crit;

        /// <summary>伤害上限（JS 的 Infinity 用 float.PositiveInfinity 表示）。</summary>
        private float _limit = float.PositiveInfinity;

        /// <summary>Cancel() 之后为 true。</summary>
        private bool _cancelled;

        /// <summary>DisableDust() 之后为 true。</summary>
        private bool _dust;

        /// <summary>DisableSound() 之后为 true。</summary>
        private bool _sound;

        /// <summary>抓住要打在玩家身上的这一下；默认值照参考：伤害 0、可闪避、没有上限。</summary>
        public HurtModifiers(object source = null, int damage = 0, int hitDirection = 0, bool pvp = false, int cooldownCounter = -1, bool dodgeable = true, bool quiet = false, bool crit = false)
        {
            _damage = damage;
            _quiet = quiet;
            _crit = crit;
            DamageSource = source;
            PvP = pvp;
            CooldownCounter = cooldownCounter;
            Dodgeable = dodgeable;
            HitDirection = hitDirection;
            SourceDamage = new StatModifier();
            IncomingDamageMultiplier = 1f;
            FinalDamage = new StatModifier();
            ArmorPenetration = 0f;
            ScalingArmorPenetration = 0f;
            Knockback = new StatModifier();
            KnockbackImmunityEffectiveness = 1f;
            HitDirectionOverride = null;
        }

        /// <summary>伤害来源（没给就是 null）；本游戏无 PlayerDamageSource，先用 object。</summary>
        public object DamageSource { get; set; }

        /// <summary>是不是玩家对玩家（PvP 伤害）。</summary>
        public bool PvP { get; set; }

        /// <summary>伤害冷却计数器（-1 = 不参与冷却）。</summary>
        public int CooldownCounter { get; set; }

        /// <summary>这次伤害能不能被闪避（默认 true）。</summary>
        public bool Dodgeable { get; set; }

        /// <summary>受击方向（-1 左 / 1 右；旧名字 hitDirection 没覆盖时读它）。</summary>
        public int HitDirection { get; set; }

        /// <summary>防御前的伤害修正（SourceValue 套它）。</summary>
        public StatModifier SourceDamage { get; set; }

        /// <summary>防御前的总倍率（默认 1）。</summary>
        public float IncomingDamageMultiplier { get; set; }

        /// <summary>防御之后的伤害修正（Final 套它）。</summary>
        public StatModifier FinalDamage { get; set; }

        /// <summary>直接穿透的防御（数值型，加在比例穿透之后）。</summary>
        public float ArmorPenetration { get; set; }

        /// <summary>按防御比例的穿透（先夹到 0..1 再乘防御）。</summary>
        public float ScalingArmorPenetration { get; set; }

        /// <summary>这一下的击退修正（本版游戏不读它，只留给读的人）。</summary>
        public StatModifier Knockback { get; set; }

        /// <summary>防击退无效化的强度（默认 1）。</summary>
        public float KnockbackImmunityEffectiveness { get; set; }

        /// <summary>覆盖受击方向（null = 用 HitDirection；旧名字 hitDirection 写的就是它）。</summary>
        public int? HitDirectionOverride { get; set; }

        /// <summary>把这一下的伤害上限往下压（和现有的取更小，最少 1）。</summary>
        public void SetMaxDamage(int limit)
        {
            _limit = Math.Min(_limit, Math.Max(limit, 1));
        }

        /// <summary>取消这一下（游戏就当没打中，HurtInfo.Cancelled 会带上）。</summary>
        public void Cancel() { _cancelled = true; }

        /// <summary>不播受伤的尘特效。</summary>
        public void DisableDust() { _dust = true; }

        /// <summary>不播受伤的音效。</summary>
        public void DisableSound() { _sound = true; }

        /// <summary>这一下被 Cancel 过了。</summary>
        public bool Cancelled => _cancelled;

        /// <summary>尘特效被禁用了。</summary>
        public bool DustDisabled => _dust;

        /// <summary>音效被禁用了。</summary>
        public bool SoundDisabled => _sound;

        /// <summary>当前伤害上限（没 SetMaxDamage 过就是正无穷）。</summary>
        public float MaxDamage => _limit;

        /// <summary>旧名字：基数伤害（SourceValue 之前；写负值最后也会被夹住）。</summary>
        public int damage { get => _damage; set => _damage = value; }

        /// <summary>旧名字：受击方向（读 = HitDirectionOverride 优先，写 = 设 HitDirectionOverride）。</summary>
        public int hitDirection { get => HitDirectionOverride ?? HitDirection; set => HitDirectionOverride = value; }

        /// <summary>旧名字：安静（读 = 基数 quiet 或尘和音都禁）。</summary>
        public bool quiet { get => _quiet || (_dust && _sound); set => _quiet = value; }

        /// <summary>旧名字：这次是不是暴击。</summary>
        public bool crit { get => _crit; set => _crit = value; }

        /// <summary>旧名字：能不能被闪避（直通 Dodgeable）。</summary>
        public bool dodgeable { get => Dodgeable; set => Dodgeable = value; }

        /// <summary>进游戏 Hurt 的伤害：SourceDamage 套完乘 IncomingDamageMultiplier，非有限取 0，截断且不小于 0。</summary>
        public int SourceValue()
        {
            var value = SourceDamage.ApplyTo(_damage) * IncomingDamageMultiplier;
            return IsFinite(value) ? Math.Max(0, (int)value) : 0;
        }

        /// <summary>穿透后剩下的防御：Scaling 先夹 0..1 按比例穿，再加 ArmorPenetration，最低 0。</summary>
        public float EffectiveDefense(float defense)
        {
            var penetration = defense * Math.Min(Math.Max(ScalingArmorPenetration, 0f), 1f) + ArmorPenetration;
            return Math.Max(defense - penetration, 0f);
        }

        /// <summary>防御之后的最终伤害：FinalDamage 套完，非有限取 1，再夹 1..MaxDamage。</summary>
        public float Final(float afterDefense)
        {
            var value = FinalDamage.ApplyTo(afterDefense);
            return Math.Max(1f, Math.Min(IsFinite(value) ? value : 1f, _limit));
        }

        /// <summary>包装成 HurtInfo 给 OnHurt/OnHit* 收（damageDone = 游戏实际扣的血）。</summary>
        public HurtInfo ToHurtInfo(int damageDone)
        {
            return new HurtInfo(new Dictionary<string, object>
            {
                ["DamageSource"] = DamageSource,
                ["PvP"] = PvP,
                ["CooldownCounter"] = CooldownCounter,
                ["Dodgeable"] = Dodgeable,
                ["HitDirection"] = hitDirection,
                ["SourceDamage"] = SourceValue(),
                ["Damage"] = damageDone,
                ["Knockback"] = Math.Max(Knockback.ApplyTo(0f), 0f),
                ["Cancelled"] = _cancelled,
                ["DustDisabled"] = _dust,
                ["SoundDisabled"] = _sound,
                ["Quiet"] = quiet,
                ["Crit"] = crit
            });
        }

        /// <summary>Number.isFinite 的对应：不是 NaN 也不是无穷。</summary>
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}