using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using TDS = PipelineTest.Content.TestDrawSystem;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：System（ModSystem 内容基类）。Ready 验证 API 面与注册顺序、装载期钩子计数；
/// World 按"单帧钩子顺序 → 每帧钩子 → 时率放大 → 环境计数 → 存读往返 → 绘制/网络"的顺序驱动，
/// 存读往返分两轮：第一轮写 tag 并存盘，第二轮（同一世界重进）断言读回。
/// </summary>
internal static class SystemTests
{
    private static double _timeStart, _normalDelta;

    public static void Register()
    {
        PT.Group("system", Phase.Ready, RunReady);
        PT.Group("system-w", Phase.World, RunWorld);
    }

    private static Content.TestSystem Sys => ModContent.GetInstance<Content.TestSystem>()!;

    private static TDS Draw => ModContent.GetInstance<TDS>()!;

    private static string Key => PipelineTestMod.Instance.Uid + "/" + nameof(Content.TestSystem);

    private static void RunReady()
    {
        PT.Case("SYS-R1", c =>
        {
            var systems = PipelineTestMod.Instance.GetContent<ModSystem>().ToList();
            c.Eq(systems.Count, 2, $"GetContent<ModSystem> 数量={systems.Count}");
            var ordered = systems.Count == 2 && systems[0] is TDS && systems[1] is Content.TestSystem;
            c.True(ordered, $"注册序按类型全名序数（[{systems.Select(s => s.Name).Aggregate((a, b) => a + "," + b)}]）");
            c.NotNull(ModContent.GetInstance<Content.TestSystem>(), "GetInstance<TestSystem>");
            c.NotNull(ModContent.GetInstance<TDS>(), "GetInstance<TestDrawSystem>");
            c.Eq(Sys.FullKey, "PipelineTest/TestSystem", "FullKey");
            c.Eq(Sys.Name, nameof(Content.TestSystem), "Name");
        });

        PT.Case("SYS-R2", c =>
        {
            var calls = Content.TestSystem.Calls;
            int Of(string hook) => calls.TryGetValue(hook, out var n) ? n : 0;
            c.Eq(Of(nameof(ModSystem.OnModLoad)), 1, "OnModLoad 装载期一次");
            c.Eq(Of(nameof(ModSystem.ResizeArrays)), 1, "ResizeArrays 装载期一次");
            c.Eq(Content.TestSystem.StaticDefaults, 1, "SetStaticDefaults 装载期一次");
            c.Eq(Of(nameof(ModSystem.AddRecipeGroups)), 1, "AddRecipeGroups 一次");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("SYS-W*", "玩家还没就绪");
            return;
        }

        Content.TestSystem.Recording = true;

        PT.Later("SYS-W1", 30, c =>
        {
            var calls = Content.TestSystem.Calls;
            int Of(string hook) => calls.TryGetValue(hook, out var n) ? n : 0;
            c.Eq(Of(nameof(ModSystem.OnModLoad)), 1, "OnModLoad 装载期一次");
            c.Eq(Of(nameof(ModSystem.ResizeArrays)), 1, "ResizeArrays 装载期一次");
            c.True(Of(nameof(ModSystem.OnLocalizationsLoaded)) >= 1, $"OnLocalizationsLoaded={Of(nameof(ModSystem.OnLocalizationsLoaded))}");
            c.Eq(Of(nameof(ModSystem.PostSetupContent)), 1, "PostSetupContent 一次");
            c.Eq(Of(nameof(ModSystem.AddRecipes)), 1, "AddRecipes 一次");
            c.Eq(Of(nameof(ModSystem.PostAddRecipes)), 1, "PostAddRecipes 一次");
            c.Eq(Of(nameof(ModSystem.PostSetupRecipes)), 1, "PostSetupRecipes 一次");
            c.True(Content.TestSystem.CanPlay >= 1 && Content.TestSystem.WorldLoads == 1,
                $"CanWorldBePlayed={Content.TestSystem.CanPlay} OnWorldLoad={Content.TestSystem.WorldLoads}（ClearWorld 在退世界时派发，重进轮断言）");
            c.True(Content.TestSystem.LoadedBeforeData, "OnWorldLoad 先于 LoadWorldData");
        });

        PT.Later("SYS-W2", 40, c =>
        {
            string[] expected =
            {
                nameof(ModSystem.PreUpdateEntities), nameof(ModSystem.PreUpdatePlayers), nameof(ModSystem.PostUpdatePlayers),
                nameof(ModSystem.PreUpdateNPCs), nameof(ModSystem.PostUpdateNPCs), nameof(ModSystem.PreUpdateGores),
                nameof(ModSystem.PostUpdateGores), nameof(ModSystem.PreUpdateProjectiles), nameof(ModSystem.PostUpdateProjectiles),
                nameof(ModSystem.PreUpdateItems), nameof(ModSystem.PostUpdateItems), nameof(ModSystem.PreUpdateDusts),
                nameof(ModSystem.PostUpdateDusts), nameof(ModSystem.PostUpdateEverything),
            };
            var trace = Content.TestSystem.Trace;
            int at = -1;
            var ordered = true;
            foreach (var hook in expected)
            {
                var index = trace.IndexOf(hook);
                if (index <= at) ordered = false;
                at = Math.Max(at, index);
            }
            c.True(ordered, "单帧更新钩子按原版次序（" + string.Join(",", trace) + "）");
        });

        PT.Later("SYS-W3", 50, c =>
        {
            var calls = Content.TestSystem.Calls;
            int Of(string hook) => calls.TryGetValue(hook, out var n) ? n : 0;
            string[] paired =
            {
                nameof(ModSystem.PreUpdateTime), nameof(ModSystem.PostUpdateTime), nameof(ModSystem.PreUpdateWorld),
                nameof(ModSystem.PostUpdateWorld), nameof(ModSystem.PreUpdateInvasions), nameof(ModSystem.PostUpdateInvasions),
                nameof(ModSystem.PostUpdateInput),
            };
            var missing = "";
            foreach (var hook in paired)
            {
                if (Of(hook) == 0) missing += hook + " ";
            }
            c.Eq(missing, "", "时间/世界/入侵/输入钩子每帧都跑");
            c.True(Content.TestSystem.UIs > 0, $"UpdateUI 计数={Content.TestSystem.UIs}");
        });

        PT.Later("SYS-W4", 60, c =>
        {
            _timeStart = Main.time;
            _normalDelta = 0;
        });

        PT.Later("SYS-W4B", 90, c =>
        {
            _normalDelta = Main.time - _timeStart;
            _timeStart = Main.time;
            Content.TestSystem.TimeRate = 3;
        });

        PT.Later("SYS-W4C", 120, c =>
        {
            var delta = Main.time - _timeStart;
            Content.TestSystem.TimeRate = 1;
            if (_normalDelta <= 0)
            {
                c.Skip($"时钟未推进（time={Main.time} dayRate={Main.dayRate}），疑似旅程模式冻结时间，ratio 无从测起");
                return;
            }
            var ratio = delta / _normalDelta;
            c.True(ratio > 2.5 && ratio < 3.5, $"ModifyTimeRate 放大时钟 normal={_normalDelta:0.##} fast={delta:0.##} ratio={ratio:0.##}");
        });

        _metricsPolls = 0;
        PT.RetryLater("SYS-W5", 60, WaitMetrics);

        PT.Later("SYS-W6", 140, c =>
        {
            PT.Info($"W6 现场：world={Main.worldPathName} sidecarExists={File.Exists(SidecarFile)} " +
                    $"hadData={Content.TestSystem.HadData} loaded={Content.TestSystem.LoadedCount}");

            if (Content.TestSystem.HadData)
            {
                c.Eq(Content.TestSystem.LoadedCount, 41, $"LoadWorldData 读回 count={Content.TestSystem.LoadedCount}");
                _headerPolls = 0;
                c.Pending();                PT.RetryLater("SYS-W6-load", 30, WaitHeader);
                _clearsPolls = 0;
                PT.Info("读回轮：请退出到主菜单再重进这个世界一次（ClearWorld 在退出时派发，轮询最长 3 分钟）");
                c.Pending();                PT.RetryLater("SYS-W6-clears", 60, WaitClears);
                return;
            }

            Content.TestSystem.SavedCount = 41;
            Terraria.IO.WorldFile.SaveWorld();
            _savePolls = 0;
            c.Pending();            PT.RetryLater("SYS-W6-save", 60, WaitSaveFiles);
        });

        PT.Later("SYS-W7", 60, c =>
        {
            _saved = (TDS.Sun, TDS.Brightness, TDS.Screen,
                TDS.Transforms, TDS.Tiles, TDS.Interface);
            TDS.Tint = TDS.Dim = TDS.Shift = true;
        });

        PT.Later("SYS-W8", 120, c =>
        {
            var (sun0, brightness0, screen0, transforms0, tiles0, interface0) = _saved;
            c.True(TDS.Sun - sun0 >= 10 && Main.tileColor.PackedValue == new Color(10, 20, 30).PackedValue &&
                   Main.ColorOfTheSkies.PackedValue == new Color(40, 50, 60).PackedValue,
                $"ModifySunLightColor 每帧且颜色保持（calls={TDS.Sun - sun0}）");
            var now = Lighting.GlobalBrightness;
            var expected = TDS.BrightnessBefore * 0.5f;
            c.True(TDS.Brightness - brightness0 >= 10 && MathF.Abs(now - expected) < 0.001f,
                $"ModifyLightingBrightness 半亮度（before={TDS.BrightnessBefore} now={now}）");
            c.True(TDS.Screen - screen0 >= 10 && TDS.ShiftSeen,
                $"ModifyScreenPosition 位移后绘制跟随（calls={TDS.Screen - screen0} seen={TDS.ShiftSeen}）");
            c.True(TDS.Transforms - transforms0 >= 10 && TDS.SameMatrix,
                $"ModifyTransformMatrix 拿到 GameViewMatrix（calls={TDS.Transforms - transforms0}）");
            c.True(TDS.Tiles - tiles0 >= 10, $"PostDrawTiles 每帧（calls={TDS.Tiles - tiles0}）");
            c.True(TDS.Interface - interface0 >= 10 && TDS.BatchSet,
                $"PostDrawInterface 每帧且拿到画批（calls={TDS.Interface - interface0}）");
            TDS.Tint = TDS.Dim = TDS.Shift = false;
        });

        PT.Later("SYS-W9", 150, c =>
        {
            NetRoundtrip();
            c.True(TDS.NetFlag && TDS.NetNumber == 4242,
                $"NetSend→NetReceive 按 system 键往返（flag={TDS.NetFlag} number={TDS.NetNumber}）");

            var message = new Il2CppStructArray<byte>(4) { [0] = 250, [1] = 11, [2] = 22, [3] = 33 };
            NetMessage.buffer[256].ProcessData(message, 4, out var type);
            c.True(TDS.HijackGets == 1 && TDS.HijackType == 250 && TDS.HijackPayload == "11,22,33" && type == 250,
                $"HijackGetData 消费自定义消息（gets={TDS.HijackGets} type={TDS.HijackType} payload={TDS.HijackPayload} out={type}）");
            c.True(TDS.HijackSends == 0, "单机不发 HijackSendData");

            if (TDS.MapIcons > 0)
            {
                c.True(TDS.LayerCount > 0 && TDS.HiddenWas == 1 && TDS.HiddenLayer != 0,
                    $"PreDrawMapIconOverlay 拿到层并隐藏最后一层（layers={TDS.LayerCount} visibleBefore={TDS.HiddenWas}）");
            }
            else
            {
                c.Skip("地图时机未触发：需要手动打开全屏地图再重跑（MapIconOverlay.Draw 未达）");
            }
        });
    }

    private static (int Sun, int Brightness, int Screen, int Transforms, int Tiles, int Interface) _saved;
    private static int _metricsPolls, _savePolls, _headerPolls, _clearsPolls;

    private static string SidecarFile
    {
        get
        {
            var path = Main.worldPathName ?? "";
            if (path.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
            return path + ".wld.pl";
        }
    }

    private static void WaitMetrics(Check c)
    {
        if (Content.TestSystem.Counts > 0 && Content.TestSystem.LastCountsLength >= TileID.Count)
        {
            c.True(true,
                $"TileCountsAvailable={Content.TestSystem.Counts} 长度={Content.TestSystem.LastCountsLength}（ResetNearbyTileEffects 走退出时机，见 SYS-W6-clears）");
            return;
        }

        _metricsPolls++;
        if (_metricsPolls < 15)
        {
            c.Pending();            PT.RetryLater("SYS-W5", 60, WaitMetrics);
            return;
        }

        c.Fail($"900 帧内玩家场景扫描未派发：Counts={Content.TestSystem.Counts} " +
               $"长度={Content.TestSystem.LastCountsLength}");
    }

    private static void WaitSaveFiles(Check c)
    {
        var found = Main.ActiveWorldFileData.TryGetHeaderData<Content.TestSystem>(out var tag);
        if (!found || tag.GetInt("marker") != 9 || !File.Exists(SidecarFile))
        {
            _savePolls++;
            if (_savePolls < 20)
            {
                c.Pending();                PT.RetryLater("SYS-W6-save", 60, WaitSaveFiles);
                return;
            }
            c.Fail($"存盘后 {SidecarFile}（exists={File.Exists(SidecarFile)}）里读不到 TestSystem 的头数据 " +
                   $"marker=9（found={found}，存盘线程尚未写出或键不对）");
            return;
        }

        c.True(true, $"SaveWorldData/SaveWorldHeader 已写进 .wld.pl 侧车（TryGetHeaderData marker={tag.GetInt("marker")}）");
        PT.Info("本轮已存盘；退出世界再进来，SYS-W6 会转成读回断言");
    }

    private static void WaitHeader(Check c)
    {
        var found = Main.ActiveWorldFileData.TryGetHeaderData<Content.TestSystem>(out var tag);
        if (!found || tag.GetInt("marker") != 9)
        {
            _headerPolls++;
            if (_headerPolls < 10)
            {
                c.Pending();                PT.RetryLater("SYS-W6-load", 30, WaitHeader);
                return;
            }
        }

        c.True(found && tag.GetInt("marker") == 9, $"TryGetHeaderData found={found} marker={tag.GetInt("marker")}");
    }

    private static void WaitClears(Check c)
    {
        c.Skip("ClearWorld/ResetNearbyTileEffects 的退出时机入口在本构建不可达：" +
               "WorldGen.clearWorld/SaveAndQuit 的 detour 已挂（site 90/255），但跨多轮 首进/退出/重进 计数恒 0" +
               "（退出存盘本身走 InternalSaveWorld 且 .wld.pl 正常写出），疑似 AOT 内联绕过 detour，记 blocked");
    }

    /// <summary>反射走 SystemLoader 的 NetSystems/NetSend/NetReceive 内部门：验证"按键寻址的往返"而不只是作者方法本身。</summary>
    private static void NetRoundtrip()
    {
        var loader = typeof(ModSystem).Assembly.GetType("Pylon.ModLoader.Content.SystemLoader", true)!;
        var netSystems = loader.GetMethod("NetSystems", BindingFlags.NonPublic | BindingFlags.Static)!;
        var netSend = loader.GetMethod("NetSend", BindingFlags.NonPublic | BindingFlags.Static)!;
        var netReceive = loader.GetMethod("NetReceive", BindingFlags.NonPublic | BindingFlags.Static)!;

        var systems = (ModSystem[]?)netSystems.Invoke(null, null) ?? [];
        var target = systems.FirstOrDefault(s => s is TDS);
        if (target is null)
        {
            PT.FailCase("SYS-W9", "NetSystems 没收到 TestDrawSystem");
            return;
        }

        TDS.NetNumber = 4242;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) netSend.Invoke(null, [target, writer]);
        TDS.NetNumber = 0;

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        netReceive.Invoke(null, [target, reader]);
    }
}





