using System;
using System.Collections.Generic;

namespace Pylon.ModLoader.Tables;

/// <summary>扩表调度：按各管线声明的表扩到位，同一类表全进程只有一份扩表器。</summary>
internal static class TableMaintenance
{
    internal static readonly List<TableGrower> Active = [];
    private static int _watchTicks;

    internal static TableGrower? Find(TableSet set)
    {
        for (var i = 0; i < Active.Count; i++)
            if (Active[i].Set == set) return Active[i];
        return null;
    }

    /// <summary>全部模块声明的扩表请求。</summary>
    internal static IReadOnlyList<TableRequest> Requests => LoaderRegistry.Tables;

    /// <summary>取这一类表的扩表器，并把目标长度抬到至少 <paramref name="target"/>。</summary>
    internal static TableGrower Ensure(TableSet set, int target)
    {
        var grower = Find(set);
        if (grower is null) return new TableGrower(set, target);
        grower.Ensure(target);
        return grower;
    }

    /// <summary>内容装配前必须扩好的表一次扩完；表还没建就返回 false，下一帧再来。</summary>
    internal static bool PumpEarly(Action<string>? log)
    {
        var ready = true;

        foreach (var request in Requests)
        {
            if (!request.Early) continue;

            var grower = Ensure(request.Set, request.Target());
            if (grower.Vanilla <= 0)
            {
                ready = false;
                continue;
            }

            grower.GrowAll(log);
        }

        return ready;
    }

    /// <summary>每帧补扩，另外每 120 帧查一遍游戏重建。</summary>
    public static int Maintain(Action<string>? log = null, Action<TableSet>? regrown = null)
    {
        if (Active.Count == 0) return 0;

        var grown = 0;
        for (var i = 0; i < Active.Count; i++)
        {
            var n = Active[i].CheckPending(log);
            if (n <= 0) continue;
            grown += n;
            Notify(regrown, Active[i], log);
        }

        if (++_watchTicks >= 120)
        {
            _watchTicks = 0;

            // 各管线声明的表按 Target() 重新取目标：装配期表还没建（Vanilla=0）时
            // 捕获的容量是错的，这里随游戏把表建出来再抬到位并补扫/重扩。
            foreach (var request in Requests)
            {
                if (request.Early) continue;

                var grower = Ensure(request.Set, request.Target());
                var n = grower.Watch(log);
                if (n <= 0) continue;
                grown += n;
                Notify(regrown, grower, log);
            }

            for (var i = 0; i < Active.Count; i++)
            {
                var n = Active[i].Watch(log);
                if (n <= 0) continue;
                grown += n;
                Notify(regrown, Active[i], log);
            }
        }

        return grown;
    }

    private static void Notify(Action<TableSet>? regrown, TableGrower grower, Action<string>? log)
    {
        if (regrown is null) return;
        try { regrown(grower.Set); }
        catch (Exception e) { log?.Invoke($"[tables] {grower.Name} 换表后重写失败：{e.Message}"); }
    }

    /// <summary>自检：某类表里扩到位 / 还是原版 / 还没建 各几张。</summary>
    public static bool Audit(TableSet set, out int total, out int grown, out int stale, out int missing,
                             List<string>? missingNames = null)
    {
        total = grown = stale = missing = 0;
        var grower = Find(set);
        if (grower is null) return false;
        grower.Audit(out total, out grown, out stale, out missing, missingNames);
        return true;
    }
}
