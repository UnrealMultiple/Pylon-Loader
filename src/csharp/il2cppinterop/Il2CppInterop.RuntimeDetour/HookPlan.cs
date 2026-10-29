#nullable disable
// 层：Hook 引擎 —— 一次 hook 的"签名计划"。
//
// 把「托管签名」翻译成「原生槽位签名」：引用/字符串 → IntPtr，bool → byte，char → ushort，
// 枚举 → 底层整型，可直传的值类型（il2cpp 结构体、Nullable<T>）原样按值传。
// ref/out 用 byref 槽位（值类型）或 IntPtr&（引用/字符串）。
// 这里只做分析与校验，不碰 IL 也不碰内存。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.InteropTypes;

namespace Il2CppInterop.RuntimeDetour
{
    /// <summary>一次 hook 的签名计划（目标方法 + handler 校验 + 原生槽位类型）。</summary>
    internal sealed class HookPlan
    {
        internal MethodInfo Target;

        internal Type DeclaringType;
        internal bool IsStatic;
        internal Type ReturnType;
        internal Type[] ParamTypes;

        /// <summary>out 参数（入口桩不预读，直接给默认值）。</summary>
        internal bool[] ParamIsOut;

        /// <summary>原生槽位类型：入口桩参数表（不含隐藏的 methodInfo）。</summary>
        internal Type[] NativeParams;

        /// <summary>原生返回类型。</summary>
        internal Type NativeReturn;

        /// <summary>handler 委托类型：(orig, self?, 参数…)。</summary>
        internal Type HandlerType;

        /// <summary>orig 委托类型：目标方法自己的托管签名。</summary>
        internal Type OrigType;

        /// <summary>il2cpp 的 MethodInfo*（原生调用要把它当隐藏尾参传）。</summary>
        internal IntPtr MethodInfoPtr;

        /// <summary>目标方法的原生代码地址（Dobby 要 patch 它）。</summary>
        internal IntPtr FunctionPtr;

        internal string Label => $"{DeclaringType?.Name}.{Target?.Name}";

        /// <summary>桌面探针用：绕过 il2cpp 方法指针解析，直接给 (MethodInfo*, 代码地址)。</summary>
        internal static Func<MethodInfo, (IntPtr methodInfo, IntPtr function)> PointersOverride;

        internal static HookPlan Create(MethodBase target, Delegate handler)
        {
            if (target is not MethodInfo method)
                throw new NotSupportedException("只能钩 MethodInfo（构造器/属性访问器请先取到具体方法）");
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            if (method.DeclaringType == null)
                throw new NotSupportedException("目标方法没有声明类型");
            if (method.DeclaringType.ContainsGenericParameters)
                throw new NotSupportedException("不支持泛型类型上的方法");

            var plan = new HookPlan
            {
                Target = method,
                DeclaringType = method.DeclaringType,
                IsStatic = method.IsStatic,
                ReturnType = method.ReturnType,
                ParamTypes = method.GetParameters().Select(p => p.ParameterType).ToArray(),
                ParamIsOut = method.GetParameters().Select(p => p.IsOut).ToArray()
            };

            if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
                throw new NotSupportedException("不支持开放泛型方法，请传闭合后的具体方法");

            // handler：(orig, self?, 参数…)
            var handlerParameters = handler.Method.GetParameters();
            if (handlerParameters.Length == 0 || !typeof(Delegate).IsAssignableFrom(handlerParameters[0].ParameterType))
                throw new ArgumentException("handler 的第一个参数必须是 orig 委托（如 Action<NPC>）", nameof(handler));

            plan.HandlerType = handler.GetType();
            plan.OrigType = handlerParameters[0].ParameterType;
            plan.ValidateHandler(handlerParameters);
            plan.CheckSupportedTypes();

            plan.NativeParams = BuildNativeParams(plan);
            plan.NativeReturn = NativeSlot(plan.ReturnType, plan.Label + " 的返回值");

            plan.ResolveNativePointers();
            return plan;
        }

        /// <summary>handler 的参数表必须等于 (orig, self?, 参数…) 且类型能对上。</summary>
        private void ValidateHandler(ParameterInfo[] handlerParameters)
        {
            var invoke = OrigType.GetMethod("Invoke");
            if (invoke == null) throw new ArgumentException("orig 必须是委托类型", "handler");

            var expected = new List<Type>();
            if (!IsStatic) expected.Add(DeclaringType);
            expected.AddRange(ParamTypes);

            var actual = invoke.GetParameters().Select(p => p.ParameterType).ToArray();
            var expectedArray = expected.ToArray();

            var same = actual.Length == expectedArray.Length &&
                       invoke.ReturnType == ReturnType &&
                       !actual.Where((t, i) => t != expectedArray[i]).Any();
            if (!same)
            {
                throw new ArgumentException(
                    $"orig 委托签名与目标不符：应为 {Describe(ReturnType, expectedArray)}，实际 {Describe(invoke.ReturnType, actual)}",
                    "handler");
            }

            var expectedCount = 1 + expectedArray.Length;
            if (handlerParameters.Length != expectedCount)
            {
                throw new ArgumentException(
                    $"handler 需要 {expectedCount} 个参数（orig + {(IsStatic ? "目标参数" : "self + 目标参数")}），实际 {handlerParameters.Length} 个",
                    "handler");
            }
        }

        /// <summary>能力校验：能表达成原生槽位就过，不支持的明确抛，不静默。</summary>
        private void CheckSupportedTypes()
        {
            foreach (var parameter in ParamTypes)
            {
                var type = parameter.IsByRef ? parameter.GetElementType() : parameter;
                if (type == null) throw new NotSupportedException($"{Label} 有无法解析的参数类型");
                if (type.IsPointer)
                    throw new NotSupportedException($"{Label} 有指针参数，不支持");
                if (type.IsByRef)
                    throw new NotSupportedException($"{Label} 有嵌套 byref 参数，不支持");
                // 先跑一遍槽位映射，失败会抛带说明的异常
                NativeSlot(type, Label + " 的参数 " + type.Name);
            }

            if (ReturnType.IsByRef)
                throw new NotSupportedException($"{Label} 返回 byref，不支持");

            NativeSlot(ReturnType, Label + " 的返回值");

            if (!IsStatic && !typeof(Il2CppObjectBase).IsAssignableFrom(DeclaringType))
            {
                throw new NotSupportedException(
                    $"{Label} 是实例方法，但声明类型 {DeclaringType?.Name} 不是 Il2CppObjectBase 派生（不是游戏类型？）");
            }
        }

        private static Type[] BuildNativeParams(HookPlan plan)
        {
            var list = new List<Type>();
            if (!plan.IsStatic) list.Add(typeof(IntPtr)); // self
            foreach (var parameter in plan.ParamTypes) list.Add(NativeSlot(parameter, plan.Label + " 的参数"));
            return list.ToArray();
        }

        /// <summary>托管类型 → 原生槽位类型。</summary>
        internal static Type NativeSlot(Type type, string what)
        {
            if (type == null) throw new NotSupportedException($"{what} 的类型为空");
            if (type.IsByRef)
            {
                var element = type.GetElementType();
                if (element == null) throw new NotSupportedException($"{what} 的 byref 元素为空");
                if (IsBoxedValueType(element)) return typeof(IntPtr);                 // ref/out 装箱值类型：数据指针
                if (IsReferenceLike(element)) return typeof(IntPtr).MakeByRefType(); // ref/out 引用：指针槽
                return NativeSlot(element, what).MakeByRefType();                     // 值类型：原始指针
            }
            if (type == typeof(string)) return typeof(IntPtr);
            if (IsBoxedValueType(type)) return MirrorTypes.For(type).Mirror;          // 按值传的装箱值类型
            if (typeof(Il2CppObjectBase).IsAssignableFrom(type)) return typeof(IntPtr);
            if (type == typeof(bool)) return typeof(byte);                 // il2cpp bool 是 1 字节
            if (type == typeof(char)) return typeof(ushort);
            if (type.IsEnum) return NativeSlot(Enum.GetUnderlyingType(type), what);
            if (type.IsPrimitive) return type;
            if (type == typeof(IntPtr) || type == typeof(UIntPtr)) return type;

            if (type.IsArray)
                throw new NotSupportedException($"{what} 的 {type.Name} 是 CLR 数组（il2cpp 代理里不该出现），不支持");
            if (type.IsPointer)
                throw new NotSupportedException($"{what} 是指针，不支持");
            if (type.IsGenericType && type.GetGenericTypeDefinition() != typeof(Nullable<>))
                throw new NotSupportedException($"{what} 是泛型值类型 {type.Name}，布局无法保证，不支持");
            if (IsBlittableValue(type)) return type;

            throw new NotSupportedException(
                $"{what} 的类型 {type.Name} 不是可直传的值类型（含引用字段/未知布局），不支持");
        }

        internal static bool IsReferenceLike(Type type) =>
            type == typeof(string) || (typeof(Il2CppObjectBase).IsAssignableFrom(type) && !IsBoxedValueType(type));

        /// <summary>
        /// "class 代理、原生按值传"的值类型：Il2CppInterop 把泛型值类型
        /// （Il2CppSystem.Nullable&lt;T&gt; 等）生成为 Il2CppSystem.ValueType 派生的 class，
        /// 但 IL2CPP 编译出来的签名是按值传的结构体。这类参数要用镜像结构 + 装箱桥接。
        /// </summary>
        internal static bool IsBoxedValueType(Type type)
        {
            if (type == null || type.IsValueType) return false;
            for (var current = type.BaseType; current != null; current = current.BaseType)
            {
                if (current.FullName == "Il2CppSystem.ValueType") return true;
            }
            return false;
        }

        /// <summary>能不能按值直传：只含基础值类型/枚举/嵌套可直传结构体。</summary>
        internal static bool IsBlittableValue(Type type)
        {
            if (!type.IsValueType) return false;
            if (type.IsPrimitive) return true;
            if (type.IsEnum) return true;
            if (type == typeof(IntPtr) || type == typeof(UIntPtr)) return true;
            if (type.IsPointer || type.ContainsGenericParameters) return false;
            if (type.IsGenericType)
            {
                if (type.GetGenericTypeDefinition() != typeof(Nullable<>)) return false;
                return IsBlittableValue(type.GetGenericArguments()[0]);
            }

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                if (field.FieldType.IsPointer) return false;
                if (!field.FieldType.IsValueType) return false;
                if (!IsBlittableValue(field.FieldType)) return false;
            }
            return true;
        }

        /// <summary>托管 MethodInfo → il2cpp 的 MethodInfo* 与代码地址。</summary>
        private void ResolveNativePointers()
        {
            if (PointersOverride != null)
            {
                (MethodInfoPtr, FunctionPtr) = PointersOverride(Target);
                return;
            }

            var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(Target);
            if (field == null)
            {
                var accessor = Il2CppInteropUtils.GetIl2CppFieldInfoPointerFieldForGeneratedFieldAccessor(Target);
                if (accessor != null)
                    throw new NotSupportedException($"{Label} 是字段访问器，不能挂钩");
                throw new NotSupportedException($"{Label} 没有 il2cpp 方法指针字段（不是 interop 生成的代理方法？）");
            }

            MethodInfoPtr = (IntPtr)field.GetValue(null);
            if (MethodInfoPtr == IntPtr.Zero)
                throw new InvalidOperationException($"{Label} 的 MethodInfo* 为空（类型还没初始化？）");

            // IL2CPP 的 MethodInfo 头部第一个指针宽字段就是 methodPointer（与内核 ModApi.cpp 同一约定）。
            FunctionPtr = Marshal.ReadIntPtr(MethodInfoPtr);
            if (FunctionPtr == IntPtr.Zero)
                throw new InvalidOperationException($"{Label} 的 methodPointer 为空（方法未编译？）");
        }

        internal static string Describe(Type returnType, Type[] parameters)
        {
            var text = new System.Text.StringBuilder();
            text.Append(returnType?.Name).Append(" (");
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(parameters[i].Name);
            }
            return text.Append(')').ToString();
        }
    }
}

