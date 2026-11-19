using System.Reflection;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Pipeline;

namespace Pylon.ModLoader.Core;

/// <summary>门契约：Wanted 是注册期每轮重评的安装条件，Install 是一次性安装体；实现 ILazyGate 的门不参与就绪判定，由运行期触点显式安装。</summary>
internal interface IGate
{
    static abstract bool Wanted();

    static abstract void Install();
}

/// <summary>惰性门标记：不参与就绪判定与每轮重评，由 GateHub.Trigger 在运行期首次触点安装。</summary>
internal interface ILazyGate : IGate;

/// <summary>门类型发现：收集类型及其嵌套容器内所有实现 IGate 的门。</summary>
internal static class GateScan
{
    internal static Type[] GatesOf(Type owner) => [.. Collect(owner)];

    private static IEnumerable<Type> Collect(Type owner)
    {
        foreach (var type in owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (typeof(IGate).IsAssignableFrom(type))
            {
                yield return type;
            }
            else
            {
                foreach (var nested in Collect(type)) yield return nested;
            }
        }
    }
}

/// <summary>一条门的运行期句柄：条件与安装体装箱成委托，惰性与否在构造期由 ILazyGate 判定。</summary>
internal sealed class GateEntry(Type type, Func<bool> wanted, Action install, bool lazy)
{
    internal readonly Type Type = type;
    internal readonly Func<bool> Wanted = wanted;
    internal readonly Action Install = install;
    internal readonly bool Lazy = lazy;
}

/// <summary>门句柄的静态桥：经泛型参数取得 static abstract 成员并装箱成委托，供反射枚举统一消费。</summary>
internal static class GateSlot<T> where T : struct, IGate
{
    internal static readonly GateEntry Entry = new(
        typeof(T),
        T.Wanted,
        T.Install,
        typeof(ILazyGate).IsAssignableFrom(typeof(T))
    );
}

/// <summary>Hook 安装状态与注册期判定工具。</summary>
internal static class HookGuard
{
    private static readonly HashSet<Type> InstalledTypes = [];

    internal static void Mark(Type gate) => InstalledTypes.Add(gate);

    internal static bool IsDone(Type gate) => InstalledTypes.Contains(gate);

    internal static bool Wants<T>(params ReadOnlySpan<HookId> hooks) where T : ModType
    {
        foreach (var content in ModContent.GetContent<T>())
        {
            var plan = content.Plan;
            foreach (var hook in hooks)
            {
                if (plan.Has(hook)) return true;
            }
        }

        return false;
    }

    internal static bool Overrides(Type type, Type baseType, string method) =>
        type.GetMethod(
            method,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy
        )?.DeclaringType != baseType;
}

/// <summary>管线的门枢纽：反射发现嵌套门并装箱，每轮重评条件、装新门；非惰性门全部就绪才返回 true，未就绪保持 pending 等下轮重评。</summary>
internal static class GateHub<P> where P : class
{
    internal static readonly GateEntry[] Gates = [.. GateScan.GatesOf(typeof(P)).Select(EntryOf)];

    private static GateEntry EntryOf(Type gate) =>
        (GateEntry)typeof(GateSlot<>).MakeGenericType(gate)
            .GetField("Entry", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    internal static bool Run()
    {
        foreach (var gate in Gates)
        {
            if (gate.Lazy || HookGuard.IsDone(gate.Type) || !gate.Wanted()) continue;
            HookGuard.Mark(gate.Type);
            gate.Install();
        }

        foreach (var gate in Gates)
        {
            if (!gate.Lazy && !HookGuard.IsDone(gate.Type)) return false;
        }

        return true;
    }

    internal static void Trigger<T>() where T : struct, IGate
    {
        foreach (var gate in Gates)
        {
            if (gate.Type != typeof(T) || HookGuard.IsDone(gate.Type)) continue;
            HookGuard.Mark(gate.Type);
            gate.Install();
            return;
        }
    }
}
