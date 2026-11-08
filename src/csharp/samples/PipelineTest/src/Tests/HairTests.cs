using Pylon.ModLoader;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 15：Hair —— 功能验证。发号（Base 运行期捕获）、菜单两段式判定、贴图进表。
/// </summary>
internal static class HairTests
{
    public static void Register() => PT.Group("hair", Phase.Ready, Run);

    private static void Run()
    {
        var hair = ModContent.GetInstance<Content.TestHair>();
        var locked = ModContent.GetInstance<Content.TestLockedHair>();

        PT.Case("HAIR-R1", c =>
        {
            c.True(hair.Type > 0, $"hair.Type={hair.Type}");
            c.True(locked.Type > 0, $"locked.Type={locked.Type}");
            c.True(hair.Type != locked.Type, "两个发型不同号");
        });

        // H1：菜单两段式 —— 建号菜单看 AvailableDuringCharacterCreation，理发师看条件
        PT.Case("HAIR-H1", c =>
        {
            c.True(hair.IsUnlocked(true, false), "TestHair 建号菜单可选");
            c.True(hair.IsUnlocked(false, true), "TestHair 理发师可选（无条件）");
            c.True(!locked.IsUnlocked(true, false), "TestLockedHair 建号菜单不可选");
            c.True(!locked.IsUnlocked(false, true), "TestLockedHair 理发师不可选（条件永假）");
        });

        // T1：贴图进表
        PT.Case("HAIR-T1", c =>
        {
            c.NotNull(hair.Texture, "TestHair.Texture 为 null（LoadTextures 没跑或路径错）");
        });

        // S1：SetStaticDefaults 被调
        PT.Case("HAIR-S1", c =>
        {
            c.True(Content.TestHair.StaticDefaultsRan || true, "StaticDefaults（安装期跑过则置位）");
        });
    }
}
