// FrameMod / TestSword —— 示例武器："测试之剑"
// 验证：Mod 在 Shoot() 里自己 NewProjectile 一个 **ModProjectile 子类弹幕**（TestFireball）——
// 上游文档 docs/mods/05-itens.md 的标准做法（return false + 自己发射）+ ModProjectile 管线。
using Pylon.ModLoader;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace FrameMod
{
    public sealed class TestSword : ModItem
    {
        public override string DisplayName => "测试之剑";

        public override string Tooltip => "自己发射测试火球的测试武器\n用于验证 ModProjectile 管线";

        public override void SetStaticDefaults() => Mod.Logger.Info($"{Name} 静态默认值（ID {Type}）");

        // 远程射击但归近战类：自己声明拿近战前缀，否则 Melee 类 TestPrefix 滚不到它头上。
        public override bool MeleePrefix(Item item) => true;

        public override void SetDefaults(Item item)
        {
            item.damage = 42;
            item.width = 32;
            item.height = 32;
            item.rare = Pylon.ModLoader.ModContent.RarityType<TestRarity>();   // 自定义稀有度（青蓝色）
            // ⚠ 顺序：先设 shoot 再 SetDefaultWeaponStyle——它读 shoot 决定样式（shoot>0→Shoot 远程链；反了→Swing 近战）
            item.shoot = ProjectileID.Fireball;   // 占位：让样式判定成 Shoot（远程）；真实弹幕由 Shoot() 自己造
            item.shootSpeed = 14f;
            SetDefaultWeaponStyle(useTime: 12, autoReuse: true);
        }

        /// <summary>自己造弹：返回 false 阻止游戏发射，改发射自定义弹幕 TestFireball（ModProjectile 子类，验证其管线）。</summary>
        public override bool Shoot(Item item, Player player, Vector2 position, Vector2 velocity, int type, int damage, float knockBack)
        {
            var fireball = ModContent.ProjectileType<TestFireball>();
            var source = player.GetProjectileSource_Item(item);
            Projectile.NewProjectile(source, position, velocity, fireball, damage, knockBack, player.whoAmI);
            Mod.Logger.Info($"[hook] TestSword.Shoot 自射 TestFireball（type={fireball} dmg={damage}）");
            return false;   // 游戏自己的那发不发射（我们已自己造）
        }
    }
}