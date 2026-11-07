using System;
using Pylon.ModLoader;

namespace PipelineTest;

/// <summary>[PT] 日志协议：所有断言只以一行日志的形态可见。</summary>
internal static class PT
{
    public static int Pass, Fail, Skip;

    public static void Boot(int groups, int cases) =>
        Log($"[PT] BOOT groups={groups} cases={cases}");

    public static void Case(string id, Action body)
    {
        Log($"[PT] START {id}");
        try
        {
            body();
        }
        catch (Exception e)
        {
            Fail++;
            Log($"[PT] FAIL {id} threw {e.GetType().Name}: {e.Message}");
            return;
        }
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

    public static void End() => Log($"[PT] END pass={Pass} fail={Fail} skip={Skip}");

    internal static Pylon.ModLoader.IModLog Log2;

    private static void Log(string message) => Log2?.Info(message);
}

/// <summary>断言辅助：True/Eq/NotNull，失败记 FAIL。</summary>
internal sealed class Check(string id)
{
    public bool Ok = true;

    public void True(bool cond, string detail)
    {
        if (!cond) { Ok = false; PT.FailCase(id, detail); }
    }

    public void NotNull(object value, string what)
    {
        if (value is null) { Ok = false; PT.FailCase(id, what + "=null"); }
    }
}
