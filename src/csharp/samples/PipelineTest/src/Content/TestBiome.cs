using System;
using Terraria;

namespace PipelineTest.Content;

/// <summary>生物群系探针：开关由测试翻，记录进/在/出各被调了几次。</summary>
public sealed class TestBiome : Pylon.ModLoader.ModBiome
{
    /// <summary>本帧算不算"在群落里"。</summary>
    public static bool Active;

    /// <summary>true = IsBiomeActive 抛异常（用来验"抛异常当 false"）。</summary>
    public static bool Throw;

    public static int Enters, Insides, Leaves;

    public static void Reset()
    {
        Active = false;
        Throw = false;
        Enters = Insides = Leaves = 0;
    }

    public override bool IsBiomeActive(Player player)
    {
        if (Throw) throw new InvalidOperationException("生物群系探针故意抛异常");
        return Active;
    }

    public override void OnEnter(Player player) => Enters++;

    public override void OnInBiome(Player player) => Insides++;

    public override void OnLeave(Player player) => Leaves++;
}

/// <summary>第二个群落：只用来确认各群落各算各的标记与回调。</summary>
public sealed class TestBiomeOther : Pylon.ModLoader.ModBiome
{
    public static bool Active;
    public static int Enters, Leaves;

    public override bool IsBiomeActive(Player player) => Active;

    public override void OnEnter(Player player) => Enters++;

    public override void OnLeave(Player player) => Leaves++;
}
