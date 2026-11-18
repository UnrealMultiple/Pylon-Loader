using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.IO;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.SaveData;
using PipelineTest.Tests;

namespace PipelineTest.Content;

/// <summary>
/// 测试系统 A：装载/世界/更新面。计数每个钩子的调用，Recording 时把一帧内的钩子按序记进 Trace，
/// ModifyTimeRate 按 TimeRate 放大时钟，SaveWorldData/SaveWorldHeader 写入可验证的标记值，
/// 弹幕前探针与提示扫格驱动（原 Mod 虚方法迁移）。
/// </summary>
public sealed class TestSystem : ModSystem
{
    public static int StaticDefaults;
    public static int WorldLoads, Clears, CanPlay, Resets, Counts, LastCountsLength = -1, UIs, SavedCount;
    public static int LoadedCount = -1;
    public static bool LoadedBeforeData, HadData, Recording;
    public static double TimeRate = 1;
    public static readonly List<string> Trace = [];
    public static readonly Dictionary<string, int> Calls = [];

    private static bool _recordingFrame;

    private static void Mark(string hook)
    {
        Calls[hook] = Calls.TryGetValue(hook, out var n) ? n + 1 : 1;
        if (Recording && hook == nameof(PreUpdateEntities)) _recordingFrame = true;
        if (!_recordingFrame) return;
        Trace.Add(hook);
        if (hook != nameof(PostUpdateEverything)) return;
        _recordingFrame = false;
        Recording = false;
    }

    public override void OnModLoad() => Mark(nameof(OnModLoad));

    public override void ResizeArrays() => Mark(nameof(ResizeArrays));

    public override void OnLocalizationsLoaded() => Mark(nameof(OnLocalizationsLoaded));

    public override void SetStaticDefaults() => StaticDefaults++;

    public override void PostSetupContent() => Mark(nameof(PostSetupContent));

    public override void AddRecipeGroups() => Mark(nameof(AddRecipeGroups));

    public override void AddRecipes() => Mark(nameof(AddRecipes));

    public override void PostAddRecipes() => Mark(nameof(PostAddRecipes));

    public override void PostSetupRecipes() => Mark(nameof(PostSetupRecipes));

    public override void OnWorldLoad()
    {
        WorldLoads++;
        LoadedBeforeData = LoadedCount < 0;
    }

    public override void OnWorldUnload() => Mark(nameof(OnWorldUnload));

    public override void PreSaveAndQuit() => Mark(nameof(PreSaveAndQuit));

    public override void ClearWorld() => Mark(nameof(ClearWorld));

    public override bool CanWorldBePlayed(PlayerFileData playerData, WorldFileData worldFileData)
    {
        CanPlay++;
        return true;
    }

    public override void SaveWorldData(TagCompound tag)
    {
        if (SavedCount > 0) tag["count"] = SavedCount;
    }

    public override void LoadWorldData(TagCompound tag)
    {
        HadData = tag.ContainsKey("count");
        LoadedCount = tag.GetInt("count");
    }

    public override void SaveWorldHeader(TagCompound tag) => tag["marker"] = 9;

    public override void PreUpdateEntities() => Mark(nameof(PreUpdateEntities));

    public override void PreUpdatePlayers() => Mark(nameof(PreUpdatePlayers));

    public override void PostUpdatePlayers() => Mark(nameof(PostUpdatePlayers));

    public override void PreUpdateNPCs() => Mark(nameof(PreUpdateNPCs));

    public override void PostUpdateNPCs() => Mark(nameof(PostUpdateNPCs));

    public override void PreUpdateGores() => Mark(nameof(PreUpdateGores));

    public override void PostUpdateGores() => Mark(nameof(PostUpdateGores));

    public override void PreUpdateProjectiles()
    {
        EnvironmentTests.Projectiles();
        Mark(nameof(PreUpdateProjectiles));
    }

    public override void PostUpdateProjectiles() => Mark(nameof(PostUpdateProjectiles));

    public override void PreUpdateItems() => Mark(nameof(PreUpdateItems));

    public override void PostUpdateItems() => Mark(nameof(PostUpdateItems));

    public override void PreUpdateDusts() => Mark(nameof(PreUpdateDusts));

    public override void PostUpdateDusts() => Mark(nameof(PostUpdateDusts));

    public override void PreUpdateTime() => Mark(nameof(PreUpdateTime));

    public override void PostUpdateTime() => Mark(nameof(PostUpdateTime));

    public override void PreUpdateWorld() => Mark(nameof(PreUpdateWorld));

    public override void PostUpdateWorld() => Mark(nameof(PostUpdateWorld));

    public override void PreUpdateInvasions() => Mark(nameof(PreUpdateInvasions));

    public override void PostUpdateInvasions() => Mark(nameof(PostUpdateInvasions));

    public override void PostUpdateEverything()
    {
        if (TooltipSweep) SweepTick();
        Mark(nameof(PostUpdateEverything));
    }

    public static bool TooltipSweep;

    private const int SweepColumns = 16;
    private const int SweepRows = 14;
    private const float SweepWidthRatio = 0.45f;
    private const float SweepTopRatio = 0.24f;
    private const float SweepHeightRatio = 0.36f;
    private static int _sweepFrame;

    private static void SweepTick()
    {
        var width = Math.Max(Main.screenWidth, 1);
        var height = Math.Max(Main.screenHeight, 1);
        var column = _sweepFrame % SweepColumns;
        var row = (_sweepFrame / SweepColumns) % SweepRows;
        Main.mouseX = (int)(column * (width * SweepWidthRatio) / SweepColumns);
        Main.mouseY = (int)((SweepTopRatio * height) + row * (height * SweepHeightRatio) / SweepRows);
        _sweepFrame++;
    }

    public override void PostUpdateInput() => Mark(nameof(PostUpdateInput));

    public override void UpdateUI(GameTime gameTime)
    {
        if (gameTime is null) return;
        UIs++;
    }

    public override void ModifyTimeRate(ref double timeRate, ref double tileUpdateRate, ref double eventUpdateRate) =>
        timeRate *= TimeRate;

    public override void ResetNearbyTileEffects() => Mark(nameof(ResetNearbyTileEffects));

    public override void TileCountsAvailable(ReadOnlySpan<int> tileCounts)
    {
        Counts++;
        LastCountsLength = tileCounts.Length;
    }
}
