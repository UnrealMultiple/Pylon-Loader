using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace XiheBoss;

// MEAC EoLSky 的原始 TSSky1 渐变在地表背景阶段绘制，避免盖住玩家和弹幕。
internal static class XiheSky
{
    private static Texture2D _gradient;
    private static bool _installed;
    private static float _intensity;
    private static float _black = 1f;

    internal static void Install()
    {
        if (_installed) return;
        // Bunny-Loader 的背景管线在 BackMountainsStep1 后绘制远景；在总入口
        // DrawSurfaceBG 挂载会落到最终背景层，覆盖玩家/弹幕。
        On.Terraria.Main.DrawSurfaceBG_BackMountainsStep1 += Draw;
        _installed = true;
    }

    internal static void Tick()
    {
        var boss = XiheBossNpc.Current;
        var active = boss != null && boss.NpcAlive;
        _intensity = MathHelper.Clamp(_intensity + (active ? 0.02f : -0.02f), 0f, 1f);
        if (!active) { if (_intensity == 0f) _black = 1f; return; }
        if (boss.BossState == XiheBossNpc.State.Spawn) _black = 1f;
        else if (boss.BossState == XiheBossNpc.State.Roar) _black = MathHelper.Lerp(_black, 0f, 0.01f);
        else _black = 0f;
    }

    internal static void Light(ref Color tile, ref Color background)
    {
        if (_intensity <= 0f) return;
        var gray = new Color(30, 30, 30, 255);
        Main.ColorOfTheSkies = Color.Lerp(Main.ColorOfTheSkies, gray, _intensity);
        tile = Color.Lerp(tile, gray, _intensity);
        background = Color.Lerp(background, gray, _intensity);
    }

    private static void Draw(On.Terraria.Main.orig_DrawSurfaceBG_BackMountainsStep1 orig, Main self,
        float backgroundTopMagicNumber, float bgGlobalScaleMultiplier, int pushBGTopHack)
    {
        orig(self, backgroundTopMagicNumber, bgGlobalScaleMultiplier, pushBGTopHack);
        if (_intensity <= 0f || Main.gameMenu) return;
        _gradient ??= ModContent.Request(XiheBossMod.Instance, "assets/textures/TSSky1.png")?.Value;
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
    }

    internal static void Reset() { _intensity = 0f; _black = 1f; }
}
