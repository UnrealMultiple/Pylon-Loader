using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Il2CppInterop.Runtime;

/// <summary>返回物形状，逐个对上内核 il2cpp::RetKind（CallFrame.h）。</summary>
public enum PylonRetKind
{
    Int = 0,
    F32 = 1,
    F64 = 2,
    S16 = 3,
    H1F = 4,
    H2F = 5,
    H3F = 6,
    H4F = 7,
    H1D = 8,
    H2D = 9,
    H3D = 10,
    H4D = 11,
    Indirect = 12,
}

/// <summary>
/// 直调帧：与内核 pylon::il2cpp::CallFrame 逐字段同布局（x16 + d8 + 间接返回 + 返回物 + MethodInfo）。
/// 生成器把参数按 AAPCS64 写进 X/D，内核只装寄存器、跳 methodPointer、把返回物写回 Out。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct PylonCallFrame
{
    public fixed long X[16];      // x0-x7 + 8 个栈槽
    public fixed ulong D[8];      // d0-d7 原始位
    public IntPtr Indirect;       // >16 字节返回：调用方缓冲
    public int Ret;               // PylonRetKind
    public int RetSize;           // 返回物字节数 / Indirect 大小
    public fixed ulong Out[4];    // x0/x1 或 s0-s3/d0-d3
    public IntPtr Method;         // MethodInfo*
    public IntPtr Exc;            // 捕获到的 Il2CppException*
}

// Opaque ABI-v1 certificate. Only the runtime can mint one from validated metadata.
// Stores layout, NEVER a methodPointer: hooks/replacements are observed on every call.
public sealed class FrameMethodPlan
{
    internal readonly IntPtr Method;
    internal readonly int Shape, Ret, RetSize, MethodDst;
    internal readonly bool Indirect;
    internal FrameMethodPlan(IntPtr method, int shape, int ret, int size, int dst, bool indirect)
        => (Method, Shape, Ret, RetSize, MethodDst, Indirect) = (method, shape, ret, size, dst, indirect);
}

/// <summary>
/// Pylon 直调入口。生成的代理调用 Invoke(method, self, args, argc, &amp;ret, &amp;exc)：
/// args[i] 对值类型是"指向值的指针"、对引用类型是对象指针本身、对 byref 是地址（与旧
/// runtime_invoke 代理的 args 数组一致）。这里的规划按 MethodInfo 的元数据做一次、按方法缓存，
/// The packed frame is executed by PylonMod_CallFrame.
/// </summary>
public static unsafe partial class GuardedInvoke
{
    [LibraryImport("pylon", EntryPoint = "PylonMod_CallFrame")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int NativeCall(IntPtr fn, PylonCallFrame* frame);

    [LibraryImport("pylon", EntryPoint = "PylonMod_CallFrameError")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr NativeError();

    private const int MaxIndirect = 512;

    private sealed class MethodPlan
    {
        public ArgOp[] Args = Array.Empty<ArgOp>();
        public bool Indirect;
        public int SelfDst = -1, MethodDst;
        public int RetKind;
        public int RetSize;
        public ArgumentPacker? Packer;
    }

    /// <summary>参数进帧的方式：0 = 指针值(args[i])，1 = 读 Size 字节，2 = F32，3 = F64，4 = HFA 位块进 D，5 = 结构体进 X。</summary>
    private struct ArgOp
    {
        public byte Kind;
        public byte Dst;
        public byte Count;
        public int Size;
    }

    private static readonly object Gate = new();
    private sealed class CachedMethod
    {
        public readonly IntPtr Method;
        public readonly int ArgCount;
        public readonly MethodPlan? Plan;
        public readonly int FrameShape;
        public CachedMethod(IntPtr method)
        {
            Method = method;
            ArgCount = checked((int)IL2CPP.il2cpp_method_get_param_count(method));
            Plan = Build(method);
            FrameShape = GetFrameShape(Plan);
            if (Plan != null) Plan.Packer = CreatePacker(Plan);
        }
    }

    private static readonly Dictionary<IntPtr, CachedMethod> Plans = new();
    [ThreadStatic] private static CachedMethod?[]? t_methods;

    [ThreadStatic] private static IntPtr[]? t_buffers;
    [ThreadStatic] private static int[]? t_capacities;
    [ThreadStatic] private static int t_depth;
    [ThreadStatic] private static int t_nativeCallbackDepth;
    [ThreadStatic] private static bool t_nativeCallbackWorker;

    /// <summary>Host supplied game-thread probe. Null or a failing probe is conservative.</summary>
    public static Func<bool>? IsGameThreadProvider { get; set; }

    /// <summary>Marks entry from a native hook callback on the current thread.</summary>
    public static void EnterNativeCallback()
    {
        if (t_nativeCallbackDepth++ != 0) return;
        try { t_nativeCallbackWorker = IsGameThreadProvider is { } probe && !probe(); }
        catch { t_nativeCallbackWorker = false; }
    }

    /// <summary>Leaves a native hook callback, restoring the outer callback state.</summary>
    public static void ExitNativeCallback()
    {
        if (t_nativeCallbackDepth <= 0) throw new InvalidOperationException("No native callback to leave");
        if (--t_nativeCallbackDepth == 0) t_nativeCallbackWorker = false;
    }

    private static bool IsWorkerNativeCallback => t_nativeCallbackDepth != 0 && t_nativeCallbackWorker;

    public static void Invoke(IntPtr method, IntPtr self, IntPtr args, int argc, ref IntPtr ret, ref IntPtr exc)
    {
        ret = IntPtr.Zero;
        exc = IntPtr.Zero;
        if (argc < 0 || (argc > 0 && args == IntPtr.Zero))
            throw new ArgumentException("Invalid direct-call arguments", nameof(argc));
        if (method == IntPtr.Zero)
            throw new InvalidOperationException("直调：MethodInfo 为空");

        if (IsWorkerNativeCallback)
        {
            int expected = checked((int)IL2CPP.il2cpp_method_get_param_count(method));
            if (argc != expected)
                throw new ArgumentException("Direct-call argument count mismatch", nameof(argc));
            FallbackRuntimeInvoke(method, self, args, ref ret, ref exc);
            return;
        }

        CachedMethod cached = GetMethod(method);
        if (argc != cached.ArgCount)
            throw new ArgumentException("Direct-call argument count mismatch", nameof(argc));
        t_depth++;
        try
        {
            MethodPlan? plan = cached.Plan;
            if (plan == null)
            {
                FallbackRuntimeInvoke(method, self, args, ref ret, ref exc);
                return;
            }

            PylonCallFrame frame = default;
            if (plan.Packer is { } packer) packer(&frame, method, self, args);
            else PackArguments(ref frame, plan, method, self, args);

            frame.Ret = plan.RetKind;
            frame.Method = method;
            frame.Exc = IntPtr.Zero;

            IntPtr buf = plan.RetSize == 0 ? IntPtr.Zero : AcquireBuffer(plan.RetSize, clear: false);
            frame.RetSize = plan.RetSize;
            if (plan.Indirect) frame.Indirect = buf;

            IntPtr fn = *(IntPtr*)method;
            if (fn == IntPtr.Zero)
                throw new InvalidOperationException("直调：methodPointer 为空");

            int rc = NativeCall(fn, &frame);
            if (rc == 0)
            {
                if (frame.Exc != IntPtr.Zero)
                {
                    exc = frame.Exc;
                    ret = buf;
                    return;
                }
                string text = Marshal.PtrToStringUTF8(NativeError()) ?? "未知错误";
                throw new InvalidOperationException("直调失败：" + text);
            }

            if (plan.Indirect)
            {
                ret = frame.Indirect;
                return;
            }

            int bytes = plan.RetSize;
            if (bytes == 0) return;
            Buffer.MemoryCopy(frame.Out, (void*)buf, bytes, bytes);
            ret = buf;
        }
        finally
        {
            t_depth--;
        }
    }

    // ABI v1 pilot shapes. A mismatch returns before touching the native executor.
    // Callers must default-initialize the ENTIRE frame; unoccupied registers are read too.
    public static bool FrameDiagnosticsAvailable
    {
        get
        {
#if PYLON_FRAME_DIAGNOSTICS
            return true;
#else
            return false;
#endif
        }
    }

    // Retain API compatibility, but never silently pretend a production build switched routes.
    public static bool FramePilotEnabled
    {
#if PYLON_FRAME_DIAGNOSTICS
        get; set;
#else
        get => true;
        set { if (!value) throw new NotSupportedException("Frame route switching requires PylonFrameDiagnostics=true"); }
#endif
    }
#if PYLON_FRAME_DIAGNOSTICS
        = true;
#endif

    public static bool TryInvokeFrame(IntPtr method, int shape, ref PylonCallFrame frame)
    {
#if PYLON_FRAME_DIAGNOSTICS
        if (!FramePilotEnabled) return false;
#endif
        if (IsWorkerNativeCallback) return false;
        if (method == IntPtr.Zero || shape is < 1 or > 4) return false;
        CachedMethod cached = GetMethod(method);
        if (cached.FrameShape != shape) return false;
        MethodPlan plan = cached.Plan!;
        return ExecuteFrame(method, plan.RetKind, plan.RetSize, plan.MethodDst, plan.Indirect, ref frame);
    }

    // Diagnostic switch compares the same generated body with/without the TLS lookup.
    public static bool FramePlanCacheEnabled
    {
#if PYLON_FRAME_DIAGNOSTICS
        get; set;
#else
        get => true;
        set { if (!value) throw new NotSupportedException("Frame route switching requires PylonFrameDiagnostics=true"); }
#endif
    }
#if PYLON_FRAME_DIAGNOSTICS
        = true;
#endif

    public static bool TryInvokeFrameCached(IntPtr method, int shape, ref FrameMethodPlan? certificate, ref PylonCallFrame frame)
    {
#if PYLON_FRAME_DIAGNOSTICS
        if (!FramePilotEnabled) return false;
#endif
        if (IsWorkerNativeCallback) return false;
        if (method == IntPtr.Zero || shape is < 1 or > 4) return false;
#if PYLON_FRAME_DIAGNOSTICS
        if (!FramePlanCacheEnabled)
        {
            // Same call depth as certificate mode: do not charge TLS for an extra entry wrapper.
            var cached = GetMethod(method);
            if (cached.FrameShape != shape) return false;
            var layout = cached.Plan!;
            return ExecuteFrame(method, layout.RetKind, layout.RetSize, layout.MethodDst, layout.Indirect, ref frame);
        }
#endif
        var plan = System.Threading.Volatile.Read(ref certificate);
        if (plan == null || plan.Method != method || plan.Shape != shape)
        {
            var cached = GetMethod(method);
            if (cached.FrameShape != shape) return false;
            var layout = cached.Plan!;
            plan = new FrameMethodPlan(method, shape, layout.RetKind, layout.RetSize, layout.MethodDst, layout.Indirect);
            System.Threading.Volatile.Write(ref certificate, plan);
        }
        return ExecuteFrame(method, plan.Ret, plan.RetSize, plan.MethodDst, plan.Indirect, ref frame);
    }

    private static bool ExecuteFrame(IntPtr method, int ret, int size, int methodDst, bool indirect, ref PylonCallFrame frame)
    {
        if (indirect && frame.Indirect == IntPtr.Zero)
            throw new ArgumentException("Generated frame requires an indirect result buffer", nameof(frame));
        frame.Ret = ret;
        frame.RetSize = size;
        frame.Method = method;
        frame.Exc = IntPtr.Zero;
        fixed (PylonCallFrame* p = &frame)
        {
            p->X[methodDst] = (long)method;
            IntPtr fn = *(IntPtr*)method;
            if (fn == IntPtr.Zero) throw new InvalidOperationException("直调:methodPointer 为空");
            if (NativeCall(fn, p) == 0 && p->Exc == IntPtr.Zero)
                throw new InvalidOperationException("直调失败:" + (Marshal.PtrToStringUTF8(NativeError()) ?? "未知错误"));
        }
        return true;
    }

    private static int GetFrameShape(MethodPlan? plan)
    {
        if (plan == null || plan.SelfDst != -1) return 0;
        var args = plan.Args;
        if (!plan.Indirect && plan.RetKind == (int)PylonRetKind.Int && plan.RetSize == 4 && plan.MethodDst == 0 &&
            args.Length == 1 && args[0].Kind == 3 && args[0].Dst == 0) return 4;
        if (plan.RetKind == (int)PylonRetKind.F32 && plan.RetSize == 4 && plan.MethodDst == 0)
        {
            if (args.Length == 3 && args[0].Kind == 2 && args[0].Dst == 0 &&
                args[1].Kind == 2 && args[1].Dst == 1 && args[2].Kind == 2 && args[2].Dst == 2) return 1;
            if (args.Length == 2 && args[0].Kind == 4 && args[0].Dst == 0 && args[0].Size == 8 && args[0].Count == 2 &&
                args[1].Kind == 4 && args[1].Dst == 2 && args[1].Size == 8 && args[1].Count == 2) return 2;
        }
        if (plan.Indirect && plan.RetKind == (int)PylonRetKind.Indirect && plan.RetSize == 64 && plan.MethodDst == 2 &&
            args.Length == 2 && args[0].Kind == 0 && args[0].Dst == 0 && args[0].Count == 1 && args[0].Size == 64 &&
            args[1].Kind == 0 && args[1].Dst == 1 && args[1].Count == 1 && args[1].Size == 64) return 3;
        return 0;
    }

    private unsafe delegate void ArgumentPacker(PylonCallFrame* frame, IntPtr method, IntPtr self, IntPtr args);

    // Emit only when a method is first called, never during metadata coverage scans.
    private static ArgumentPacker? CreatePacker(MethodPlan plan)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported) return null;
        try
        {
            var method = new DynamicMethod("PylonPack", typeof(void),
                new[] { typeof(PylonCallFrame*), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr) },
                typeof(GuardedInvoke).Module, true);
            var il = method.GetILGenerator();
            void Destination(int offset)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, offset);
                il.Emit(OpCodes.Add);
            }
            void Source(int index, int offset = 0)
            {
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Ldc_I4, index * 8);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldind_I);
                if (offset != 0)
                {
                    il.Emit(OpCodes.Ldc_I4, offset);
                    il.Emit(OpCodes.Add);
                }
            }
            if (plan.SelfDst >= 0)
            {
                Destination(plan.SelfDst * 8);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Stind_I);
            }
            Destination(plan.MethodDst * 8);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stind_I);
            for (int i = 0; i < plan.Args.Length; i++)
            {
                ArgOp op = plan.Args[i];
                int dst = (op.Kind is 2 or 3 or 4 ? 128 : 0) + op.Dst * 8;
                if (op.Kind == 4)
                {
                    int leaf = op.Size / op.Count;
                    for (int k = 0; k < op.Count; k++)
                    {
                        Destination(dst + k * 8);
                        Source(i, k * leaf);
                        il.Emit(leaf == 4 ? OpCodes.Ldind_U4 : OpCodes.Ldind_I8);
                        il.Emit(leaf == 4 ? OpCodes.Stind_I4 : OpCodes.Stind_I8);
                    }
                    continue;
                }
                Destination(dst);
                Source(i);
                switch (op.Kind)
                {
                    case 0: il.Emit(OpCodes.Stind_I); break;
                    case 1:
                        il.Emit(op.Size switch { 1 => OpCodes.Ldind_I1, 2 => OpCodes.Ldind_I2,
                            <= 4 => OpCodes.Ldind_I4, _ => OpCodes.Ldind_I8 });
                        il.Emit(OpCodes.Conv_I8);
                        il.Emit(OpCodes.Stind_I8);
                        break;
                    case 2:
                        il.Emit(OpCodes.Ldind_U4);
                        il.Emit(OpCodes.Stind_I4);
                        break;
                    case 3:
                        il.Emit(OpCodes.Ldind_I8);
                        il.Emit(OpCodes.Stind_I8);
                        break;
                    case 5:
                        il.Emit(OpCodes.Ldc_I4, op.Size);
                        il.Emit(OpCodes.Cpblk);
                        break;
                    default: return null;
                }
            }
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<ArgumentPacker>();
        }
        catch (Exception)
        {
            return null; // The interpreted plan remains valid if runtime code generation is unavailable.
        }
    }
    private static void PackArguments(ref PylonCallFrame frame, MethodPlan plan, IntPtr method, IntPtr self, IntPtr args)
    {
        fixed (long* x = frame.X)
        fixed (ulong* d = frame.D)
        {
            if (plan.SelfDst >= 0) x[plan.SelfDst] = (long)self;
            x[plan.MethodDst] = (long)method;

            for (int i = 0; i < plan.Args.Length; i++)
            {
                var op = plan.Args[i];
                IntPtr cell = ((IntPtr*)args)[i];
                switch (op.Kind)
                {
                    case 0: // 指针/引用/字符串/数组/byref/>16 字节结构体：args[i] 本身就是值
                        x[op.Dst] = (long)cell;
                        break;
                    case 1: // 值类型字
                        x[op.Dst] = ReadWord(cell, op.Size);
                        break;
                    case 2: // float
                        d[op.Dst] = BitConverter.SingleToUInt32Bits(*(float*)cell);
                        break;
                    case 3: // double
                        d[op.Dst] = BitConverter.DoubleToUInt64Bits(*(double*)cell);
                        break;
                    case 4: // One HFA element per SIMD register.
                        int leafSize = op.Size / op.Count;
                        for (int k = 0; k < op.Count; k++)
                            d[op.Dst + k] = leafSize == 4 ? ((uint*)cell)[k] : ((ulong*)cell)[k];
                        break;
                    case 5: // <=16 字节非 HFA 结构体：位块进 X
                        Buffer.MemoryCopy((void*)cell, x + op.Dst, op.Size, op.Size);
                        break;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ReadWord(IntPtr cell, int size)
    {
        return size switch
        {
            1 => *(sbyte*)cell,
            2 => *(short*)cell,
            <= 4 => *(int*)cell,
            _ => *(long*)cell,
        };
    }

    private static MethodPlan? Resolve(IntPtr method) => GetMethod(method).Plan;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CachedMethod GetMethod(IntPtr method)
    {
        var local = t_methods ??= new CachedMethod?[64];
        ulong key = (ulong)method >> 3;
        int index = (int)((key ^ (key >> 7) ^ (key >> 17)) & 63);
        var cached = local[index];
        if (cached != null && cached.Method == method) return cached;
        return ResolveSlow(method, local, index);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CachedMethod ResolveSlow(IntPtr method, CachedMethod?[] local, int index)
    {
        CachedMethod? cached;
        lock (Gate)
        {
            if (Plans.TryGetValue(method, out cached))
            {
                local[index] = cached;
                return cached;
            }
        }
        var created = new CachedMethod(method);
        lock (Gate)
        {
            if (!Plans.TryGetValue(method, out cached)) Plans[method] = cached = created;
        }
        local[index] = cached;
        return cached;
    }
    private static MethodPlan? Build(IntPtr method)
    {
        try
        {
            // icall / PInvoke / runtime 实现不承诺托管 methodPointer ABI。
            // 尤其不能把 icall 包装内部的 _Injected 目标当作原方法直调。
            uint implementation = 0;
            uint flags = IL2CPP.il2cpp_method_get_flags(method, ref implementation);
            if ((flags & 0x2000) != 0 || (implementation & (0x1000u | 0x3u)) != 0)
                return null;
            if (IntPtr.Size != 8 || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
                return null;
            var plan = new MethodPlan();
            var ops = new List<ArgOp>();

            int ngrn = 0, nsrn = 0, nsaa = 0;

            bool TakeInt(int count, out int dst)
            {
                dst = -1;
                if (ngrn + count <= 8)
                {
                    dst = ngrn;
                    ngrn += count;
                    return true;
                }
                ngrn = 8;
                if (nsaa + count > 8) return false;
                dst = 8 + nsaa;
                nsaa += count;
                return true;
            }

            // 实例方法的 this：必须是类实例（值类型 this 走回退）
            IntPtr declaringClass = IL2CPP.il2cpp_method_get_class(method);
            if (declaringClass == IntPtr.Zero || IL2CPP.il2cpp_class_is_generic(declaringClass) ||
                IL2CPP.il2cpp_class_is_inflated(declaringClass)) return null;
            bool isInstance = IL2CPP.il2cpp_method_is_instance(method);
            if (isInstance)
            {
                if (IL2CPP.il2cpp_class_is_valuetype(declaringClass)) return null;
                if (!TakeInt(1, out int selfDst)) return null;
                plan.SelfDst = selfDst;
            }

            uint count = IL2CPP.il2cpp_method_get_param_count(method);

            for (uint i = 0; i < count; i++)
            {
                IntPtr type = IL2CPP.il2cpp_method_get_param(method, i);
                var op = Classify(type);
                if (op == null) return null;
                var a = op.Value;
                bool ok;
                if (a.Kind == 2 || a.Kind == 3) // F32/F64
                {
                    ok = nsrn + 1 <= 8;
                    if (ok) a.Dst = (byte)nsrn++;
                }
                else if (a.Kind == 4) // HFA 位块
                {
                    ok = nsrn + a.Count <= 8;
                    if (ok) { a.Dst = (byte)nsrn; nsrn += a.Count; }
                }
                else
                {
                    int words = a.Kind == 1 || a.Kind == 5 ? Math.Max(1, (a.Size + 7) / 8) : a.Count;
                    ok = TakeInt(words, out int dst);
                    if (ok) a.Dst = (byte)dst;
                }
                if (!ok) return null;
                ops.Add(a);
            }

            // 隐藏 MethodInfo 放在参数之后的第一个整数寄存器/栈槽（il2cpp 的调用约定）
            if (!TakeInt(1, out int methodDst)) return null;
            plan.MethodDst = methodDst;
            // Args 仅含显式参数;this/MethodInfo 分别写入,不能用寄存器编号过滤。

            plan.Args = ops.ToArray();

            IntPtr retType = IL2CPP.il2cpp_method_get_return_type(method);
            var ret = ClassifyReturn(retType);
            if (ret == null) return null;
            plan.RetKind = ret.Value.Kind;
            plan.RetSize = ret.Value.Size;
            plan.Indirect = ret.Value.Kind == (int)PylonRetKind.Indirect;
            return plan;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>参数分类：返回该参数进帧的方式；不支持则 null（回退 runtime_invoke）。</summary>
    private static ArgOp? Classify(IntPtr type)
    {
        if (type == IntPtr.Zero) return null;
        if (IL2CPP.il2cpp_type_is_byref(type)) return new ArgOp { Kind = 0, Count = 1 };

        int t = IL2CPP.il2cpp_type_get_type(type);
        switch (t)
        {
            case 0x02: return new ArgOp { Kind = 1, Size = 1 }; // Boolean
            case 0x03: return new ArgOp { Kind = 1, Size = 2 }; // Char
            case 0x04: return new ArgOp { Kind = 1, Size = 1 }; // I1
            case 0x05: return new ArgOp { Kind = 1, Size = 1 }; // U1
            case 0x06: return new ArgOp { Kind = 1, Size = 2 }; // I2
            case 0x07: return new ArgOp { Kind = 1, Size = 2 }; // U2
            case 0x08: return new ArgOp { Kind = 1, Size = 4 }; // I4
            case 0x09: return new ArgOp { Kind = 1, Size = 4 }; // U4
            case 0x0A: return new ArgOp { Kind = 1, Size = 8 }; // I8
            case 0x0B: return new ArgOp { Kind = 1, Size = 8 }; // U8
            case 0x0C: return new ArgOp { Kind = 2, Count = 1 }; // R4
            case 0x0D: return new ArgOp { Kind = 3, Count = 1 }; // R8
            case 0x0E: // String
            case 0x0F: // Ptr
            case 0x12: // Class
            case 0x14: // Array
            case 0x1C: // Object
            case 0x1D: // SZArray
                return new ArgOp { Kind = 0, Count = 1 };
            case 0x18: // I (IntPtr)
            case 0x19: // U (UIntPtr)
                return new ArgOp { Kind = 1, Size = 8 };
            case 0x11: // ValueType
            case 0x15: // GenericInstance
                return ClassifyValue(type);
            default:
                return null;
        }
    }

    private static ArgOp? ClassifyValue(IntPtr type)
    {
        IntPtr klass = IL2CPP.il2cpp_class_from_type(type);
        if (klass == IntPtr.Zero) return null;
        if (!IL2CPP.il2cpp_class_is_valuetype(klass))
            return new ArgOp { Kind = 0, Count = 1 };

        uint align = 0;
        int size = IL2CPP.il2cpp_class_value_size(klass, ref align);
        if (size <= 0 || size > MaxIndirect) return null;

        if (IL2CPP.il2cpp_class_is_enum(klass))
            return new ArgOp { Kind = 1, Size = size };

        if (TryHfa(klass, out bool isFloat, out int leaves) && size == leaves * (isFloat ? 4 : 8))
            return new ArgOp { Kind = 4, Count = (byte)leaves, Size = size };

        if (align > 8) return null;
        if (size > 16) return new ArgOp { Kind = 0, Count = 1, Size = size }; // 按引用拷（args[i] 是地址）
        return new ArgOp { Kind = 5, Size = size };
    }

    private static (int Kind, int Size)? ClassifyReturn(IntPtr type)
    {
        if (type == IntPtr.Zero) return null;
        if (IL2CPP.il2cpp_type_is_byref(type)) return null;
        int t = IL2CPP.il2cpp_type_get_type(type);
        switch (t)
        {
            case 0x01: return ((int)PylonRetKind.Int, 0);   // void
            case 0x02: return ((int)PylonRetKind.Int, 1);
            case 0x03: return ((int)PylonRetKind.Int, 2);
            case 0x04: return ((int)PylonRetKind.Int, 1);
            case 0x05: return ((int)PylonRetKind.Int, 1);
            case 0x06: return ((int)PylonRetKind.Int, 2);
            case 0x07: return ((int)PylonRetKind.Int, 2);
            case 0x08: return ((int)PylonRetKind.Int, 4);
            case 0x09: return ((int)PylonRetKind.Int, 4);
            case 0x0A: return ((int)PylonRetKind.Int, 8);
            case 0x0B: return ((int)PylonRetKind.Int, 8);
            case 0x0C: return ((int)PylonRetKind.F32, 4);
            case 0x0D: return ((int)PylonRetKind.F64, 8);
            case 0x0E:
            case 0x0F:
            case 0x12:
            case 0x14:
            case 0x1C:
            case 0x1D:
            case 0x18:
            case 0x19:
                return ((int)PylonRetKind.Int, 8);
            case 0x11:
            case 0x15:
                break;
            default:
                return null;
        }

        IntPtr klass = IL2CPP.il2cpp_class_from_type(type);
        if (klass == IntPtr.Zero) return null;
        if (!IL2CPP.il2cpp_class_is_valuetype(klass)) return ((int)PylonRetKind.Int, 8);

        uint align = 0;
        int size = IL2CPP.il2cpp_class_value_size(klass, ref align);
        if (size <= 0 || size > MaxIndirect) return null;
        if (IL2CPP.il2cpp_class_is_enum(klass)) return ((int)PylonRetKind.Int, size);

        if (TryHfa(klass, out bool isFloat, out int leaves) && size == leaves * (isFloat ? 4 : 8))
            return isFloat
                ? ((int)(PylonRetKind.H1F + leaves - 1), size)
                : ((int)(PylonRetKind.H1D + leaves - 1), size);

        if (align > 8) return null;
        if (size > 16) return ((int)PylonRetKind.Indirect, size);
        return ((int)PylonRetKind.S16, size);
    }

    /// <summary>值类型是不是 HFA（1..4 个全 float 或全 double 叶子），最多递归 8 层。</summary>
    private static bool TryHfa(IntPtr klass, out bool isFloat, out int leaves)
    {
        isFloat = false;
        leaves = 0;
        char kind = '\0';
        if (!WalkFields(klass, ref kind, ref leaves, 0)) return false;
        if (leaves <= 0 || leaves > 4) return false;
        isFloat = kind == 'f';
        return true;
    }

    private static bool WalkFields(IntPtr klass, ref char kind, ref int leaves, int depth)
    {
        if (depth > 8) return false;
        IntPtr iter = IntPtr.Zero;
        while (true)
        {
            IntPtr field = IL2CPP.il2cpp_class_get_fields(klass, ref iter);
            if (field == IntPtr.Zero) break;
            if ((IL2CPP.il2cpp_field_get_flags(field) & 0x10) != 0) continue; // static

            IntPtr ftype = IL2CPP.il2cpp_field_get_type(field);
            int t = IL2CPP.il2cpp_type_get_type(ftype);
            if (t == 0x0C) // R4
            {
                if (kind == 'd') return false;
                kind = 'f';
                if (++leaves > 4) return false;
            }
            else if (t == 0x0D) // R8
            {
                if (kind == 'f') return false;
                kind = 'd';
                if (++leaves > 4) return false;
            }
            else if (t == 0x11) // 嵌套值类型
            {
                IntPtr nested = IL2CPP.il2cpp_class_from_type(ftype);
                if (nested == IntPtr.Zero || IL2CPP.il2cpp_class_is_enum(nested)) return false;
                if (!WalkFields(nested, ref kind, ref leaves, depth + 1)) return false;
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    private static void FallbackRuntimeInvoke(IntPtr method, IntPtr self, IntPtr args, ref IntPtr ret, ref IntPtr exc)
    {
        IntPtr exception = IntPtr.Zero;
        IntPtr returned = IL2CPP.il2cpp_runtime_invoke(method, self, (void**)args, ref exception);
        exc = exception;
        if (exception != IntPtr.Zero) return;

        IntPtr retType = IL2CPP.il2cpp_method_get_return_type(method);
        int t = IL2CPP.il2cpp_type_get_type(retType);
        bool valueReturn = t is >= 0x02 and <= 0x0D or 0x18 or 0x19 ||
            (t is 0x11 or 0x15 && IL2CPP.il2cpp_class_is_valuetype(IL2CPP.il2cpp_class_from_type(retType)));
        if (valueReturn)
        {
            IntPtr klass = IL2CPP.il2cpp_class_from_type(retType);
            uint align = 0;
            int size = klass != IntPtr.Zero ? IL2CPP.il2cpp_class_value_size(klass, ref align) : 0;
            if (size <= 0) throw new InvalidOperationException("Invalid fallback value size");
            IntPtr buf = AcquireBuffer(Math.Max(size, 8));
            if (returned != IntPtr.Zero && size > 0)
            {
                void* data = (void*)IL2CPP.il2cpp_object_unbox(returned);
                Buffer.MemoryCopy(data, (void*)buf, size, size);
            }
            ret = buf;
        }
        else
        {
            IntPtr buf = AcquireBuffer(8);
            *(IntPtr*)buf = returned;
            ret = buf;
        }
    }

    private static IntPtr AcquireBuffer(int size, bool clear = true)
    {
        if (size <= 0) size = 8;
        var buffers = t_buffers ??= Array.Empty<IntPtr>();
        var capacities = t_capacities ??= Array.Empty<int>();
        int d = t_depth;
        if (d >= buffers.Length)
        {
            var grown = new IntPtr[Math.Max(d + 1, 8)];
            Array.Copy(buffers, grown, buffers.Length);
            t_buffers = buffers = grown;
            Array.Resize(ref capacities, grown.Length);
            t_capacities = capacities;
        }
        int capacity = Math.Max(size, 32);
        if (capacities[d] < capacity)
        {
            buffers[d] = Marshal.ReAllocHGlobal(buffers[d], (IntPtr)capacity);
            capacities[d] = capacity;
        }
        if (clear) new Span<byte>((void*)buffers[d], size).Clear();
        return buffers[d];
    }
}
