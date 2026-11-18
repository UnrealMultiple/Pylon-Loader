using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Pipelines;
using Terraria;
using Terraria.GameContent.UI.ResourceSets;

namespace Pylon.ModLoader.Content;
/// <summary>游戏类型来自 interop 代理，用 extension block 补玩家侧的作者 API。</summary>
public static class PlayerTml
{
    extension(Player player)
    {
        /// <summary>这个玩家身上的全部 ModPlayer 实例（按注册序）。</summary>
        public ModPlayer[] ModPlayers => PlayerPipeline.Instance.ModsOf(player);

        public string setBonus
        {
            get => EquipPipeline.SetBonusText(player) ?? "";
            set => EquipPipeline.SetBonusText(player, value);
        }

        public T GetModPlayer<T>() where T : ModPlayer => player.GetModPlayer(TemplateOf<T>());

        public T GetModPlayer<T>(T baseInstance) where T : ModPlayer
        {
            var instances = PlayerPipeline.Instance.ModsOf(player);
            if (baseInstance is null || baseInstance.Index >= instances.Length)
                throw new KeyNotFoundException($"Instance of '{typeof(T).Name}' does not exist on the current player.");

            return (T)instances[baseInstance.Index];
        }

        public bool TryGetModPlayer<T>(out T? result) where T : ModPlayer =>
            player.TryGetModPlayer(TemplateOf<T>(), out result);

        public bool TryGetModPlayer<T>(T baseInstance, out T? result) where T : ModPlayer
        {
            var instances = PlayerPipeline.Instance.ModsOf(player);
            if (baseInstance is null || baseInstance.Index >= instances.Length)
            {
                result = null;
                return false;
            }

            result = (T)instances[baseInstance.Index];
            return true;
        }

        public bool HasBuff(int type) => player.FindBuffIndex(type) != -1;

        public static int LifeCrystalMax => LifeCrystals;

        public static int LifeFruitMax => LifeFruits;

        public static int ManaCrystalMax => ManaCrystals;

        public int ConsumedLifeCrystals
        {
            get => Math.Clamp((Math.Min(player.statLifeMax, 400) - 100) / 20, 0, LifeCrystals);
            set
            {
                var fruit = Math.Clamp((player.statLifeMax - 400) / 5, 0, LifeFruits);
                player.statLifeMax = 100 + 20 * Math.Clamp(value, 0, LifeCrystals) + 5 * fruit;
            }
        }

        public int ConsumedLifeFruit
        {
            get => Math.Clamp((player.statLifeMax - 400) / 5, 0, LifeFruits);
            set
            {
                var crystals = Math.Clamp((Math.Min(player.statLifeMax, 400) - 100) / 20, 0, LifeCrystals);
                player.statLifeMax = 100 + 20 * crystals + 5 * Math.Clamp(value, 0, LifeFruits);
            }
        }

        public int ConsumedManaCrystals
        {
            get => Math.Clamp((player.statManaMax - 20) / 20, 0, ManaCrystals);
            set => player.statManaMax = 20 + 20 * Math.Clamp(value, 0, ManaCrystals);
        }

        public void UseHealthMaxIncreasingItem(int increase)
        {
            increase = Math.Max(0, increase);
            player.statLifeMax2 += increase;
            player.statLife += increase;
            if (Main.myPlayer == player.whoAmI) player.HealEffect(increase);
        }

        public void UseManaMaxIncreasingItem(int increase)
        {
            increase = Math.Max(0, increase);
            player.statManaMax2 += increase;
            player.statMana += increase;
            if (Main.myPlayer == player.whoAmI) player.ManaEffect(increase);
        }

        public void AddImmuneTime(int cooldownCounterId, int immuneTime)
        {
            if (cooldownCounterId < 0)
            {
                player.immuneTime += immuneTime;
                return;
            }

            var cooldowns = player.hurtCooldowns;
            if (cooldowns is null || (uint)cooldownCounterId >= (uint)cooldowns.Length) return;
            cooldowns[cooldownCounterId] += immuneTime;
        }

        public ref ExtraJumpState GetJumpState<T>() where T : ExtraJump => ref ExtraJumpTable.GetJumpState<T>(player);

        public ref ExtraJumpState GetJumpState<T>(T baseInstance) where T : ExtraJump =>
            ref ExtraJumpTable.GetJumpState(player, baseInstance);

        public Span<ExtraJumpState> ExtraJumps => ExtraJumpTable.ExtraJumpStates(player);

        public bool blockExtraJumps
        {
            get => ExtraJumpTable.GetBlockExtraJumps(player);
            set => ExtraJumpTable.SetBlockExtraJumps(player, value);
        }

        public bool AnyExtraJumpUsable() => ExtraJumpTable.AnyExtraJumpUsable(player);

        public void StopExtraJumpInProgress() => ExtraJumpTable.StopExtraJumpInProgress(player);

        public void ConsumeAllExtraJumps() => ExtraJumpTable.ConsumeAllExtraJumps(player);

        public SceneEffectInstance CurrentSceneEffect => SceneEffectPipeline.Scene;

        public bool InModBiome(ModBiome biome) => BiomePipeline.Instance.IsActive(player, biome);

        public bool InModBiome<T>() where T : ModBiome
        {
            var biome = ModContent.GetInstance<T>();
            return biome is not null && BiomePipeline.Instance.IsActive(player, biome);
        }

        public StatModifier GetDamage(DamageClass damageClass) => DamageClassLoader.Stats(player, damageClass).damage;

        public StatModifier GetDamage<T>() where T : DamageClass => DamageClassLoader.Stats(player, DamageClassLoader.InstanceOf<T>()).damage;

        public ref float GetCritChance(DamageClass damageClass) => ref DamageClassLoader.Stats(player, damageClass).critChance;

        public ref float GetCritChance<T>() where T : DamageClass => ref DamageClassLoader.Stats(player, DamageClassLoader.InstanceOf<T>()).critChance;

        public ref float GetAttackSpeed(DamageClass damageClass) => ref DamageClassLoader.Stats(player, damageClass).attackSpeed;

        public ref float GetAttackSpeed<T>() where T : DamageClass => ref DamageClassLoader.Stats(player, DamageClassLoader.InstanceOf<T>()).attackSpeed;

        public ref float GetArmorPenetration(DamageClass damageClass) => ref DamageClassLoader.Stats(player, damageClass).armorPen;

        public ref float GetArmorPenetration<T>() where T : DamageClass => ref DamageClassLoader.Stats(player, DamageClassLoader.InstanceOf<T>()).armorPen;

        public StatModifier GetKnockback(DamageClass damageClass) => DamageClassLoader.Stats(player, damageClass).knockback;

        public StatModifier GetKnockback<T>() where T : DamageClass => DamageClassLoader.Stats(player, DamageClassLoader.InstanceOf<T>()).knockback;

        public StatModifier GetTotalDamage(DamageClass damageClass) => DamageClassLoader.Total(player, damageClass).Damage;

        public StatModifier GetTotalDamage<T>() where T : DamageClass => DamageClassLoader.Total(player, DamageClassLoader.InstanceOf<T>()).Damage;

        public float GetTotalCritChance(DamageClass damageClass) => DamageClassLoader.Total(player, damageClass).Crit;

        public float GetTotalCritChance<T>() where T : DamageClass => DamageClassLoader.Total(player, DamageClassLoader.InstanceOf<T>()).Crit;

        public float GetTotalAttackSpeed(DamageClass damageClass) => DamageClassLoader.Total(player, damageClass).Speed;

        public float GetTotalAttackSpeed<T>() where T : DamageClass => DamageClassLoader.Total(player, DamageClassLoader.InstanceOf<T>()).Speed;

        public float GetTotalArmorPenetration(DamageClass damageClass) => DamageClassLoader.Total(player, damageClass).Pen;

        public float GetTotalArmorPenetration<T>() where T : DamageClass => DamageClassLoader.Total(player, DamageClassLoader.InstanceOf<T>()).Pen;

        public StatModifier GetTotalKnockback(DamageClass damageClass) => DamageClassLoader.Total(player, damageClass).Knockback;

        public StatModifier GetTotalKnockback<T>() where T : DamageClass => DamageClassLoader.Total(player, DamageClassLoader.InstanceOf<T>()).Knockback;

        public float GetWeaponAttackSpeed(Item sItem)
        {
            float speed = DamageClassLoader.Total(player, DamageClassLoader.ItemClass(sItem)).Speed;
            var multiplier = Terraria.ID.ItemID.Sets.BonusMeleeSpeedMultiplier;
            int type = sItem.type;
            float bonus = multiplier is not null && (uint)type < (uint)multiplier.Length ? multiplier[type] : 1f;
            return 1 + (speed - 1) * bonus;
        }

        public int GetWeaponArmorPenetration(Item sItem) =>
            (int)(sItem.armorPenetration + DamageClassLoader.Total(player, DamageClassLoader.ItemClass(sItem)).Pen);
    }

    extension(PlayerStatsSnapshot snapshot)
    {
        public int AmountOfLifeHearts =>
            snapshot.LifePerSegment > 0f ? Math.Clamp((int)Math.Ceiling(snapshot.LifeMax / snapshot.LifePerSegment), 0, 20) : 0;

        public int AmountOfManaStars =>
            snapshot.ManaPerSegment > 0f ? Math.Clamp((int)Math.Ceiling(snapshot.ManaMax / snapshot.ManaPerSegment), 0, 20) : 0;
    }

    private const int LifeCrystals = 15;
    private const int LifeFruits = 20;
    private const int ManaCrystals = 9;

    private static T TemplateOf<T>() where T : ModPlayer
    {
        foreach (var template in PlayerPipeline.Instance.Binding.Templates)
            if (template is T typed) return typed;

        throw new KeyNotFoundException($"Instance of '{typeof(T).Name}' does not exist on the current player.");
    }
}
