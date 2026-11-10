using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Il2CppInterop.RuntimeDetour.HookGen;

public static partial class OnHookGenerator
{
    private sealed partial class Generator
    {
        internal sealed class MethodNode
        {
            public MethodDefinition Definition = null!;
            public string EventName = null!;
            public string OrigName => "orig_" + EventName;
            public string HookName => "hook_" + EventName;
            public int Index;

            /// <summary>Method-level generic parameter names (empty for non-generic methods).</summary>
            public List<string>? TypeParameterNames;
            public bool IsGenericMethod => TypeParameterNames is { Count: > 0 };
        }

        internal sealed class TypeNode
        {
            public TypeDefinition Definition = null!;
            public string Name = null!;
            public readonly List<MethodNode> Methods = new();
            public readonly List<TypeNode> Children = new();

            /// <summary>Type-level generic parameter names (empty for non-generic types).</summary>
            public List<string>? TypeParameterNames;
            public bool IsGenericType => TypeParameterNames is { Count: > 0 };
        }

        // ---------------- Building the tree ----------------

        internal TypeNode? BuildType(TypeDefinition type)
        {
            if (!IsTypeCandidate(type)) return null;

            var node = new TypeNode
            {
                Definition = type,
                Name = Escape(BaseName(type.Name))
            };
            if (type.HasGenericParameters)
            {
                node.TypeParameterNames = type.GenericParameters.Select(p => p.Name).ToList();
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            var hookable = new List<MethodDefinition>();
            foreach (var method in type.Methods)
            {
                if (IsHookable(method)) hookable.Add(method);
            }

            // Overloaded members (same name hooked more than once, ctors included) are named by
            // parameter types (NewProjectile_IEntitySource_float_...) instead of _1/_2 suffixes,
            // so a subscriber can tell overloads apart; UniqueName still resolves rare collisions.
            var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var method in hookable)
            {
                overloadCounts.TryGetValue(method.Name, out var count);
                overloadCounts[method.Name] = count + 1;
            }

            foreach (var method in hookable)
            {
                var baseName = method.IsConstructor ? "ctor" : Escape(method.Name);
                var eventName = overloadCounts[method.Name] > 1
                    ? OverloadEventName(baseName, method)
                    : baseName;
                eventName = UniqueName(eventName, used);
                var methodNode = new MethodNode { Definition = method, EventName = eventName, Index = node.Methods.Count };
                if (method.HasGenericParameters)
                {
                    // Generic methods: the event host class is named "method name + arity"; method-level !!i becomes host-class-level !i
                    methodNode.TypeParameterNames = method.GenericParameters.Select(p => p.Name).ToList();
                }
                node.Methods.Add(methodNode);
            }

            foreach (var nested in type.NestedTypes)
            {
                var child = BuildType(nested);
                if (child == null) continue;
                child.Name = UniqueName(child.Name, used);
                node.Children.Add(child);
            }

            return node.Methods.Count == 0 && node.Children.Count == 0 ? null : node;
        }

        private static bool IsTypeCandidate(TypeDefinition type)
        {
            if (!type.IsClass && !type.IsValueType) return false;
            if (type.IsEnum || type.IsInterface) return false;
            // il2cpp 代理把一部分游戏类生成为值类型包装（如 Terraria.TileObject）——它们的静态方法
            // 同样有 NativeMethodInfoPtr、同样可以 detour；BuildType 本来就会把"没有可钩方法的类型"丢掉，
            // 所以这里放行值类型只会为真的可钩对象多出宿主类，不会为纯数据 struct 生成空壳。
            if (type.BaseType?.FullName == "System.MulticastDelegate") return false;
            if (!IsValidIdentifier(BaseName(type.Name))) return false;
            if (string.IsNullOrEmpty(type.Namespace) && BaseName(type.Name).StartsWith("_", StringComparison.Ordinal)) return false;

            for (var parent = type.DeclaringType; parent != null; parent = parent.DeclaringType)
            {
                if (!IsValidIdentifier(BaseName(parent.Name))) return false;
            }
            return type.Methods.Count > 0;
        }

        /// <summary>Strips the generic arity suffix (List`1 -&gt; List) and compiler-generated names.</summary>
        private static string BaseName(string name)
        {
            var tick = name.IndexOf('\u0060');
            return tick >= 0 ? name.Substring(0, tick) : name;
        }

        private static bool IsHookable(MethodDefinition method)
        {
            // Constructors are hookable too: il2cpp .ctor is a native function with its own
            // NativeMethodInfoPtr_* field; the engine bridges them via ConstructorTarget.
            if (method.IsConstructor)
            {
                return !method.IsAbstract && !method.IsStatic
                    && method.Parameters.All(p => IsRepresentable(p.ParameterType))
                    && HasMethodInfoField(method);
            }
            // 属性访问器（get_/set_）也是 IsSpecialName，但它们是普通函数体，可以挂钩子；
            // 运算符（op_*）与其它特殊方法仍然跳过。
            if (method.IsSpecialName && !IsAccessor(method.Name)) return false;
            if (method.IsAbstract) return false;
            if (!IsValidIdentifier(method.Name)) return false;
            // ref T returns are supported: delegates use the element type, the engine writes the
            // handler's value back through the original data pointer.
            if (method.ReturnType is ByReferenceType byRefReturn && !IsRepresentable(byRefReturn.ElementType)) return false;
            if (method.ReturnType is not ByReferenceType && !IsRepresentable(method.ReturnType)) return false;
            foreach (var parameter in method.Parameters)
                if (!IsRepresentable(parameter.ParameterType)) return false;
            return HasMethodInfoField(method);
        }

        private static bool IsAccessor(string name) =>
            name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal);

        private static bool HasMethodInfoField(MethodDefinition method)
        {
            if (!method.HasBody) return false;
            try
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Ldsfld) continue;
                    if (instruction.Operand is not FieldReference field) continue;
                    // regular methods: NativeMethodInfoPtr_*;
                    // generic methods: declaring type MethodInfoStoreGeneric_*, field name Pointer.
                    if (field.Name.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal)) return true;
                    if (field.Name == "Pointer" &&
                        field.DeclaringType?.Name.StartsWith("MethodInfoStoreGeneric_", StringComparison.Ordinal) == true)
                        return true;
                }
            }
            catch { return false; }
            return false;
        }

        private static bool IsRepresentable(TypeReference type)
        {
            switch (type)
            {
                case null: return false;
                case ByReferenceType byRef: return IsRepresentable(byRef.ElementType);
                case PointerType: return false;
                case FunctionPointerType: return false;
                case ArrayType array: return array.Rank == 1 && IsRepresentable(array.ElementType);
                case GenericParameter: return true;   // generic parameters are compatible (referencing declaring type/method-level generic parameters)
                case GenericInstanceType generic:
                    {
                        // closed generic instances (Il2CppSystem.Nullable<Color> etc., boxed value types) are allowed:
                        // the engine (HookPlan.NativeSlot + MirrorTypes) bridges them by il2cpp value size at hook time.
                        if (!(generic.ElementType is TypeReference element)) return false;
                        return generic.GenericArguments.All(IsRepresentable);
                    }
                default:
                    for (var current = type; current != null; current = current.DeclaringType)
                    {
                        if (!IsValidIdentifier(current.Name)) return false;
                        if (current.HasGenericParameters) return false;
                    }
                    return true;
            }
        }
    }
}
