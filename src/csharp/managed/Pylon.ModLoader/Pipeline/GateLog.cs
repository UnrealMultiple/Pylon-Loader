using System.Text;
using Pylon.ModLoader.Core;

namespace Pylon.ModLoader.Pipeline;

/// <summary>[gate] 日志数据源：按管线聚合门就绪数，管线与门的增改无需修改本类。</summary>
internal static class GateLog
{
    private static readonly (string Prefix, Type[] Gates)[] Pipelines = Scan();

    internal static string Snapshot()
    {
        var sb = new StringBuilder(256).Append("[gate]");
        foreach (var (prefix, gates) in Pipelines)
        {
            var done = 0;
            foreach (var gate in gates)
            {
                if (HookGuard.IsDone(gate)) done++;
            }

            sb.Append(' ').Append(prefix).Append('=').Append(done).Append('/').Append(gates.Length);
        }

        return sb.ToString();
    }

    private static (string, Type[])[] Scan() =>
    [
        .. from pipeline in typeof(HookGuard).Assembly.GetTypes()
           where pipeline.DeclaringType is null
           let gates = GateScan.GatesOf(pipeline)
           where gates.Length > 0
           orderby pipeline.Name
           select (Prefix(pipeline), gates)
    ];

    private static string Prefix(Type pipeline)
    {
        var name = pipeline.Name.EndsWith("Pipeline", StringComparison.Ordinal)
            ? pipeline.Name[..^"Pipeline".Length]
            : pipeline.Name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
