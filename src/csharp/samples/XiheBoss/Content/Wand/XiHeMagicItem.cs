using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss.Wand
{

    public sealed class XiHeMagicItem : ModItem
    {
        public override string DisplayName => "羲和魔杖";

        public override string Tooltip =>
            "引导太阳的怒火，自天穹降下贯穿天地的激光柱\n" +
            "按住使用：约 3.3 秒后降下激光柱，可拖动准星\n" +
            "激光柱维持期间持续消耗魔力\n" +
            "「羲和驭日，焚尽八荒」";

        public override string TexturePath => null;

        internal void ApplyIcon()
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
                Mod.Logger.Warn("[item] 借用原版图标失败：" + e);
            }

            try { TextureAssets.Item[Type] = Mod.Assets.Request<Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D>("assets/textures/Arc.png"); }
            catch (System.Exception e) { Mod.Logger.Warn("[item] Arc 兜底图标失败：" + e); }
        }

        public override void SetDefaults()
        {
            var item = Item;
            item.width = 30;
            item.height = 30;

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

            item.shoot = 0;
            item.shootSpeed = 1f;
        }

        public override bool? UseItem(Player player)
        {
            var item = Item;
            if (player.whoAmI != Main.myPlayer) return false;
            SpawnController(item, player);
            return false;
        }

        private static void SpawnController(Item item, Player player)
        {
            var type = ModContent.ProjectileType<XiheMagicControler>();
            if (type <= 0) return;

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
