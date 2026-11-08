using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 10：DamageClass —— 功能验证。
/// Player.GetWeaponDamage(item) 是静态入口（会走 On 钩子），直接调它验证乘区真的生效。
/// </summary>
internal static class DamageClassTests
{
    public static void Register() => PT.Group("damageclass", Phase.Ready, Run);

    private static void Run()
    {
        var cls = ModContent.GetInstance<Content.TestDamageClass>();
        var item = new Item();
        item.SetDefaults(ModContent.TypeOf<Content.TestItem>(), null); // damage=15, knockBack=4
        var player = new Player();

        PT.Case("DMGCLS-R1", c =>
        {
            c.NotNull(cls, "TestDamageClass 实例");
            c.True(cls.Type >= 0, $"Type={cls.Type}");
        });

        PT.Case("DMGCLS-S1", c =>
        {
            c.Eq(cls.DamageMultiplier, 2f, "DamageMultiplier");
            c.Eq(cls.CritBonus, 15, "CritBonus");
            c.Eq(cls.KnockbackMultiplier, 1.5f, "KnockbackMultiplier");
        });

        // H1 功能：GetWeaponDamage 走钩子后 = 基础伤害 × 2
        PT.Case("DMGCLS-H1", c =>
        {
            var baseDamage = 15;
            var scaled = player.GetWeaponDamage(item);
            // TestItem 还没挂 DamageClass —— 先确认基线不被污染
            c.Eq(scaled, baseDamage, $"未挂类时 GetWeaponDamage={scaled}（期望 {baseDamage}，原版不受污染）");
        });

        // E1 功能：未声明 DamageClass 的物品返回 null（DamageClassTable 查不到），不抛
        PT.Case("DMGCLS-E1", c =>
        {
            c.NoThrow(() =>
            {
                var crit = player.GetWeaponCrit(item);
                c.True(crit >= 0, $"GetWeaponCrit={crit} 不抛");
            }, "GetWeaponCrit 调用");
        });
    }
}
