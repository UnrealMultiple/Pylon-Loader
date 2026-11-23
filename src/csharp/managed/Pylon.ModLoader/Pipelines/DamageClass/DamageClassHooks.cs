using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 伤害职业的游戏入口：每帧重置（ResetEffects）、攻速折算（CapAttackSpeeds）、
/// 武器伤害/暴击/击退/攻速（GetWeaponDamageMultiplier、GetWeaponCrit、GetWeaponKnockback、
/// ApplyItemAnimation、ApplyItemTime）、破防（ProcessHitAgainstNPC、NewProjectile）、
/// 鞭类弹幕的暴击与小兵伤害（Projectile.Damage、TryConsumingTimerCrit、Projectile.Update）、
/// 以及提示行的 Damage/CritChance/Speed/Knockback 重排。
/// </summary>
internal static class DamageClassHooks
{
    static bool[] projectileTypes = Array.Empty<bool>();
    static readonly object ProjectileGate = new();
    static Projectile firing = null!;

    internal static void Install() =>
        GateHub<DamageClassPipeline>.Trigger<DamageClassPipeline.Gates.Engine>();

    internal static void InstallEngine()
    {
        On.Terraria.Player.ResetEffects += ResetEffects;
        On.Terraria.Player.CapAttackSpeeds += CapAttackSpeeds;
        On.Terraria.Player.GetWeaponDamageMultiplier += GetWeaponDamageMultiplier;
        On.Terraria.Player.GetWeaponCrit += GetWeaponCrit;
        On.Terraria.Player.GetWeaponKnockback += GetWeaponKnockback;
        On.Terraria.Player.ApplyItemAnimation += ApplyItemAnimation;
        On.Terraria.Player.ApplyItemTime_Item += ApplyItemTime;
        On.Terraria.Player.ProcessHitAgainstNPC += ProcessHitAgainstNPC;
        On.Terraria.Projectile.NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float_NewProjectileModifier += NewProjectile;
        TooltipPipeline.WantDamageLines();
    }

    internal static void WantProjectile(int type)
    {
        if (type < 0) return;
        lock (ProjectileGate)
        {
            bool[] table = projectileTypes;
            if (type >= table.Length)
            {
                var grown = new bool[Math.Max(type + 1, table.Length * 2)];
                Array.Copy(table, grown, table.Length);
                table = grown;
            }
            table[type] = true;
            Volatile.Write(ref projectileTypes, table);
        }
        GateHub<DamageClassPipeline>.Trigger<DamageClassPipeline.Gates.Projectile>();
    }

    internal static void InstallProjectileHooks()
    {
        On.Terraria.Projectile.Damage += ProjectileDamage;
        On.Terraria.Player.TryConsumingTimerCrit += TryConsumingTimerCrit;
        On.Terraria.Projectile.Update += ProjectileUpdate;
    }

    static bool Marked(int type)
    {
        bool[] table = Volatile.Read(ref projectileTypes);
        return (uint)type < (uint)table.Length && table[type];
    }

    static bool HasData(Player player) => DamageClassLoader.Players.Count > 0 && DamageClassLoader.Players.ContainsKey(player.Pointer);

    static bool MeleeLike(DamageClass cls) => cls == DamageClass.Melee || cls == DamageClass.MeleeNoSpeed || cls == DamageClass.SummonMeleeSpeed;

    static void ResetEffects(On.Terraria.Player.orig_ResetEffects orig, Player self)
    {
        orig(self);
        try
        {
            if (DamageClassLoader.Players.TryGetValue(self.Pointer, out DamageClassLoader.PlayerStats? d)) DamageClassLoader.Reset(self, d);
        }
        catch (Exception e) { ContentErrors.Line("[damageclass] ResetEffects 异常：" + Safe.Dump(e)); }
    }

    static void CapAttackSpeeds(On.Terraria.Player.orig_CapAttackSpeeds orig, Player self)
    {
        try { DamageClassLoader.Fold(self); }
        catch (Exception e) { ContentErrors.Line("[damageclass] Fold 异常：" + Safe.Dump(e)); }
        orig(self);
    }

    static float GetWeaponDamageMultiplier(On.Terraria.Player.orig_GetWeaponDamageMultiplier orig, Player self, Item item)
    {
        DamageClass cls;
        bool hooked;
        try
        {
            cls = DamageClassLoader.ItemClass(item);
            hooked = DamageClassLoader.IsHooked(cls);
            if (!hooked && !HasData(self)) return orig(self, item);
        }
        catch (Exception e)
        {
            ContentErrors.Line("[damageclass] GetWeaponDamageMultiplier 异常：" + Safe.Dump(e));
            return orig(self, item);
        }
        StatModifier t = DamageClassLoader.Total(self, cls).Damage;
        float mult = hooked ? t.Additive * t.Multiplicative : orig(self, item);
        int damage = item.damage;
        if ((t.Base == 0 && t.Flat == 0) || damage <= 0) return mult;
        return ((damage + t.Base) * mult + t.Flat) / damage;
    }

    static int GetWeaponCrit(On.Terraria.Player.orig_GetWeaponCrit orig, Player self, Item item)
    {
        int value = orig(self, item);
        try
        {
            DamageClass cls = DamageClassLoader.ItemClass(item);
            if (!DamageClassLoader.IsHooked(cls)) return value;
            return DamageClassLoader.UsesCrit(cls) ? DamageClassLoader.Round(DamageClassLoader.Total(self, cls).Crit) + item.crit : 0;
        }
        catch (Exception e)
        {
            ContentErrors.Line("[damageclass] GetWeaponCrit 异常：" + Safe.Dump(e));
            return value;
        }
    }

    static float GetWeaponKnockback(On.Terraria.Player.orig_GetWeaponKnockback orig, Player self, Item item, float knockBack)
    {
        float vanilla = orig(self, item, knockBack);
        try
        {
            DamageClass cls = DamageClassLoader.ItemClass(item);
            if (!DamageClassLoader.IsHooked(cls) && !HasData(self)) return vanilla;
            StatModifier modifier = DamageClassLoader.Total(self, cls).Knockback;
            return modifier.Equals(StatModifier.Default) ? vanilla : modifier.ApplyTo(vanilla);
        }
        catch (Exception e)
        {
            ContentErrors.Line("[damageclass] GetWeaponKnockback 异常：" + Safe.Dump(e));
            return vanilla;
        }
    }

    static void ApplyItemAnimation(On.Terraria.Player.orig_ApplyItemAnimation orig, Player self, Item item)
    {
        orig(self, item);
        try
        {
            DamageClass cls = DamageClassLoader.ItemClass(item);
            bool hooked = DamageClassLoader.IsHooked(cls);
            if (!hooked && (MeleeLike(cls) || !HasData(self))) return;
            float speed = DamageClassLoader.Total(self, cls).Speed;
            if (!hooked && MathF.Abs(speed - 1) < 1e-6f) return;
            int baseFrames = hooked ? item.useAnimation : self.itemAnimationMax;
            int frames = Math.Max(1, (int)(baseFrames / Math.Clamp(speed, 0.01f, 3f)));
            self.itemAnimation = frames;
            self.itemAnimationMax = frames;
        }
        catch (Exception e) { ContentErrors.Line("[damageclass] ApplyItemAnimation 异常：" + Safe.Dump(e)); }
    }

    static void ApplyItemTime(On.Terraria.Player.orig_ApplyItemTime_Item orig, Player self, Item item)
    {
        orig(self, item);
        try
        {
            DamageClass cls = DamageClassLoader.ItemClass(item);
            bool hooked = DamageClassLoader.IsHooked(cls);
            if (!hooked && (MeleeLike(cls) || !HasData(self))) return;
            float speed = DamageClassLoader.Total(self, cls).Speed;
            if (MathF.Abs(speed - 1) < 1e-6f) return;
            int frames = Math.Max(1, (int)(item.useTime / Math.Clamp(speed, 0.01f, 3f)));
            self.itemTime = frames;
            self.itemTimeMax = frames;
        }
        catch (Exception e) { ContentErrors.Line("[damageclass] ApplyItemTime 异常：" + Safe.Dump(e)); }
    }

    static void ProcessHitAgainstNPC(On.Terraria.Player.orig_ProcessHitAgainstNPC orig, Player self, Item item,
                                     Rectangle itemRectangle, int originalDamage, float knockBack, int npcIndex)
    {
        int extra = 0;
        try
        {
            DamageClass cls = DamageClassLoader.ItemClass(item);
            if (DamageClassLoader.IsHooked(cls) || HasData(self)) extra = DamageClassLoader.ExtraPen(self, cls, item.melee);
        }
        catch (Exception e) { ContentErrors.Line("[damageclass] ProcessHitAgainstNPC 异常：" + Safe.Dump(e)); }
        if (extra <= 0)
        {
            orig(self, item, itemRectangle, originalDamage, knockBack, npcIndex);
            return;
        }
        self.armorPenetration += extra;
        try { orig(self, item, itemRectangle, originalDamage, knockBack, npcIndex); }
        finally { self.armorPenetration -= extra; }
    }

    static int NewProjectile(On.Terraria.Projectile.orig_NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float_NewProjectileModifier orig,
                             IEntitySource spawnSource, float x, float y, float speedX, float speedY,
                             int type, int damage, float knockBack, int owner, float ai0, float ai1, float ai2,
                             NewProjectileModifier modifier)
    {
        int i = orig(spawnSource, x, y, speedX, speedY, type, damage, knockBack, owner, ai0, ai1, ai2, modifier);
        if (i < 0 || i >= 1000) return i;
        try
        {
            Projectile p = Main.projectile[i];
            int who = p.owner;
            if (!p.active || who < 0 || who >= 255) return i;
            Player player = Main.player[who];
            if (player is null || !player.active || !HasData(player)) return i;
            int extra = DamageClassLoader.ExtraPen(player, DamageClassLoader.ProjectileClass(p), p.melee);
            if (extra > 0) p.armorPenetration += extra;
        }
        catch (Exception e) { ContentErrors.Line("[damageclass] NewProjectile 异常：" + Safe.Dump(e)); }
        return i;
    }

    static void ProjectileDamage(On.Terraria.Projectile.orig_Damage orig, Projectile self)
    {
        if (!Marked(self.type))
        {
            orig(self);
            return;
        }
        Projectile outer = firing;
        firing = self;
        try { orig(self); }
        finally { firing = outer; }
    }

    static void TryConsumingTimerCrit(On.Terraria.Player.orig_TryConsumingTimerCrit orig, Player self, Rectangle hitbox, ref bool crit)
    {
        Projectile p = firing;
        if (p is not null)
        {
            try
            {
                DamageClass cls = DamageClassLoader.ProjectileClass(p);
                if (DamageClassLoader.IsHooked(cls))
                {
                    float chance = DamageClassLoader.UsesCrit(cls) ? DamageClassLoader.Total(self, cls).Crit + p.bonusCritChance : -1;
                    Item held = self.HeldItem;
                    if (chance >= 0 && held is not null && DamageClassLoader.ItemClass(held) == cls) chance += held.crit;
                    crit = chance >= 0 && Random.Shared.NextSingle() * 100 < chance;
                }
            }
            catch (Exception e) { ContentErrors.Line("[damageclass] TryConsumingTimerCrit 异常：" + Safe.Dump(e)); }
        }
        orig(self, hitbox, ref crit);
    }

    static void ProjectileUpdate(On.Terraria.Projectile.orig_Update orig, Projectile self, int i)
    {
        orig(self, i);
        if (!Marked(self.type)) return;
        try
        {
            if (!self.active || !(self.minion || self.sentry) || self.originalDamage <= 0) return;
            Player owner = Main.player[self.owner];
            if (owner is null) return;
            StatModifier damage = DamageClassLoader.Total(owner, DamageClassLoader.ProjectileClass(self)).Damage;
            self.damage = (int)(damage.ApplyTo(self.originalDamage) + 5e-6f);
        }
        catch (Exception e) { ContentErrors.Line("[damageclass] Projectile.Update 异常：" + Safe.Dump(e)); }
    }

    internal static void Tooltip(Item item, List<TooltipLine> lines)
    {
        if (item.damage <= 0) return;
        Player me = Main.LocalPlayer;
        if (me is null) return;
        DamageClass cls = DamageClassLoader.ItemClass(item);
        bool hooked = DamageClassLoader.IsHooked(cls);
        bool Show(string line)
        {
            try { return cls.ShowStatTooltipLine(me, line); }
            catch (Exception e)
            {
                ContentErrors.Line($"[damageclass] {cls.Name}.ShowStatTooltipLine 异常：{Safe.Dump(e)}");
                return true;
            }
        }
        int Find(string name) => lines.FindIndex(l => l.Mod == "Terraria" && l.Name == name);

        int damage = Find("Damage");
        if (damage >= 0)
        {
            if (!Show("Damage")) lines.RemoveAt(damage);
            else if (hooked)
            {
                string text = lines[damage].Text;
                int digits = 0;
                while (digits < text.Length && char.IsDigit(text[digits])) digits++;
                lines[damage].Text = text[..digits] + (cls is VanillaDamageClass ? "" : " ") + (cls.DisplayName?.Value ?? cls.Name);
            }
        }

        int crit = Find("CritChance");
        if (hooked)
        {
            string? text = null;
            if (DamageClassLoader.UsesCrit(cls))
            {
                int c = DamageClassLoader.Round(DamageClassLoader.Total(me, cls).Crit) + item.GetVisualCritChance(me);
                if (c > 0) text = c + Lang.tip[5].Value;
            }
            if (text == null || !Show("CritChance"))
            {
                if (crit >= 0) lines.RemoveAt(crit);
            }
            else if (crit >= 0) lines[crit].Text = text;
            else lines.Insert(After(lines, "Damage", "ItemName"), new TooltipLine("Terraria", "CritChance", text));
        }
        else if (crit >= 0 && !Show("CritChance")) lines.RemoveAt(crit);

        int speed = Find("Speed");
        if (speed >= 0 && !Show("Speed")) lines.RemoveAt(speed);
        else if (speed < 0 && hooked && item.useStyle != 0 && Show("Speed"))
            lines.Insert(After(lines, "CritChance", "Damage", "ItemName"), new TooltipLine("Terraria", "Speed", Lang.tip[SpeedTip(item.useAnimation)].Value ?? ""));

        int knockback = Find("Knockback");
        if (knockback < 0) return;
        if (!Show("Knockback"))
        {
            lines.RemoveAt(knockback);
            return;
        }
        if (!HasData(me)) return;
        StatModifier km = DamageClassLoader.Total(me, cls).Knockback;
        if (km.Equals(StatModifier.Default)) return;
        float kb = item.knockBack;
        if (item.summon) kb += me.minionKB;
        if (me.magicQuiver && item.useAmmo == ItemID.WoodenArrow || item.useAmmo == ItemID.Stake) kb = (int)(kb * 1.1f);
        Item selected = me.inventory[me.selectedItem];
        if (selected is not null && selected.type == ItemID.PsychoKnife && item.type == ItemID.PsychoKnife) kb += kb * (1 - me.stealth);
        lines[knockback].Text = Lang.tip[KnockbackTip(km.ApplyTo(kb))].Value ?? "";
    }

    static int After(List<TooltipLine> lines, params string[] names)
    {
        foreach (string name in names)
        {
            int at = lines.FindIndex(l => l.Mod == "Terraria" && l.Name == name);
            if (at >= 0) return at + 1;
        }
        return lines.Count;
    }

    static int SpeedTip(int a) => a <= 8 ? 6 : a <= 20 ? 7 : a <= 25 ? 8 : a <= 30 ? 9 : a <= 35 ? 10 : a <= 45 ? 11 : a <= 55 ? 12 : 13;

    static int KnockbackTip(float kb) => kb == 0 ? 14 : kb <= 1.5f ? 15 : kb <= 3 ? 16 : kb <= 4 ? 17 : kb <= 6 ? 18 : kb <= 7 ? 19 : kb <= 9 ? 20 : kb <= 11 ? 21 : 22;
}
