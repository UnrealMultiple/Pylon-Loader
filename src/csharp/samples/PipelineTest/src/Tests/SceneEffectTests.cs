using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：SceneEffect —— 真机验证。
/// Ready：两个探针进了内容表、安装期解析了音乐槽、基类默认值（Music = -1、Priority = 0）、群落也在这张表里。
/// World：选赢家（优先级 → 权重 → 平手确定）、音乐落地（写 newMusic / -1 不选 / 0 静音）、
/// SpecialVisuals 每帧含未激活、三条失败语义（问句抛 / 权重抛 / 回调抛）。
/// 断言全看 Main.newMusic：那是"谁赢了"唯一能从外部看见的结果，比读内部当前赢家更接近玩家实际感知。
/// </summary>
internal static class SceneEffectTests
{
    public static void Register()
    {
        PT.Group("sceneeffect", Phase.Ready, RunReady);
        PT.Group("sceneeffect-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("SCENE-R1", c =>
        {
            var all = ModContent.GetContent<ModSceneEffect>();
            c.NotNull(all, "场景效果内容表");

            var a = ModContent.GetInstance<TestSceneEffectA>();
            var b = ModContent.GetInstance<TestSceneEffectB>();
            var plain = ModContent.GetInstance<TestSceneEffectPlain>();

            c.True(all.Contains(a) && all.Contains(b) && all.Contains(plain),
                $"两个探针 + 空白效果都在表里（表长 {all.Count}）");
        });

        PT.Case("SCENE-R2", c =>
        {
            // 槽位在 SetStaticDefaults 里解析：现在应当是两块真实文件各占一个槽（原版 105 之后）。
            c.True(TestSceneEffectProbe.StaticDefaultsDone, "SetStaticDefaults 已跑过");
            c.True(TestSceneEffectProbe.SlotA > 0, $"A 的音乐槽={TestSceneEffectProbe.SlotA}（assets/music/test）");
            c.True(TestSceneEffectProbe.SlotB > 0, $"B 的音乐槽={TestSceneEffectProbe.SlotB}（assets/music/test2）");
            c.True(TestSceneEffectProbe.SlotA != TestSceneEffectProbe.SlotB, "两个槽不相同");
        });

        PT.Case("SCENE-R3", c =>
        {
            var plain = ModContent.GetInstance<TestSceneEffectPlain>();
            var player = Main.player[Main.myPlayer];

            c.Eq(plain.Music, -1, "默认 Music = -1（不选，交回游戏）");
            c.Eq(plain.Priority, 0, "默认 Priority = 0");
            c.Eq(plain.GetWeight(player), 0.5f, "默认权重");
            c.True(!plain.IsSceneEffectActive(player), "默认不激活");
            c.True(plain.MapBackground is null, "默认没有全屏地图背景");
            c.True(!plain.MapBackgroundFullbright, "默认不全亮");
            c.True(plain.WaterStyle is null && plain.SurfaceBackgroundStyle is null &&
                   plain.UndergroundBackgroundStyle is null, "默认不换水/背景");

            c.NoThrow(() =>
            {
                plain.SpecialVisuals(player, false);
                plain.SpecialVisuals(player, true);
                var color = Color.White;
                plain.MapBackgroundColor(ref color);
                c.True(color == Color.White, "默认 MapBackgroundColor 不改色");
            }, "基类回调默认实现");
        });

        PT.Case("SCENE-R4", c =>
        {
            // 群落也是场景效果，进同一张表；它的优先级/静音有自己的默认值（由 biome 组细验）。
            var all = ModContent.GetContent<ModSceneEffect>();
            var biome = ModContent.GetInstance<TestBiome>();
            c.True(biome is not null && all.Contains(biome), "群落与普通场景效果同表");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("SCENE-W1", "玩家还没就绪");
            return;
        }


        TestSceneEffectProbe.Reset();
        MusicLoader.StopMusic();
        TestSceneEffectProbe.UseVanillaMusic = true;
        TestSceneEffectProbe.AActive = true; // 只有 A 激活，权重 1、优先级 100

        // W1：赢家把音乐写进 newMusic。之后把 B 的优先级抬高一档（权重仍是 0）。
        PT.Later("SCENE-W1", 160, c =>
        {
            c.Eq(Main.newMusic, MusicID.OverworldDay, "赢家的 Music 写进了 Main.newMusic");
            TestSceneEffectProbe.BActive = true;
            TestSceneEffectProbe.BPriority = TestSceneEffectProbe.APriority + 1;
            TestSceneEffectProbe.BWeight = 0f;
        });

        // W2：优先级高者赢，哪怕权重是 0（把两条判据分开验）。之后把优先级拉平，让权重说话。
        PT.Later("SCENE-W2", 163, c =>
        {
            c.Eq(Main.newMusic, MusicID.Eerie, "优先级高者赢（权重为 0 也算赢）");
            TestSceneEffectProbe.BPriority = TestSceneEffectProbe.APriority;
            TestSceneEffectProbe.AWeight = 1f;
            TestSceneEffectProbe.BWeight = 0f;
        });

        // W3：同优先级比权重。之后把权重也拉平，进平手。
        PT.Later("SCENE-W3", 166, c =>
        {
            c.Eq(Main.newMusic, MusicID.OverworldDay, "同优先级时权重大者赢");
            TestSceneEffectProbe.BWeight = TestSceneEffectProbe.AWeight;
        });

        // W4/W5：优先级与权重都平手时结果是确定的：两个之一赢，且不随帧抖动。
        PT.Later("SCENE-W4", 169, c =>
        {
            var winner = Main.newMusic;
            TestSceneEffectProbe.TieValue = winner;
            c.True(winner == MusicID.OverworldDay || winner == MusicID.Eerie,
                $"平手时仍是两个之一赢（赢家槽={winner}）");
        });

        PT.Later("SCENE-W5", 172, c =>
        {
            c.Eq(Main.newMusic, TestSceneEffectProbe.TieValue, "平手结果每帧一致（先注册的赢，不抖动）");
            TestSceneEffectProbe.AActive = false;
            TestSceneEffectProbe.BActive = false;
        });

        // W6：没有赢家就不写 newMusic（原版这一帧的决定留着）。之后只让 B 激活，音乐改成 -1。
        PT.Later("SCENE-W6", 175, c =>
        {
            var vanilla = Main.newMusic;
            c.True(vanilla != MusicID.OverworldDay && vanilla != MusicID.Eerie,
                $"没有赢家时不写 newMusic（原版值={vanilla}）");
            TestSceneEffectProbe.BActive = true;
            TestSceneEffectProbe.BPriority = 100;
            TestSceneEffectProbe.BWeight = 1f;
            TestSceneEffectProbe.BMusic = -1;
        });

        // W7：Music = -1 = 不选，交回游戏。之后改成 0（静音）。
        PT.Later("SCENE-W7", 178, c =>
        {
            c.True(Main.newMusic != MusicID.Eerie,
                $"Music = -1 时不写（newMusic={Main.newMusic}）");
            TestSceneEffectProbe.BMusic = 0;
        });

        // W8：Music = 0 = 静音，确实写进去了（原版这帧本来有曲子，被写成 0 才说明是我们的决定）。
        PT.Later("SCENE-W8", 181, c =>
        {
            c.Eq(Main.newMusic, 0, "Music = 0 静音生效");
            TestSceneEffectProbe.BMusic = null;
            TestSceneEffectProbe.ThrowActiveB = true;
            MusicLoader.StopMusic();
        TestSceneEffectProbe.UseVanillaMusic = true;
        TestSceneEffectProbe.AActive = true;
            TestSceneEffectProbe.APriority = 100;
            TestSceneEffectProbe.AWeight = 1f;
        });

        // W9：IsSceneEffectActive 抛异常 → 这一次当未激活（B 出局），A 照常赢，不连坐。
        PT.Later("SCENE-W9", 184, c =>
        {
            c.Eq(Main.newMusic, MusicID.OverworldDay, "问句抛异常的赢家被当未激活");
            TestSceneEffectProbe.ThrowActiveB = false;
            TestSceneEffectProbe.ThrowWeightB = true;
            TestSceneEffectProbe.BWeight = 1f; // 若异常没被吞，B 会赢
        });

        // W10：GetWeight 抛异常 → 用 0 参与比较，权重 1 的 A 赢。
        PT.Later("SCENE-W10", 187, c =>
        {
            c.Eq(Main.newMusic, MusicID.OverworldDay, "权重抛异常用 0 比较");
            TestSceneEffectProbe.ThrowWeightB = false;
            TestSceneEffectProbe.ThrowVisualsA = true;
            TestSceneEffectProbe.Marker = TestSceneEffectProbe.VisualsOnB;
        });

        // W11/W12：A 的 SpecialVisuals 抛异常之后，派发继续走到 B（每帧都在涨）。
        PT.Later("SCENE-W11", 190, c =>
        {
            c.True(TestSceneEffectProbe.ThrowVisualsA, "A 已在注入异常");
            TestSceneEffectProbe.Marker = TestSceneEffectProbe.VisualsOnB;
        });

        PT.Later("SCENE-W12", 193, c =>
        {
            c.True(TestSceneEffectProbe.VisualsOnB > TestSceneEffectProbe.Marker,
                $"A 抛异常后仍派发给下一个（B 次数 {TestSceneEffectProbe.Marker}→{TestSceneEffectProbe.VisualsOnB}）");
            TestSceneEffectProbe.ThrowVisualsA = false;
            TestSceneEffectProbe.AActive = false;
            TestSceneEffectProbe.BActive = false;
        });

        // W13：SpecialVisuals 对未激活的也调（isActive=false），两条问句每帧都被问到。
        PT.Later("SCENE-W13", 196, c =>
        {
            c.True(TestSceneEffectProbe.VisualsOffA > 0 && TestSceneEffectProbe.VisualsOnA > 0,
                $"未激活也调 SpecialVisuals（on={TestSceneEffectProbe.VisualsOnA} off={TestSceneEffectProbe.VisualsOffA}）");
            c.True(TestSceneEffectProbe.ActiveCallsA > 0 && TestSceneEffectProbe.WeightCallsA > 0,
                $"每帧确实问了 IsSceneEffectActive({TestSceneEffectProbe.ActiveCallsA}) 与 GetWeight({TestSceneEffectProbe.WeightCallsA})");
        });

        // W14：探针退出后把决定权交还原版。
        PT.Later("SCENE-W14", 199, c =>
        {
            c.True(Main.newMusic != MusicID.OverworldDay && Main.newMusic != MusicID.Eerie,
                $"探针退出后交还原版（newMusic={Main.newMusic}）");
            TestSceneEffectProbe.Reset();
        });
    }
}
