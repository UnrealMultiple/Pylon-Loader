using System.Globalization;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: DirectCallCoverage <proxy-directory> <report.md>");
    return 2;
}

var rows = new List<(string Name, int Direct, int Fallback)>();
foreach (string path in Directory.EnumerateFiles(Path.GetFullPath(args[0]), "*.dll").Order())
{
    using var assembly = AssemblyDefinition.ReadAssembly(path);
    int direct = 0, fallback = 0;
    foreach (var type in AllTypes(assembly.MainModule.Types))
    foreach (var method in type.Methods.Where(m => m.HasBody))
    {
        bool guarded = false, invoked = false;
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference reference) continue;
            guarded |= reference.DeclaringType.FullName == "Il2CppInterop.Runtime.GuardedInvoke" && reference.Name == "Invoke";
            invoked |= reference.DeclaringType.FullName == "Il2CppInterop.Runtime.IL2CPP" && reference.Name == "il2cpp_runtime_invoke";
        }
        if (guarded) direct++;
        else if (invoked) fallback++;
    }
    if (direct + fallback > 0) rows.Add((assembly.Name.Name, direct, fallback));
}

var report = new StringBuilder("# 当前代理直调入口覆盖统计\n\n");
report.AppendLine("统计现有代理 DLL 的 IL 调用点,不包含字段访问器、辅助方法或未调用游戏方法的生成成员。每个代理方法只计一次。\n");
report.AppendLine("**GuardedInvoke 入口不等于实际直调**:运行时仍会拒绝 icall/PInvoke/runtime、泛型声明类、ABI 不支持或寄存器/栈槽超限的方法。实际可直调数量以设备元数据规划结果为准。\n");
report.AppendLine("| 程序集 | GuardedInvoke 入口 | 固定 runtime_invoke | 入口占比 |\n|---|---:|---:|---:|");
foreach (var row in rows) AddRow(row.Name, row.Direct, row.Fallback);
AddRow("全部代理", rows.Sum(r => r.Direct), rows.Sum(r => r.Fallback));
var game = rows.Where(r => r.Name == "Assembly-CSharp" || r.Name.StartsWith("Unity", StringComparison.Ordinal)).ToList();
AddRow("游戏及 Unity 代理", game.Sum(r => r.Direct), game.Sum(r => r.Fallback));
File.WriteAllText(Path.GetFullPath(args[1]), report.ToString());
Console.Write(report.ToString());
return 0;

void AddRow(string name, int direct, int fallback) => report.AppendLine($"| {name} | {direct} | {fallback} | {(100.0 * direct / (direct + fallback)).ToString("F2", CultureInfo.InvariantCulture)}% |");
static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
    }
}
