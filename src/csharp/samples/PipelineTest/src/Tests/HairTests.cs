using System.IO;
using Pylon.ModLoader;
using Pylon.ModLoader.Textures;
using Terraria;
using Terraria.GameContent;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 15：Hair —— 发号（Base 运行期捕获）、菜单两段式判定、主贴图/戴帽子版贴图进表。
/// 贴图用 40x784（14 帧 × 40x56）的真发型图，尺寸断言能直接抓出占位图。
/// </summary>
internal static class HairTests
{
    public static void Register()
    {
        PT.Group("hair", Phase.Ready, Run);
        PT.Group("hair-w", Phase.World, RunWorld);
    }

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

        // T1：主贴图进表，尺寸必须是真发型图 40x784（14 帧 × 40x56）
        PT.Case("HAIR-T1", c =>
        {
            c.NotNull(hair.Texture, "TestHair.Texture");
            if (hair.Texture is not null)
            {
                PylonTexture.Size(hair.Texture.Value, out var w, out var h);
                c.Eq(w, 40, "主贴图宽");
                c.Eq(h, 784, "主贴图高（14 帧 × 56）");
            }

            var table = TextureAssets.PlayerHair;
            c.True(table is not null && hair.Type < table.Length, $"PlayerHair 表长 {table?.Length} > {hair.Type}");
            if (table is not null && hair.Type < table.Length)
                c.NotNull(table[hair.Type], $"PlayerHair[{hair.Type}]");
        });

        // T2：戴帽子版贴图进 PlayerHairAlt
        PT.Case("HAIR-T2", c =>
        {
            var alt = TextureAssets.PlayerHairAlt;
            c.True(alt is not null && hair.Type < alt.Length, $"PlayerHairAlt 表长 {alt?.Length} > {hair.Type}");
            if (alt is not null && hair.Type < alt.Length)
                c.NotNull(alt[hair.Type], $"PlayerHairAlt[{hair.Type}]");
        });

        // S1：SetStaticDefaults 被调
        PT.Case("HAIR-S1", c =>
        {
            c.True(Content.TestHair.StaticDefaultsRan, $"StaticDefaultsRan={Content.TestHair.StaticDefaultsRan}");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        var hair = ModContent.GetInstance<Content.TestHair>();

        PT.Case("HAIR-W1", c =>
        {
            if (player is null || !player.active) { c.True(false, "玩家还没就绪"); return; }

            player.hair = hair.Type;
            player.hairDye = 0;
            PT.Info($"玩家发型已设为 mod 发型 {hair.Type}（应看到灰色侧分长发，不是纯色条）");
            c.Eq(player.hair, hair.Type, "player.hair");
        });

        // W2：走真实存盘链路 —— .plr 里存 0，名字进玩家侧车；存完内存里的值要恢复。
        PT.Case("HAIR-W2", c =>
        {
            var file = Main.ActivePlayerFileData;
            c.NotNull(file, "Main.ActivePlayerFileData");
            if (file is null) return;

            var path = file.Path;
            var sidecar = path + ".pl.json";

            Player.SavePlayer(file, false, true);
            c.Eq(player.hair, hair.Type, "存盘后内存里的 hair 被恢复");

            c.True(File.Exists(sidecar), $"侧车存在 {sidecar}");
            if (!File.Exists(sidecar)) return;

            var json = File.ReadAllText(sidecar);
            PT.Info($"侧车内容：{json}");
            c.True(json.Contains("pylon:hair"), "侧车写了 pylon:hair");
            c.True(json.Contains(hair.FullKey), $"侧车按名字存（{hair.FullKey}）");
        });
    }
}
