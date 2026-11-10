using System;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>
/// 场景效果探针：开关、优先级、权重、音乐、异常注入都在运行时改写。
/// 断言只认 `Main.newMusic` —— 本管线唯一"谁赢了"的可见结果（水/背景那条另由 water、background 组验）。
/// </summary>
public static class TestSceneEffectProbe
{
    public static bool AActive;
    public static bool BActive;

    public static int APriority = 100;
    public static int BPriority = 100;

    public static float AWeight = 1f;
    public static float BWeight;

    /// <summary>安装期解析出的两个音乐槽：A 固定用 test，B 用 test2。</summary>
    public static int SlotA;
    public static int SlotB;
    public static bool UseVanillaMusic;

    /// <summary>B 的音乐覆盖值：null = 用 SlotB；-1 = 不选；0 = 静音。</summary>
    public static int? BMusic;

    // ---- 异常注入：验"当未激活 / 当 0 / 记一次继续下一个" ----
    public static bool ThrowActiveB;
    public static bool ThrowWeightB;
    public static bool ThrowVisualsA;

    // ---- 计数：两个问句是不是真被每帧问到、SpecialVisuals 是不是每帧都派发 ----
    public static int ActiveCallsA, ActiveCallsB;
    public static int WeightCallsA, WeightCallsB;
    public static int VisualsOnA, VisualsOffA, VisualsOnB, VisualsOffB;

    /// <summary>跨用例传值用（平手读到的赢家、计数快照）。</summary>
    public static int TieValue = int.MinValue;
    public static int Marker;

    public static bool StaticDefaultsDone;

    /// <summary>background 组用：让探针 A 声明表层背景风格。</summary>
    public static bool UseBackground;

    public static void Reset()
    {
        AActive = false;
        BActive = false;
        APriority = 100;
        BPriority = 100;
        AWeight = 1f;
        BWeight = 0f;
        BMusic = null;
        UseVanillaMusic = false;
        ThrowActiveB = false;
        ThrowWeightB = false;
        ThrowVisualsA = false;
        ActiveCallsA = ActiveCallsB = 0;
        WeightCallsA = WeightCallsB = 0;
        VisualsOnA = VisualsOffA = VisualsOnB = VisualsOffB = 0;
        TieValue = int.MinValue;
        Marker = 0;
    }
}

/// <summary>探针 A：音乐固定是 assets/music/test 的槽，用来在日志/断言里认出"它赢了"。</summary>
public sealed class TestSceneEffectA : ModSceneEffect
{
    public override void SetStaticDefaults()
    {
        // 槽位只在安装期解析一次：每帧 DecideMusic 都会读 Music，不能每次都进表。
        TestSceneEffectProbe.SlotA = MusicLoader.GetMusicSlot(Mod, "assets/music/test");
        TestSceneEffectProbe.SlotB = MusicLoader.GetMusicSlot(Mod, "assets/music/test2");
        TestSceneEffectProbe.StaticDefaultsDone = true;
    }

    public override int Priority => TestSceneEffectProbe.APriority;

    public override int Music => TestSceneEffectProbe.UseVanillaMusic ? Terraria.ID.MusicID.OverworldDay : TestSceneEffectProbe.SlotA;

    public override ModSurfaceBackgroundStyle SurfaceBackgroundStyle =>
        TestSceneEffectProbe.UseBackground ? ModContent.GetInstance<TestSurfaceBackgroundStyle>() : null!;

    public override bool IsSceneEffectActive(Player player)
    {
        TestSceneEffectProbe.ActiveCallsA++;
        return TestSceneEffectProbe.AActive;
    }

    public override float GetWeight(Player player)
    {
        TestSceneEffectProbe.WeightCallsA++;
        return TestSceneEffectProbe.AWeight;
    }

    public override void SpecialVisuals(Player player, bool isActive)
    {
        if (!isActive)
        {
            TestSceneEffectProbe.VisualsOffA++;
            return;
        }

        TestSceneEffectProbe.VisualsOnA++;

        if (TestSceneEffectProbe.ThrowVisualsA)
            throw new InvalidOperationException("场景效果探针：SpecialVisuals 故意抛异常");
    }
}

/// <summary>探针 B：音乐槽可被测试改成 -1（不选）/0（静音），用来验这两种语义。</summary>
public sealed class TestSceneEffectB : ModSceneEffect
{
    public override int Priority => TestSceneEffectProbe.BPriority;

    public override int Music => TestSceneEffectProbe.BMusic ??
        (TestSceneEffectProbe.UseVanillaMusic ? Terraria.ID.MusicID.Eerie : TestSceneEffectProbe.SlotB);

    public override bool IsSceneEffectActive(Player player)
    {
        TestSceneEffectProbe.ActiveCallsB++;

        if (TestSceneEffectProbe.ThrowActiveB)
            throw new InvalidOperationException("场景效果探针：IsSceneEffectActive 故意抛异常");

        return TestSceneEffectProbe.BActive;
    }

    public override float GetWeight(Player player)
    {
        TestSceneEffectProbe.WeightCallsB++;

        if (TestSceneEffectProbe.ThrowWeightB)
            throw new InvalidOperationException("场景效果探针：GetWeight 故意抛异常");

        return TestSceneEffectProbe.BWeight;
    }

    public override void SpecialVisuals(Player player, bool isActive)
    {
        if (isActive) TestSceneEffectProbe.VisualsOnB++;
        else TestSceneEffectProbe.VisualsOffB++;
    }
}

/// <summary>什么都不覆写：用来对基类默认值（尤其是 Music = -1 "不选" 与 Priority = 0）。</summary>
public sealed class TestSceneEffectPlain : ModSceneEffect
{
}