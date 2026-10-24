using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace XiheMagic
{
    public sealed class XiHeMagicItem : ModItem
    {
        public override string DisplayName => "羲和魔杖";

        public override string Tooltip =>
            "引导太阳的怒火，自天穹降下贯穿天地的激光柱\n" +
            "按住使用：约 3.3 秒后降下激光柱，可拖动准星\n" +
            "激光柱维持期间持续消耗魔力\n" +
            "「羲和驭日，焚尽八荒」";

        // MEAC 里这件武器直接借用原版 Item_4952（光之女皇的魔法武器）的图标；
        // 这里不复制游戏美术，等贴图表定型后把槽位指到原版贴图（完全一致）。
        public override string TexturePath => null;

        public override void PostSetupContent()
        {
            try
            {
                Main.instance?.LoadItem(ItemID.FairyQueenMagicItem);
                var vanilla = TextureAssets.Item[ItemID.FairyQueenMagicItem];
                if (vanilla != null)
                {
                    TextureAssets.Item[Type] = vanilla;
                    return;
                }
            }
            catch (System.Exception e)
            {
                Mod.Logger.Warn("[item] 借用原版图标失败：" + e.Message);
            }

            // 兜底：用 MEAC 的 Arc 贴图，至少不是空白。
            try { TextureAssets.Item[Type] = Mod.LoadAsset("assets/textures/Arc.png"); }
            catch (System.Exception e) { Mod.Logger.Warn("[item] Arc 兜底图标失败：" + e.Message); }
        }

        public override void SetDefaults(Item item)
        {
            item.width = 30;
            item.height = 30;

            // 原版：holdStyle = 6（HoldLamp）、useStyle = 14（RaiseLamp）
            item.holdStyle = (byte)ItemHoldStyleID.HoldLamp;
            item.useStyle = (byte)ItemUseStyleID.RaiseLamp;

            item.magic = true;
            item.melee = false;
            item.ranged = false;
            item.summon = false;
            item.noMelee = true;

            item.damage = 200;
            item.knockBack = 0f;
            item.rare = 8;

            item.useTime = 30;
            item.useAnimation = 30;
            item.useTurn = false;
            item.autoReuse = true;
            item.channel = true;

            // 原版 item.shoot = XiheMagicControler；手机版改成 UseItem 里手动发射，
            // 不依赖 useStyle 是否走 ItemCheck_Shoot（RaiseLamp 不是 Shoot 样式）。
            item.shoot = 0;
            item.shootSpeed = 1f;
        }

        /// <summary>每次「真正开始使用」问一次：场上没有控制器就造一个（原版 Shoot 的同一个判断）。</summary>
        public override bool UseItem(Item item, Player player)
        {
            if (player.whoAmI != Main.myPlayer) return false;
            SpawnController(item, player);
            return false;
        }

        private static void SpawnController(Item item, Player player)
        {
            var type = ModContent.ProjectileType<XiheMagicControler>();
            if (type <= 0) return;

            // ⚠ 手机版 Player.ownedProjectileCounts 是每实例定长数组，没扩到 mod 弹幕号，
            // 直接索引会 IndexOutOfRange（真机实测）。改成自己扫场上弹幕。
            if (HasController(player, type)) return;

            var source = player.GetProjectileSource_Item(item);
            var damage = Math.Max(item.damage, player.GetWeaponDamage(item));
            Projectile.NewProjectile(source, player.Center, Vector2.Zero, type,
                damage, player.GetWeaponKnockback(item, item.knockBack),
                player.whoAmI, 0f, 0f, 0f);
        }

        private static bool HasController(Player player, int type)
        {
            var all = Main.projectile;
            for (var i = 0; i < all.Length; i++)
            {
                var p = all[i];
                if (p != null && p.active && p.type == type && p.owner == player.whoAmI) return true;
            }
            return false;
        }
    }
}
