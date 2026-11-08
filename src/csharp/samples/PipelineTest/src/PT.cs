using System;
using System.Collections.Generic;

namespace PipelineTest;

/// <summary>测试阶段：Load=T0（注册面）、Ready=T1（安装面，游戏表已就绪）、World=T2（进世界后）、Tick=T3（每帧）。</summary>
internal enum Phase
{
    Load,
    Ready,
    World,
    Tick,
}

/// <summary>
/// [PT] 日志协议 + 组/阶段调度。
/// 判定只认日志行：BOOT/START/PASS/FAIL/SKIP/GROUP/END。
/// 断言失败不抛（Check 记 FAIL 并继续），异常由 Case 兜住记 FAIL。
/// </summary>
internal static class PT
{
    // ---- 汇总计数 ----
    public static int Pass, Fail, Skip;

    /// <summary>组开关：tests.txt 里的组名（null = 全部跑）。</summary>
    public static HashSet<string>? Enabled;

    private static Pylon.ModLoader.IModLog? _log;
    public static void Bind(Pylon.ModLoader.IModLog log) => _log = log;

    private static void Log(string message) => _log?.Info(message);

    /// <summary>用例之外的说明性日志（也走 [PT] 前缀，能被过滤到）。</summary>
    public static void Info(string message) => Log("[PT] " + message);

    // ---- 组注册与执行 ----

    private sealed class GroupDef
    {
        public string Name = "";
        public Phase Phase;
        public Action Body = static () => { };
        public bool Done;
    }

    private static readonly List<GroupDef> All = [];

    /// <summary>注册一个组。必须在各阶段入口之前调用（Load() 里注册全部）。</summary>
    public static void Group(string name, Phase phase, Action body) =>
        All.Add(new GroupDef { Name = name, Phase = phase, Body = body });

    /// <summary>延迟用例：世界阶段之后的第 frames 帧跑（给"等一次场景扫描/动画"这类用例用）。</summary>
    private sealed class DeferredCase
    {
        public string Id = "";
        public int Frames;
        public Action<Check> Body = static _ => { };
    }

    private static readonly List<DeferredCase> Deferred = [];

    /// <summary>安排一个 frames 帧之后才跑的用例；由 <see cref="RunTick"/> 每帧推进。</summary>
    public static void Later(string id, int frames, Action<Check> body) =>
        Deferred.Add(new DeferredCase { Id = id, Frames = Math.Max(1, frames), Body = body });

    /// <summary>每帧调用：推进所有延迟用例。世界跑过之后才由 TestPlayer.PostUpdate 调用。</summary>
    public static void RunTick()
    {
        for (var i = Deferred.Count - 1; i >= 0; i--)
        {
            var item = Deferred[i];
            if (--item.Frames > 0) continue;
            Deferred.RemoveAt(i);
            Case(item.Id, item.Body);
        }
    }

    /// <summary>运行某个阶段的全部已启用组；每个组一轮只跑一次。</summary>
    public static void RunPhase(Phase phase)
    {
        foreach (var group in All)
        {
            if (group.Phase != phase || group.Done) continue;
            if (Enabled is { } enabled && !enabled.Contains(group.Name)) continue;

            group.Done = true;
            Log($"[PT] GROUP-START {group.Name}");
            var before = (Pass, Fail, Skip);
            try
            {
                group.Body();
            }
            catch (Exception e)
            {
                Fail++;
                Log($"[PT] FAIL {group.Name}/* 组体异常 {e.GetType().Name}: {e.Message}");
            }
            Log($"[PT] GROUP {group.Name} pass={Pass - before.Pass} fail={Fail - before.Fail} skip={Skip - before.Skip}");
        }
    }

    /// <summary>注册了但这一轮没跑的组（核对组开关用）。</summary>
    public static string PendingGroups()
    {
        var names = new List<string>();
        foreach (var g in All)
        {
            if (!g.Done) names.Add(g.Name);
        }
        return string.Join(",", names);
    }

    // ---- 用例 ----

    /// <summary>一个用例：START → body → 结果行。崩溃时最后一条 START 就是嫌疑用例。</summary>
    public static void Case(string id, Action<Check> body)
    {
        Log($"[PT] START {id}");
        var check = new Check(id);
        try
        {
            body(check);
        }
        catch (Exception e)
        {
            FailCase(id, $"threw {e.GetType().Name}: {e.Message}");
            return;
        }
        if (check.Ok) PassCase(id, check.Detail.Length == 0 ? "ok" : check.Detail);
    }

    public static void PassCase(string id, string detail)
    {
        Pass++;
        Log($"[PT] PASS {id} {detail}");
    }

    public static void FailCase(string id, string detail)
    {
        Fail++;
        Log($"[PT] FAIL {id} {detail}");
    }

    public static void SkipCase(string id, string reason)
    {
        Skip++;
        Log($"[PT] SKIP {id} {reason}");
    }

    // ---- 汇总 ----

    public static void Boot() => Log($"[PT] BOOT groups={All.Count}");

    public static void End() => Log($"[PT] END pass={Pass} fail={Fail} skip={Skip}");
}

/// <summary>断言辅助：失败记 FAIL 并继续（一轮拿全部结果）。</summary>
internal sealed class Check(string id)
{
    public bool Ok = true;
    public string Detail = "";

    public void True(bool condition, string detail)
    {
        if (condition)
        {
            if (Detail.Length == 0) Detail = detail;
            return;
        }
        Ok = false;
        PT.FailCase(id, detail);
    }

    public void Eq<T>(T actual, T expected, string what) =>
        True(EqualityComparer<T>.Default.Equals(actual, expected), $"{what}={actual}（期望 {expected}）");

    public void NotNull(object? value, string what) => True(value is not null, $"{what}=null");

    public void InRange(long value, long min, long max, string what) =>
        True(value >= min && value <= max, $"{what}={value}（应在 {min}..{max}）");

    public void NoThrow(Action action, string what)
    {
        try { action(); if (Detail.Length == 0) Detail = what; }
        catch (Exception e) { Ok = false; PT.FailCase(id, $"{what} threw {e.GetType().Name}: {e.Message}"); }
    }

    public void Fail(string detail)
    {
        Ok = false;
        PT.FailCase(id, detail);
    }
}