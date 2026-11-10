using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

internal static class EnvironmentTests
{
    internal static bool Active;
    internal static int SkyCalls;
    internal static int LightCalls;
    internal static int ProjectileCalls;

    public static void Register() => PT.Group("environment-w", Phase.World, Run);

    internal static void Sky(ref Il2CppMicrosoft.Xna.Framework.Graphics.Color tile,
                             ref Il2CppMicrosoft.Xna.Framework.Graphics.Color background)
    {
        if (!Active) return;
        SkyCalls++;
        tile = new(31, 63, 95, 255);
        background = new(47, 79, 111, 255);
    }

    internal static void Light(ref float scale)
    {
        if (!Active) return;
        LightCalls++;
        scale = 0.5f;
    }

    internal static void Projectiles()
    {
        if (Active) ProjectileCalls++;
    }

    private static void Run()
    {
        var savedTile = Main.tileColor;
        var savedSky = Main.ColorOfTheSkies;
        var savedBrightness = Lighting.GlobalBrightness;
        Active = true;
        try
        {
            PT.Case("ENV-H1", c =>
            {
                var before = SkyCalls;
                Main.ApplyColorOfTheSkiesToTiles();
                c.Eq(SkyCalls, before + 1, "天空色游戏入口分发一次");
                c.Eq((int)Main.tileColor.R, 31, "方块光照色写回");
                c.Eq((int)Main.ColorOfTheSkies.R, 47, "背景色写回");
            });
            PT.Case("ENV-H2", c =>
            {
                var before = LightCalls;
                Lighting.UpdateGlobalBrightness();
                c.Eq(LightCalls, before + 1, "光照游戏入口分发一次");
                c.True(float.IsFinite(Lighting.GlobalBrightness), "全局亮度保持有限");
            });
            PT.Case("ENV-H3", c =>
            {
                var before = ProjectileCalls;
                Main.instance.PreUpdateAllProjectiles();
                c.Eq(ProjectileCalls, before + 1, "弹幕更新前游戏入口分发一次");
            });
        }
        finally
        {
            Active = false;
            Main.tileColor = savedTile;
            Main.ColorOfTheSkies = savedSky;
            Lighting.GlobalBrightness = savedBrightness;
        }
    }
}
