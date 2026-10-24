using System.Runtime.InteropServices;
using AsmArm64;
using Il2CppInterop.Runtime;
using AsmInstruction = AsmArm64.Arm64Instruction;
using AsmInstructionId = AsmArm64.Arm64InstructionId;

namespace Pylon.ModLoader.Patching;
/// <summary>
/// Legacy 方法体解析与逐条指令补丁（尚未迁移静态 fingerprint）：klass 来自强类型 store，
/// 方法走 il2cpp 名字 API（解析不到返回 null，没有 GetIl2CppMethod 的假桩问题）；
/// 体指针读 MethodInfo 头部，边界用"同类第一个更靠后的方法"启发式。
/// </summary>
internal static class MethodCode
{
    private const int MaxMethodBytes = 512 * 1024; // 找不到后继方法时的天花板

    /// <summary>强类型类的 klass 指针（未就绪返回 Zero）。</summary>
    public static IntPtr KlassOf<T>() => Il2CppClassPointerStore<T>.NativeClassPtr;

    /// <summary>按运行时类型解析 klass（阶段表里存 System.Type；走 interop 自己的解析面）。</summary>
    public static IntPtr KlassOf(System.Type type) =>
        type is null ? IntPtr.Zero : Il2CppClassPointerStore.GetNativeClassPointer(type);

    /// <summary>解析方法体 [body, end)。解析不到返回 false。</summary>
    public static unsafe bool TryResolve(IntPtr klass, string name, int argc, out IntPtr body, out IntPtr end)
    {
        body = end = IntPtr.Zero;
        if (klass == IntPtr.Zero) return false;

        var method = IL2CPP.il2cpp_class_get_method_from_name(klass, name, argc);
        if (method == IntPtr.Zero) return false;

        var start = Marshal.ReadIntPtr(method); // MethodInfo 头部第一个指针宽字段 = methodPointer
        if (start == IntPtr.Zero) return false;

        var last = start + MaxMethodBytes;
        var iter = IntPtr.Zero;
        IntPtr other;
        while ((other = IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
        {
            var at = Marshal.ReadIntPtr(other);
            if (at > start && at < last) last = at;
        }

        body = start;
        end = last;
        return true;
    }

    internal readonly record struct MovzPlan(uint Expected, Arm64RegisterW Register,
        ushort Immediate, Arm64PatchInstruction Replacement);

    internal readonly record struct LoopPlan(uint Expected, uint ExpectedBranch, Arm64RegisterXOrSP Register,
        ushort Immediate, Arm64ConditionalKind Condition, Arm64PatchInstruction Replacement);

    /// <summary>纯离线规划：输入 word 必须由调用方提供；不读取地址或安装补丁。</summary>
    internal static bool TryPlanMovz(uint word, ushort from, ushort to, out MovzPlan plan)
    {
        plan = default;
        if (!IsMovz(word, from, out var register)) return false;
        // 重建原指令也须逐位一致：排除非零移位及其他 MOV 编码别名。
        using var original = new Arm64PatchBuilder(0);
        original.MOVZ(register, from);
        if (!TryOne(original, out var rebuilt) || rebuilt.Word != word) return false;
        using var replacement = new Arm64PatchBuilder(0);
        replacement.MOVZ(register, to);
        if (!TryOne(replacement, out var result) || !IsMovz(result.Word, to, out var targetRegister) ||
            !targetRegister.Equals(register)) return false;
        plan = new(word, register, to, result);
        return true;
    }

    /// <summary>纯离线规划：只接受零移位 CMP X/SP + 紧邻的指定 B.cond，保留原业务条件。</summary>
    internal static bool TryPlanLoop(uint word, uint next, int from, int to, bool exitOnEqual, out LoopPlan plan)
    {
        plan = default;
        if (from is < 0 or > 4095 || to is < 0 or > 4095 ||
            !IsCompare(word, from, out var register)) return false;
        var branch = AsmInstruction.Decode(next);
        if (branch.Id != AsmInstructionId.B_only_condbranch || branch.OperandCount != 2 ||
            branch.GetOperand(0).Kind != Arm64OperandKind.Enum ||
            branch.GetOperand(1).Kind != Arm64OperandKind.Label) return false;
        var conditionOperand = (Arm64EnumOperand)branch.GetOperand(0);
        if (conditionOperand.EnumKind != Arm64EnumKind.Conditional) return false;
        var condition = conditionOperand.AsConditional;
        if (!(exitOnEqual ? condition == Arm64ConditionalKind.EQ :
            condition is Arm64ConditionalKind.NE or Arm64ConditionalKind.LT or Arm64ConditionalKind.LO)) return false;
        using var original = new Arm64PatchBuilder(0);
        original.CMP(register, (ushort)from);
        if (!TryOne(original, out var rebuilt) || rebuilt.Word != word) return false;
        using var replacement = new Arm64PatchBuilder(0);
        replacement.CMP(register, (ushort)to);
        if (!TryOne(replacement, out var result) || !IsCompare(result.Word, to, out var targetRegister) ||
            !targetRegister.Equals(register)) return false;
        plan = new(word, next, register, (ushort)to, condition, result);
        return true;
    }

    private static bool IsMovz(uint word, ushort immediate, out Arm64RegisterW register)
    {
        register = default;
        var instruction = AsmInstruction.Decode(word);
        if (instruction.Id is not (AsmInstructionId.MOVZ_32_movewide or AsmInstructionId.MOV_movz_32_movewide) ||
            instruction.OperandCount < 2 || instruction.GetOperand(0).Kind != Arm64OperandKind.Register ||
            instruction.GetOperand(1).Kind != Arm64OperandKind.Immediate ||
            ((Arm64ImmediateOperand)instruction.GetOperand(1)).Value != immediate || !HasZeroShift(instruction)) return false;
        var decoded = ((Arm64RegisterOperand)instruction.GetOperand(0)).Value;
        if (decoded.Kind != Arm64RegisterKind.W) return false;
        register = (Arm64RegisterW)decoded;
        return true;
    }

    private static bool IsCompare(uint word, int immediate, out Arm64RegisterXOrSP register)
    {
        register = default;
        var instruction = AsmInstruction.Decode(word);
        if (instruction.Id != AsmInstructionId.CMP_subs_64s_addsub_imm || instruction.OperandCount < 2 ||
            instruction.GetOperand(0).Kind != Arm64OperandKind.Register ||
            instruction.GetOperand(1).Kind != Arm64OperandKind.Immediate ||
            ((Arm64ImmediateOperand)instruction.GetOperand(1)).Value != immediate || !HasZeroShift(instruction)) return false;
        var decoded = ((Arm64RegisterOperand)instruction.GetOperand(0)).Value;
        if (decoded.Kind is not (Arm64RegisterKind.X or Arm64RegisterKind.SP)) return false;
        register = (Arm64RegisterXOrSP)decoded;
        return true;
    }

    private static bool HasZeroShift(AsmInstruction instruction) => instruction.OperandCount == 2 ||
        (instruction.OperandCount == 3 && instruction.GetOperand(2).Kind == Arm64OperandKind.Shift &&
         ((Arm64ShiftOperand)instruction.GetOperand(2)).Amount == 0);

    private static bool TryOne(Arm64PatchBuilder builder, out Arm64PatchInstruction instruction)
    {
        instruction = default;
        if (!builder.TryBuild(out var instructions, out var diagnostics) || diagnostics.Count != 0 || instructions.Count != 1)
            return false;
        instruction = instructions[0];
        return true;
    }

    /// <summary>Legacy live scan：实时 word 同时充当 expected；计数不是静态 fingerprint 验证。</summary>
    public static unsafe int PatchMovzImm16(IntPtr body, IntPtr end, ushort from, ushort to)
    {
        var patched = 0;
        for (var p = (uint*)body; (IntPtr)p < end; p++)
        {
            var word = *p;
            if (!TryPlanMovz(word, from, to, out var plan)) continue;
            if (CodeWrite.TryWrite((IntPtr)p, plan.Expected, plan.Replacement.Word, out _)) patched++;
        }
        return patched;
    }

    /// <summary>Legacy live scan：沿用启发式边界、实时 expected 和逐条安装；尚未迁移静态 fingerprint。</summary>
    public static unsafe int PatchLoopEnd(IntPtr body, IntPtr end, int from, int to, bool exitOnEqual)
    {
        var patched = 0;
        for (var p = (uint*)body; (IntPtr)(p + 1) < end; p++)
        {
            var word = *p;
            var next = p[1];
            if (!TryPlanLoop(word, next, from, to, exitOnEqual, out var plan)) continue;
            if (CodeWrite.TryWrite((IntPtr)p, plan.Expected, plan.Replacement.Word, out _)) patched++;
        }
        return patched;
    }
}
