using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss;

internal static class XiheSky
{
    private static Texture2D _gradient;
    private static bool _installed;
    private static float _intensity;

    private static float _sun;
    private static float _black = 1f;

    private static float _wand;

    internal static void Install()
    {
        if (_installed) return;

        On.Terraria.Main.DrawSurfaceBG_BackMountainsStep1 += Draw;
        _installed = true;
    }

    internal static void Tick()
    {
        var boss = XiheBossNpc.Current;
        var active = boss != null && boss.NpcAlive;
        _intensity = MathHelper.Clamp(_intensity + (active ? 0.02f : -0.02f), 0f, 1f);
        if (!active)
        {
            _sun = MathHelper.Max(0f, _sun - 0.02f);
            if (_intensity == 0f) _black = 1f;
            return;
        }

        var eolSunWindow = (boss.TimerM > 2480 && boss.TimerM < 9560) || boss.TimerM > 10620;
        var releaseSun = false;
        var sunType = ModContent.ProjectileType<SunProj>();
        for (var i = 0; i < Main.projectile.Length; i++)
        {
            var p = Main.projectile[i];
            if (p != null && p.active && p.type == sunType)
            {
                releaseSun = true;
                break;
            }
        }
        var sunWindow = eolSunWindow || releaseSun;
        _sun = MathHelper.Clamp(MathHelper.Lerp(_sun, sunWindow ? 1f : 0f,
            sunWindow ? 0.01f : 0.02f), 0f, 1f);

        if (boss.BossState == XiheBossNpc.State.Spawn) _black = 1f;
        else if (boss.BossState == XiheBossNpc.State.Roar) _black = MathHelper.Lerp(_black, 0f, 0.01f);
        else _black = 0f;

        TickWand();
    }

    private static void TickWand()
    {
        var controllerType = ModContent.ProjectileType<Wand.XiheMagicControler>();
        var any = false;
        if (controllerType > 0)
        {
            for (var i = 0; i < Main.projectile.Length; i++)
            {
                var p = Main.projectile[i];
                if (p != null && p.active && p.type == controllerType)
                {
                    any = true;
                    break;
                }
            }
        }

        _wand = MathHelper.Clamp(_wand + (any ? 0.005f : -0.05f), 0f, 1f);
    }

    internal static float WandIntensity => _wand;

    internal static float Intensity => _intensity;
    internal static float SunIntensity => Math.Max(_sun, _wand);
    internal static bool SunVisible => SunIntensity > 0.001f && Math.Max(_intensity, _wand) > 0.001f;

    internal static Vector2 SunScreenPosition()
    {
        var dayLength = Main.dayTime ? 27000f : 16200f;
        var yOffset = (float)(-Main.screenPosition.Y / (Main.worldSurface * 16f - 600f) * 200f);
        var progress = 1f - (float)Main.time / dayLength;
        return new Vector2(
            (1f - progress) * Main.screenWidth / 2f - 100f * progress,
            yOffset + progress * progress * 250f + 180f);
    }

    internal static Vector2 SunUv()
    {
        var p = SunScreenPosition();
        var matrix = Main.BackgroundViewMatrix.TransformationMatrix;
        var translation = matrix.Translation;
        var zoomTranslation = Main.BackgroundViewMatrix.ZoomMatrix.Translation;
        var flipY = ((Enum)Main.BackgroundViewMatrix.Effects).HasFlag(
            (Enum)(object)SpriteEffects.FlipVertically);
        matrix.Translation = translation - zoomTranslation * new Vector3(1f, flipY ? -1f : 1f, 1f);
        var transformed = Vector2.Transform(p, matrix);
        return new Vector2(transformed.X / Math.Max(1, Main.screenWidth),
            1f - transformed.Y / Math.Max(1, Main.screenHeight));
    }

    internal static void Light(ref Color tile, ref Color background)
    {
        if (_intensity > 0f)
        {
            var gray = new Color(30, 30, 30, 255);
            Main.ColorOfTheSkies = Color.Lerp(Main.ColorOfTheSkies, gray, _intensity);
            tile = Color.Lerp(tile, gray, _intensity);
            background = Color.Lerp(background, gray, _intensity);
        }

        if (_wand > 0f)
            Main.ColorOfTheSkies = Color.Lerp(Main.ColorOfTheSkies, new Color(80, 80, 80, 255), _wand);

        var countdown = XiheBossNpc.SpawnCountdown;
        if (countdown >= 0 && countdown < 400)
            Main.ColorOfTheSkies = Color.Lerp(Main.ColorOfTheSkies,
                new Color(40, 40, 40, 200), 1f - countdown / 400f);
    }

    private static void Draw(On.Terraria.Main.orig_DrawSurfaceBG_BackMountainsStep1 orig, Main self,
        float backgroundTopMagicNumber, float bgGlobalScaleMultiplier, int pushBGTopHack)
    {
        orig(self, backgroundTopMagicNumber, bgGlobalScaleMultiplier, pushBGTopHack);
        if (_intensity <= 0f || Main.gameMenu) return;
        _gradient ??= XiheBossMod.Instance.Assets.Request<Texture2D>("assets/textures/TSSky1.png")?.Value;
        if (_gradient == null) return;
        var sb = Main.spriteBatch;
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, new Il2CppSystem.Nullable<Matrix>(Matrix.Identity));
        sb.Draw(_gradient, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.White * (_intensity * 0.8f));
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
            DepthStencilState.None, RasterizerState.CullNone, null, new Il2CppSystem.Nullable<Matrix>(Main.GameViewMatrix.TransformationMatrix));
        if (_black > 0.001f)
            sb.Draw(Terraria.GameContent.TextureAssets.MagicPixel.Value,
                new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.Black * (_black * _intensity));

        if (_wand > 0.001f)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, new Il2CppSystem.Nullable<Matrix>(Matrix.Identity));
            sb.Draw(Terraria.GameContent.TextureAssets.MagicPixel.Value,
                new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.Black * _wand);
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, new Il2CppSystem.Nullable<Matrix>(Matrix.Identity));
            sb.Draw(_gradient, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight),
                new Color(255, 255, 255, 200) * (_wand * 0.9f));
            sb.End();

            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, RasterizerState.CullNone, null,
                new Il2CppSystem.Nullable<Matrix>(Main.GameViewMatrix.TransformationMatrix));
        }
    }

    internal static void Reset() { _intensity = 0f; _sun = 0f; _black = 1f; _wand = 0f; }
}
