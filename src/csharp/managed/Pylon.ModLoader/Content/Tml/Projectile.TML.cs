using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Pipelines;
using Pylon.ModLoader.Textures;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader.Content;
/// <summary>Projectile.TML.cs 的等价扩展成员；游戏 Projectile 来自 interop 代理，用 extension block 补作者 API。</summary>
public static class ProjectileTml
{
    extension(Projectile projectile)
    {
        /// <summary>这个弹幕对应的 ModProjectile 实例；原版弹幕返回 null。</summary>
        public ModProjectile? ModProjectile => ProjectilePipeline.Instance.InstanceOf(projectile);

        /// <summary>作用在这个弹幕上的全局弹幕实例（按注册序）。</summary>
        public IReadOnlyList<GlobalProjectile> EntityGlobals => GlobalProjectileRegistry.Of(projectile);

        public T GetGlobalProjectile<T>() where T : GlobalProjectile
        {
            foreach (var global in GlobalProjectileRegistry.Of(projectile))
                if (global is T typed) return typed;

            throw new KeyNotFoundException($"Projectile {projectile.type} 上没有 {typeof(T).FullName}");
        }

        public T GetGlobalProjectile<T>(T baseInstance) where T : GlobalProjectile
        {
            foreach (var global in GlobalProjectileRegistry.Of(projectile))
                if (global is T typed) return typed;

            return baseInstance;
        }

        public bool TryGetGlobalProjectile<T>(out T? result) where T : GlobalProjectile
        {
            foreach (var global in GlobalProjectileRegistry.Of(projectile))
            {
                if (global is not T typed) continue;
                result = typed;
                return true;
            }

            result = null;
            return false;
        }

        /// <summary>弹幕的主人；敌意/陷阱弹幕与槽位越界返回 false。</summary>
        public bool TryGetOwner(out Player? player)
        {
            player = null;
            if (projectile.npcProj || projectile.trap || (uint)projectile.owner > 255u) return false;

            var owner = Main.player[projectile.owner];
            if (owner is not { active: true }) return false;

            player = owner;
            return true;
        }

        /// <summary>照另一个已注册类型的默认值铺一遍；本弹幕的类型号与 ModProjectile/Global 实例保持不变。</summary>
        public void CloneDefaults(int TypeToClone) => ProjectilePipeline.Instance.CloneDefaults(projectile, TypeToClone);

        /// <summary>画这张精灵要用的默认 DrawData（原版公式 + 本弹幕的 DrawOffset/Origin 偏移）。</summary>
        public DrawData? GetDefaultDrawData(Player player, Color lightColor)
        {
            var texture = OwnTexture(projectile);
            if (texture is null) return null;

            PylonTexture.Size(texture, out var textureWidth, out var textureHeight);

            var mod = ProjectilePipeline.Instance.InstanceOf(projectile);
            var drawOffsetX = mod?.DrawOffsetX ?? 0;
            var originOffsetY = -(mod?.DrawOriginOffsetY ?? 0);
            var originX = (textureWidth - projectile.width) * 0.5f + projectile.width * 0.5f + (mod?.DrawOriginOffsetX ?? 0f);

            var effects = projectile.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Rectangle sourceRectangle;
            var frameTable = Main.projFrames;
            var frames = frameTable is not null && projectile.type >= 0 && projectile.type < frameTable.Length
                ? frameTable[projectile.type] : 1;
            if (frames > 1)
            {
                var frameHeight = textureHeight / frames;
                sourceRectangle = new Rectangle(0, frameHeight * projectile.frame, textureWidth, frameHeight - 1);
            }
            else
            {
                sourceRectangle = new Rectangle(0, 0, textureWidth, textureHeight);
                if (projectile.ownerHitCheck && player.gravDir == -1f)
                {
                    if (player.direction == 1) effects = SpriteEffects.FlipHorizontally;
                    else if (player.direction == -1) effects = SpriteEffects.None;
                }
            }

            return new DrawData(texture,
                new Vector2(projectile.position.X - Main.screenPosition.X + originX + drawOffsetX,
                            projectile.position.Y - Main.screenPosition.Y + projectile.height / 2f + projectile.gfxOffY),
                sourceRectangle, projectile.GetAlpha(lightColor), projectile.rotation,
                new Vector2(originX, projectile.height / 2f + originOffsetY), projectile.scale, effects, 0);
        }

        /// <summary>真生成一发弹幕并返回弹幕对象本身。</summary>
        public static Projectile NewProjectileDirect(IEntitySource spawnSource, Vector2 position, Vector2 velocity,
            int type, int damage, float knockback, int owner = -1, float ai0 = 0f, float ai1 = 0f, float ai2 = 0f)
        {
            var index = Projectile.NewProjectile(spawnSource, position, velocity, type, damage, knockback,
                owner, ai0, ai1, ai2);
            return Main.projectile[index];
        }

        public DamageClass DamageType
        {
            get => DamageClassLoader.ProjectileClass(projectile);
            set => DamageClassLoader.SetProjectileClass(projectile, value);
        }

        public bool CountsAsClass<T>() where T : DamageClass => DamageClassLoader.ProjectileClass(projectile).CountsAsClass(DamageClassLoader.InstanceOf<T>());

        public bool CountsAsClass(DamageClass damageClass) => DamageClassLoader.ProjectileClass(projectile).CountsAsClass(damageClass);
    }

    private static Texture2D? OwnTexture(Projectile proj)
    {
        var table = Terraria.GameContent.TextureAssets.Projectile;
        if (proj.type < 0 || proj.type >= table.Length) return null;
        return table[proj.type]?.Value;
    }
}
