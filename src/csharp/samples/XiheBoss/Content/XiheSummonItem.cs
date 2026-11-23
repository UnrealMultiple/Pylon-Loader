using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss
{

    public sealed class XiheSummonItem : ModItem
    {
        public override string DisplayName => "羲和的印章";

        public override string TexturePath => null;

        internal void ApplyIcon()
        {
            try
            {
                Main.instance?.LoadItem(4989);
                var vanilla = TextureAssets.Item[4989];
                if (vanilla != null)
                {
                    TextureAssets.Item[Type] = vanilla;
                    return;
                }
            }
            catch (Exception e)
            {
                Mod.Logger.Warn("[summon] 借用原版图标失败：" + e);
            }
        }

        public override void SetDefaults()
        {
            var item = Item;
            item.width = 10;
            item.height = 10;
            item.scale = 1f;
            item.useTime = 30;
            item.useAnimation = 30;
            item.useStyle = (byte)ItemUseStyleID.HoldUp;
            item.value = 10000;
            item.rare = 11;
            item.UseSound = SoundID.Item1;
            item.autoReuse = true;
            item.melee = true;
            item.magic = false;
            item.ranged = false;
            item.summon = false;
        }

        public override bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
            Color drawColor, Color itemColor, Vector2 origin, float scale)
        {
            drawColor.B = 150;
            drawColor.A = 200;
            var tex = TextureAssets.Item[Item.type].Value;
            var texOrigin = Utils.Size(tex) / 2f;
            XiheDraw.Sprite(spriteBatch, tex, position, frame, drawColor, 0f, texOrigin, scale);

            drawColor.A = 0;
            drawColor *= 0.5f;
            XiheDraw.Sprite(spriteBatch, tex, position, frame, drawColor, 0f, texOrigin,
                scale + 0.04f + (float)Math.Sin(Main.timeForVisualEffects * 0.1f) * 0.08f);
            XiheDraw.Sprite(spriteBatch, tex, position, frame, drawColor, 0f, texOrigin,
                scale + 0.1f + (float)Math.Sin(Main.timeForVisualEffects * 0.1f) * 0.12f);
            return false;
        }

        public override bool CanUseItem(Player player)
        {

            var projType = ModContent.ProjectileType<XiheSummonProj>();
            if (projType > 0 && HasSummonProj(player, projType)) return false;
            if (XiheBossNpc.Current != null && XiheBossNpc.Current.NpcAlive) return false;

            var source = player.GetProjectileSource_Item(Item);
            Projectile.NewProjectile(source, player.Center, Vector2.Zero, projType, 1, 1f,
                player.whoAmI, 0f, 0f, 0f);

            return false;
        }

        private static bool HasSummonProj(Player player, int type)
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
