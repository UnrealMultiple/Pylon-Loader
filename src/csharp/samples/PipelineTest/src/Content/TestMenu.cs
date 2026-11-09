#nullable enable
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using ReLogic.Content;
using System;

namespace PipelineTest.Content;

/// <summary>
/// </summary>
public sealed class TestMenu : ModMenu
{
    public static int CtUpdate;
    public static int CtOnSelected;
    public static int CtOnDeselected;
    public static int CtPreDrawLogo;
    public static bool LastOnTitleScreen;

    private static Asset<Texture2D>? _logo;
    private static Asset<Texture2D>? _sun;
    private static Asset<Texture2D>? _moon;

    public static void Reset()
    {
        CtUpdate = CtOnSelected = CtOnDeselected = CtPreDrawLogo = 0;
        LastOnTitleScreen = false;
    }

    public override void SetStaticDefaults()
    {
        _logo = PipelineTestMod.Instance.LoadAsset("assets/items/ExampleModTitle.png");
        _sun = PipelineTestMod.Instance.LoadAsset("assets/items/ExampleSun.png");
        _moon = PipelineTestMod.Instance.LoadAsset("assets/items/ExampliumMoon.png");
    }

    public override Asset<Texture2D>? Logo => _logo;

    public override Asset<Texture2D>? SunTexture => _sun;

    public override Asset<Texture2D>? MoonTexture => _moon;

    public override string DisplayName => "管线测试主题";

    public override void Update(bool isOnTitleScreen)
    {
        CtUpdate++;
        LastOnTitleScreen = isOnTitleScreen;
    }

    public override void OnSelected() => CtOnSelected++;

    public override void OnDeselected() => CtOnDeselected++;

    public override bool PreDrawLogo(SpriteBatch spriteBatch,
        ref Vector2 logoDrawCenter, ref float logoRotation, ref float logoScale,
        ref Color drawColor)
    {
        CtPreDrawLogo++;
        return true;
    }
}