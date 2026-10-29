#nullable disable
// 层：Hook 引擎 —— 两个桩的 IL 生成。
//
//  ① 入口桩（原生 ABI → 托管）：Dobby 把游戏的原生入口跳到它。
//  ② orig 桩（托管 → 原生 ABI）：orig 委托本身，用 calli 跳到链路的下一跳。
//
// 签名映射（与 HookPlan.NativeSlot 一致）：
//   引用/字符串 → IntPtr；bool → byte；char → ushort；枚举 → 底层整型；
//   真值类型（Rectangle/Vector2…）按值直传；ref/out → byref 或 IntPtr；
//   装箱值类型（Il2CppSystem.Nullable&lt;T&gt; 这类 class 代理但原生按值传）→ 镜像结构 + il2cpp_value_box/unbox。
//
// 隐藏尾参 MethodInfo* 两边都要带；ECMA-335 规定 calli 先压参数、最后压函数指针。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace Il2CppInterop.RuntimeDetour
{
    /// <summary>入口桩与 orig 桩的构造。</summary>
    internal static class HookStub
    {
        private const string EntryPrefix = "(pylon -> managed) ";
        private const string OrigPrefix = "(managed -> pylon) ";

        private static readonly MethodInfo HandlersGetter = typeof(List<Delegate>).GetProperty("Item")!.GetGetMethod()!;
        private static readonly MethodInfo PointersGetter = typeof(List<IntPtr>).GetProperty("Item")!.GetGetMethod()!;
        private static readonly FieldInfo HandlersField =
            typeof(HookState).GetField(nameof(HookState.Handlers), BindingFlags.Static | BindingFlags.NonPublic)!;
        private static readonly FieldInfo OrigDelegatesField =
            typeof(HookState).GetField(nameof(HookState.OrigDelegates), BindingFlags.Static | BindingFlags.NonPublic)!;
        private static readonly FieldInfo OrigPointersField =
            typeof(HookState).GetField(nameof(HookState.OrigPointers), BindingFlags.Static | BindingFlags.NonPublic)!;
        private static readonly System.Reflection.FieldInfo ManagedToIl2CppField =
            typeof(HookStub).GetField(nameof(ManagedToIl2Cpp), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        private static readonly System.Reflection.FieldInfo Il2CppToManagedField =
            typeof(HookStub).GetField(nameof(Il2CppToManaged), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        private static readonly MethodInfo ReportExceptionMethod =
            typeof(HookState).GetMethod(nameof(HookState.ReportException), BindingFlags.Static | BindingFlags.NonPublic)!;

        /// <summary>可注入的字符串转换（真机 IL2CPP，桌面探针可换假实现复现桩 IL）。</summary>
        internal static Func<string, IntPtr> ManagedToIl2Cpp = IL2CPP.ManagedStringToIl2Cpp;
        internal static Func<IntPtr, string> Il2CppToManaged = IL2CPP.Il2CppStringToManaged;
        private static readonly MethodInfo StringToManagedInvoke =
            typeof(Func<IntPtr, string>).GetMethod("Invoke")!;
        private static readonly MethodInfo ManagedToStringInvoke =
            typeof(Func<string, IntPtr>).GetMethod("Invoke")!;
        private static readonly MethodInfo ToPtr = typeof(IL2CPP).GetMethod(nameof(IL2CPP.Il2CppObjectBaseToPtr))!;
        private static readonly MethodInfo GetTypeFromHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
        private static readonly MethodInfo GetClassPointer = typeof(Il2CppClassPointerStore)
            .GetMethod(nameof(Il2CppClassPointerStore.GetNativeClassPointer), new[] { typeof(Type) })!;
        private static readonly MethodInfo ValueBox = typeof(IL2CPP).GetMethod(nameof(IL2CPP.il2cpp_value_box))!;
        private static readonly MethodInfo ObjectUnbox = typeof(IL2CPP).GetMethod(nameof(IL2CPP.il2cpp_object_unbox))!;

        /// <summary>生成入口桩，返回 (方法, 原生入口指针)。</summary>
        internal static (Delegate stub, IntPtr entry) BuildEntry(HookPlan plan, int slot)
        {
            var nativeParams = plan.NativeParams.Concat(new[] { typeof(IntPtr) }).ToArray();
            var method = new DynamicMethod(EntryPrefix + plan.Label, plan.NativeReturn, nativeParams,
                typeof(HookStub).Module, skipVisibility: true);

            var il = method.GetILGenerator();
            var handlerLocal = il.DeclareLocal(plan.HandlerType);
            var origLocal = il.DeclareLocal(plan.OrigType);
            var byrefLocals = new LocalBuilder[plan.ParamTypes.Length];
            var boxedByrefLocals = new LocalBuilder[plan.ParamTypes.Length];

            // 返回值必须先落局部变量：leave 会清空栈，带值跳出 try 块是非法的。
            LocalBuilder resultLocal = null;
            if (plan.NativeReturn != typeof(void)) resultLocal = il.DeclareLocal(plan.NativeReturn);

            il.BeginExceptionBlock();

            // handler = (HandlerType)HookState.Handlers[slot];
            il.Emit(OpCodes.Ldsfld, HandlersField);
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Callvirt, HandlersGetter);
            il.Emit(OpCodes.Castclass, plan.HandlerType);
            il.Emit(OpCodes.Stloc, handlerLocal);

            // orig = (OrigType)HookState.OrigDelegates[slot];
            il.Emit(OpCodes.Ldsfld, OrigDelegatesField);
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Callvirt, HandlersGetter);
            il.Emit(OpCodes.Castclass, plan.OrigType);
            il.Emit(OpCodes.Stloc, origLocal);

            // handler(orig, self?, 参数…)
            il.Emit(OpCodes.Ldloc, handlerLocal);
            il.Emit(OpCodes.Ldloc, origLocal);

            var argIndex = 0;
            if (!plan.IsStatic)
            {
                il.Emit(OpCodes.Ldarg, argIndex);
                EmitNativeToManaged(il, plan.DeclaringType, typeof(IntPtr));
                argIndex++;
            }

            for (var i = 0; i < plan.ParamTypes.Length; i++)
            {
                var managed = plan.ParamTypes[i];
                var native = plan.NativeParams[argIndex];

                if (managed.IsByRef)
                {
                    var element = managed.GetElementType()!;
                    if (HookPlan.IsBoxedValueType(element) && native == typeof(IntPtr))
                    {
                        // ref/out 装箱值类型：原生给数据指针，托管侧要 ref 代理对象
                        var local = il.DeclareLocal(element);
                        boxedByrefLocals[i] = local;
                        if (plan.ParamIsOut[i])
                        {
                            il.Emit(OpCodes.Ldloca, local);
                            il.Emit(OpCodes.Initobj, element);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldarg, argIndex);
                            EmitBoxFromPointer(il, element);
                            il.Emit(OpCodes.Stloc, local);
                        }
                        il.Emit(OpCodes.Ldloca, local);
                    }
                    else if (native == managed)
                    {
                        // 值类型 ref：原生指针就是变量本身，直接透传（handler 改了立即生效）
                        il.Emit(OpCodes.Ldarg, argIndex);
                    }
                    else
                    {
                        var nativeElement = native.GetElementType()!;
                        var local = il.DeclareLocal(element);
                        byrefLocals[i] = local;

                        if (plan.ParamIsOut[i])
                        {
                            il.Emit(OpCodes.Ldloca, local);
                            il.Emit(OpCodes.Initobj, element);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldarg, argIndex);
                            il.Emit(OpCodes.Ldobj, nativeElement);
                            EmitNativeToManaged(il, element, nativeElement);
                            il.Emit(OpCodes.Stloc, local);
                        }

                        il.Emit(OpCodes.Ldloca, local);
                    }
                }
                else
                {
                    il.Emit(OpCodes.Ldarg, argIndex);
                    EmitNativeToManaged(il, managed, native);
                }

                argIndex++;
            }

            il.Emit(OpCodes.Callvirt, plan.HandlerType.GetMethod("Invoke")!);

            // 托管返回值 → 原生形态，然后存进局部变量（不能带值 leave）
            if (resultLocal != null)
            {
                EmitManagedToNative(il, plan.ReturnType, plan.NativeReturn);
                il.Emit(OpCodes.Stloc, resultLocal);
            }

            // ref/out 写回（值类型 ref 已直接透传，不在这里）
            argIndex = plan.IsStatic ? 0 : 1;
            for (var i = 0; i < plan.ParamTypes.Length; i++)
            {
                if (boxedByrefLocals[i] != null)
                {
                    var element = plan.ParamTypes[i].GetElementType()!;
                    var info = MirrorTypes.For(element);
                    il.Emit(OpCodes.Ldarg, argIndex);            // 目标数据指针
                    il.Emit(OpCodes.Ldloc, boxedByrefLocals[i]);
                    il.Emit(OpCodes.Call, ToPtr);
                    il.Emit(OpCodes.Call, ObjectUnbox);          // 源数据指针
                    il.Emit(OpCodes.Ldc_I4, info.Size);
                    il.Emit(OpCodes.Cpblk);
                }
                else if (byrefLocals[i] != null)
                {
                    var element = plan.ParamTypes[i].GetElementType()!;
                    var nativeElement = plan.NativeParams[argIndex].GetElementType()!;
                    il.Emit(OpCodes.Ldarg, argIndex);
                    il.Emit(OpCodes.Ldloc, byrefLocals[i]);
                    EmitManagedToNative(il, element, nativeElement);
                    il.Emit(OpCodes.Stobj, nativeElement);
                }
                argIndex++;
            }

            il.BeginCatchBlock(typeof(Exception));
            var exceptionLocal = il.DeclareLocal(typeof(Exception));
            il.Emit(OpCodes.Stloc, exceptionLocal);
            il.Emit(OpCodes.Ldstr, "entry:" + plan.Label);
            il.Emit(OpCodes.Ldloc, exceptionLocal);
            il.Emit(OpCodes.Call, ReportExceptionMethod);
            if (resultLocal != null)
            {
                il.Emit(OpCodes.Ldloca, resultLocal);
                il.Emit(OpCodes.Initobj, plan.NativeReturn);
            }
            il.EndExceptionBlock();

            if (resultLocal != null) il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);

            var delegateType = NativeDelegateFactory.Create(plan.NativeReturn, nativeParams, CallingConvention.Cdecl);
            var stub = method.CreateDelegate(delegateType);
            HookState.KeepAlive.Add(stub);
            var entry = Marshal.GetFunctionPointerForDelegate(stub);
            if (entry == IntPtr.Zero)
                throw new InvalidOperationException($"{plan.Label} 的入口桩拿不到函数指针");
            return (stub, entry);
        }

        /// <summary>生成 orig 桩（托管签名），返回强类型委托。</summary>
        internal static Delegate BuildOrig(HookPlan plan, int slot)
        {
            var managedParams = new List<Type>();
            if (!plan.IsStatic) managedParams.Add(plan.DeclaringType);
            managedParams.AddRange(plan.ParamTypes);

            var method = new DynamicMethod(OrigPrefix + plan.Label, plan.ReturnType, managedParams.ToArray(),
                typeof(HookStub).Module, skipVisibility: true);

            var il = method.GetILGenerator();
            var byrefLocals = new LocalBuilder[plan.ParamTypes.Length];

            // ⚠ 原生返回值必须先落在原生类型的局部变量：calli 返回的是 NativeReturn（string→IntPtr），
            //    声明成托管返回类型（string）直接 Stloc 类型不匹配 → InvalidProgramException（CLR detected invalid program）。
            var nativeResultLocal = plan.NativeReturn != typeof(void)
                ? il.DeclareLocal(plan.NativeReturn)
                : null;

            var argIndex = 0;
            if (!plan.IsStatic)
            {
                il.Emit(OpCodes.Ldarg, argIndex++);
                il.Emit(OpCodes.Call, ToPtr);
            }

            for (var i = 0; i < plan.ParamTypes.Length; i++)
            {
                var managed = plan.ParamTypes[i];
                var native = plan.NativeParams[argIndex];

                if (managed.IsByRef)
                {
                    var element = managed.GetElementType()!;
                    if (HookPlan.IsBoxedValueType(element) && native == typeof(IntPtr))
                    {
                        // ref/out 装箱值类型：取代理对象 → 拆箱拿数据指针
                        il.Emit(OpCodes.Ldarg, argIndex);
                        il.Emit(OpCodes.Ldobj, element);
                        EmitUnboxToPointer(il, element);
                    }
                    else if (native == managed)
                    {
                        il.Emit(OpCodes.Ldarg, argIndex);
                    }
                    else
                    {
                        var nativeElement = native.GetElementType()!;
                        var local = il.DeclareLocal(nativeElement);
                        byrefLocals[i] = local;

                        il.Emit(OpCodes.Ldarg, argIndex);   // 托管 ref
                        il.Emit(OpCodes.Ldobj, element);    // 取当前值
                        EmitManagedToNative(il, element, nativeElement);
                        il.Emit(OpCodes.Stloc, local);
                        il.Emit(OpCodes.Ldloca, local);     // 原生指针
                    }
                }
                else
                {
                    il.Emit(OpCodes.Ldarg, argIndex);
                    EmitManagedToNative(il, managed, native);
                }

                argIndex++;
            }

            // 隐藏尾参 MethodInfo*
            il.Emit(OpCodes.Ldc_I8, plan.MethodInfoPtr.ToInt64());
            il.Emit(OpCodes.Conv_I);

            // 下一跳地址（最后一个压栈）
            il.Emit(OpCodes.Ldsfld, OrigPointersField);
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Callvirt, PointersGetter);

            var nativeParams = plan.NativeParams.Concat(new[] { typeof(IntPtr) }).ToArray();
            il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, plan.NativeReturn, nativeParams);

            if (nativeResultLocal != null)
            {
                il.Emit(OpCodes.Stloc, nativeResultLocal);

                // ref/out 写回（值类型 ref 直接透传；装箱值类型的改动已在箱内可见）
                argIndex = plan.IsStatic ? 0 : 1;
                for (var i = 0; i < plan.ParamTypes.Length; i++)
                {
                    if (byrefLocals[i] != null)
                    {
                        var element = plan.ParamTypes[i].GetElementType()!;
                        var nativeElement = plan.NativeParams[argIndex].GetElementType()!;
                        il.Emit(OpCodes.Ldarg, argIndex);
                        il.Emit(OpCodes.Ldloc, byrefLocals[i]);
                        EmitNativeToManaged(il, element, nativeElement);
                        il.Emit(OpCodes.Stobj, element);
                    }
                    argIndex++;
                }

                il.Emit(OpCodes.Ldloc, nativeResultLocal);
                EmitNativeToManaged(il, plan.ReturnType, plan.NativeReturn);   // IntPtr → string 等
            }

            il.Emit(OpCodes.Ret);

            var orig = method.CreateDelegate(plan.OrigType);
            HookState.KeepAlive.Add(orig);
            return orig;
        }

        /// <summary>生成"原样转发"的 handler（链头被撤销时用它保持 Dobby 钩子但不再做任何事）。</summary>
        internal static Delegate BuildPassthrough(HookPlan plan)
        {
            var handlerInvoke = plan.HandlerType.GetMethod("Invoke")!;
            var parameters = handlerInvoke.GetParameters().Select(p => p.ParameterType).ToArray();
            var method = new DynamicMethod("(pylon passthrough) " + plan.Label, handlerInvoke.ReturnType, parameters,
                typeof(HookStub).Module, skipVisibility: true);

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            for (var i = 1; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Callvirt, plan.OrigType.GetMethod("Invoke")!);
            il.Emit(OpCodes.Ret);

            var passthrough = method.CreateDelegate(plan.HandlerType);
            HookState.KeepAlive.Add(passthrough);
            return passthrough;
        }

        // ---------------- 原生 → 托管 ----------------

        /// <summary>栈顶是原生槽位值，转换成托管值。</summary>
        private static void EmitNativeToManaged(ILGenerator il, Type managed, Type native)
        {
            if (managed == native) return;

            if (HookPlan.IsBoxedValueType(managed))
            {
                // 原生按值传的结构体：落镜像局部变量 → 装箱 → 包装成代理对象
                var info = MirrorTypes.For(managed);
                var local = il.DeclareLocal(info.Mirror);
                il.Emit(OpCodes.Stloc, local);
                EmitBoxFromLocal(il, managed, local);
                return;
            }

            if (managed == typeof(bool))
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Cgt_Un);
                return;
            }

            if (managed == typeof(char))
            {
                il.Emit(OpCodes.Conv_U2);
                return;
            }

            if (managed.IsEnum) return; // 栈上就是底层整型

            if (managed == typeof(string))
            {
                // 实例方法调用：this ∈ 栈底、参数 ∈ 栈顶。
                // 当前栈顶是 IntPtr（参数），必须先存临时 → 压 this → 压参数 → Callvirt Invoke。
                var temp = il.DeclareLocal(native ?? typeof(IntPtr));
                il.Emit(OpCodes.Stloc, temp);
                il.Emit(OpCodes.Ldsfld, Il2CppToManagedField);
                il.Emit(OpCodes.Ldloc, temp);
                il.Emit(OpCodes.Callvirt, StringToManagedInvoke);
                return;
            }

            if (typeof(Il2CppObjectBase).IsAssignableFrom(managed))
            {
                EmitWrapReference(il, managed);
                return;
            }

            throw new NotSupportedException($"没有 原生→托管 的转换：{native?.Name} → {managed?.Name}");
        }

        /// <summary>栈顶是托管值，转换成原生槽位值。</summary>
        private static void EmitManagedToNative(ILGenerator il, Type managed, Type native)
        {
            if (managed == native) return;

            if (HookPlan.IsBoxedValueType(managed))
            {
                // 代理对象 → 拆箱 → 拷贝进镜像局部变量 → 按值交给原生。
                // null 代理（如 GUIPanel.Draw 的 Nullable<Color> 不传就是 null）：
                // ToPtr(null)=0 → unbox(0) 空指针 NRE；原生侧期望"清零结构体"（hasValue=false）。
                var info = MirrorTypes.For(managed);
                var mirror = il.DeclareLocal(info.Mirror);

                var end = il.DefineLabel();
                var nullLabel = il.DefineLabel();
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Beq, nullLabel);

                // 非 null：unbox → cpblk 进镜像
                var data = il.DeclareLocal(typeof(IntPtr));
                EmitUnboxToPointer(il, managed);
                il.Emit(OpCodes.Stloc, data);
                il.Emit(OpCodes.Ldloca, mirror);
                il.Emit(OpCodes.Conv_I);
                il.Emit(OpCodes.Ldloc, data);
                il.Emit(OpCodes.Ldc_I4, info.Size);
                il.Emit(OpCodes.Cpblk);
                il.Emit(OpCodes.Br, end);

                // null：清零镜像（hasValue=false 的默认值就是一串 0）
                il.MarkLabel(nullLabel);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldloca, mirror);
                il.Emit(OpCodes.Initobj, info.Mirror);

                il.MarkLabel(end);
                il.Emit(OpCodes.Ldloc, mirror);
                return;
            }

            if (managed == typeof(bool))
            {
                il.Emit(OpCodes.Conv_U1);
                return;
            }

            if (managed == typeof(char))
            {
                il.Emit(OpCodes.Conv_U2);
                return;
            }

            if (managed.IsEnum)
            {
                EmitConvToUnderlying(il, managed);
                return;
            }

            if (managed == typeof(string))
            {
                // 同理：栈顶是 string（参数），存临时 → 压 this → 压参数 → Callvirt Invoke。
                var temp = il.DeclareLocal(typeof(string));
                il.Emit(OpCodes.Stloc, temp);
                il.Emit(OpCodes.Ldsfld, ManagedToIl2CppField);
                il.Emit(OpCodes.Ldloc, temp);
                il.Emit(OpCodes.Callvirt, ManagedToStringInvoke);
                return;
            }

            if (typeof(Il2CppObjectBase).IsAssignableFrom(managed))
            {
                il.Emit(OpCodes.Call, ToPtr);
                return;
            }

            throw new NotSupportedException($"没有 托管→原生 的转换：{managed?.Name} → {native?.Name}");
        }

        /// <summary>栈顶 IntPtr → interop 包装（0 时给 null）。</summary>
        private static void EmitWrapReference(ILGenerator il, Type managedType)
        {
            var nullLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Beq, nullLabel);

            il.Emit(OpCodes.Newobj, managedType.GetConstructor(new[] { typeof(IntPtr) })
                                     ?? throw new NotSupportedException($"{managedType.Name} 没有 ctor(IntPtr)"));
            il.Emit(OpCodes.Br, endLabel);

            il.MarkLabel(nullLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull);
            il.MarkLabel(endLabel);
        }

        /// <summary>把镜像局部变量装箱成代理对象（栈顶）。</summary>
        private static void EmitBoxFromLocal(ILGenerator il, Type boxed, LocalBuilder mirror)
        {
            il.Emit(OpCodes.Ldtoken, boxed);
            il.Emit(OpCodes.Call, GetTypeFromHandle);
            il.Emit(OpCodes.Call, GetClassPointer);
            il.Emit(OpCodes.Ldloca, mirror);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Call, ValueBox);
            EmitWrapReference(il, boxed);
        }

        /// <summary>栈顶是原生数据指针，装箱成代理对象（栈顶）。</summary>
        private static void EmitBoxFromPointer(ILGenerator il, Type boxed)
        {
            var data = il.DeclareLocal(typeof(IntPtr));
            il.Emit(OpCodes.Stloc, data);

            il.Emit(OpCodes.Ldtoken, boxed);
            il.Emit(OpCodes.Call, GetTypeFromHandle);
            il.Emit(OpCodes.Call, GetClassPointer);
            il.Emit(OpCodes.Ldloc, data);
            il.Emit(OpCodes.Call, ValueBox);
            EmitWrapReference(il, boxed);
        }

        /// <summary>栈顶是代理对象，拆箱成数据指针（栈顶）。</summary>
        private static void EmitUnboxToPointer(ILGenerator il, Type boxed)
        {
            il.Emit(OpCodes.Call, ToPtr);
            il.Emit(OpCodes.Call, ObjectUnbox);
        }

        private static void EmitConvToUnderlying(ILGenerator il, Type enumType)
        {
            var target = Enum.GetUnderlyingType(enumType);
            if (target == typeof(byte) || target == typeof(sbyte)) il.Emit(OpCodes.Conv_U1);
            else if (target == typeof(short) || target == typeof(ushort)) il.Emit(OpCodes.Conv_I2);
            else if (target == typeof(int) || target == typeof(uint)) il.Emit(OpCodes.Conv_I4);
            else if (target == typeof(long) || target == typeof(ulong)) il.Emit(OpCodes.Conv_I8);
        }
    }
}