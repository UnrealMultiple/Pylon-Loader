using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Il2CppInterop.RuntimeDetour.HookGen;

public static partial class OnHookGenerator
{
    private sealed partial class Generator
    {
        // ---------------- Naming & generic mapping ----------------

        private static string UniqueName(string baseName, HashSet<string> used)
        {
            var candidate = baseName;
            var index = 1;
            while (!IsFree(candidate, used)) candidate = baseName + "_" + index++;
            used.Add(candidate);
            used.Add("orig_" + candidate);
            used.Add("hook_" + candidate);
            return candidate;
        }

        private static bool IsFree(string candidate, HashSet<string> used) =>
            !used.Contains(candidate) && !used.Contains("orig_" + candidate) && !used.Contains("hook_" + candidate);

        // Metadata identifiers never contain C#'s source-only @ escape.
        private static string Escape(string name) => name;

        /// <summary>IL primitive names -&gt; C# keyword style segments for overload event names (Single -&gt; float, Int32 -&gt; int ...).</summary>
        private static readonly Dictionary<string, string> PrimitiveSegmentNames = new(StringComparer.Ordinal)
        {
            ["Single"] = "float",
            ["Double"] = "double",
            ["Int32"] = "int",
            ["UInt32"] = "uint",
            ["Int64"] = "long",
            ["UInt64"] = "ulong",
            ["Int16"] = "short",
            ["UInt16"] = "ushort",
            ["Byte"] = "byte",
            ["SByte"] = "sbyte",
            ["Boolean"] = "bool",
            ["Char"] = "char",
            ["String"] = "string",
            ["Object"] = "object",
            ["Decimal"] = "decimal"
        };

        /// <summary>
        ///     One event-name segment for a parameter type: simple type name with IL primitives in
        ///     C# keyword form (float/int/...), generics as Element_Arg1_Arg2, arrays with an Array
        ///     suffix and ref/out params with a Ref suffix - e.g. IEntitySource, float, List_int.
        /// </summary>
        private static string TypeSegmentName(TypeReference type)
        {
            switch (type)
            {
                case ByReferenceType byRef:
                    return TypeSegmentName(byRef.ElementType) + "Ref";
                case PointerType:
                case FunctionPointerType:
                    return "Unsafe";
                case ArrayType array:
                    return TypeSegmentName(array.ElementType) + "Array";
                case GenericParameter gp:
                    return gp.Name;
                case GenericInstanceType generic:
                    {
                        var name = TypeSegmentName(generic.ElementType);
                        return generic.GenericArguments.Count == 0
                            ? name
                            : name + "_" + string.Join("_", generic.GenericArguments.Select(TypeSegmentName));
                    }
                default:
                    {
                        var simple = BaseName(type.Name);
                        return PrimitiveSegmentNames.TryGetValue(simple, out var mapped) ? mapped : simple;
                    }
            }
        }

        /// <summary>
        ///     Event name for an overloaded method: method name + "_" + parameter type segments,
        ///     e.g. NewProjectile_IEntitySource_float_float_..._NewProjectileModifier.
        /// </summary>
        private static string OverloadEventName(string baseName, MethodDefinition method) =>
            baseName + "_" + string.Join("_", method.Parameters.Select(p => TypeSegmentName(p.ParameterType)));

        /// <summary>Reuses the proxy method's parameter names; empty/invalid names fall back to argN; duplicates get a _1 suffix.</summary>
        private static string ParameterName(ParameterDefinition parameter, HashSet<string> used)
        {
            var name = parameter.Name;
            if (string.IsNullOrEmpty(name) || !IsValidIdentifier(name)) name = "arg" + parameter.Index;
            var candidate = name;
            var index = 1;
            while (!used.Add(candidate)) candidate = name + "_" + index++;
            return candidate;
        }

        private static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!char.IsLetter(name[0]) && name[0] != '_') return false;
            foreach (var c in name)
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        /// <summary>
        ///     Maps generic parameters (!!0/!0) in a signature to the host class's generic parameters
        ///     (used for methods on generic types); everything else is unchanged. When typeArgs is
        ///     null this is the identity map.
        /// </summary>
        private static TypeReference MapTarget(TypeReference type, IList<GenericParameter> typeArgs,
            IList<GenericParameter> methodArgs) => MapSignature(type, p =>
            {
                var args = p.Type == GenericParameterType.Method ? methodArgs : typeArgs;
                return p.Position < args.Count ? args[p.Position] : p;
            });

        /// <summary>One recursive visitor for every signature wrapper, preserving modifiers and array bounds.</summary>
        private static TypeReference MapSignature(TypeReference type, Func<GenericParameter, TypeReference> map)
        {
            TypeReference Visit(TypeReference t) => MapSignature(t, map);
            switch (type)
            {
                case GenericParameter p: return map(p);
                case ByReferenceType t: return new ByReferenceType(Visit(t.ElementType));
                case PointerType t: return new PointerType(Visit(t.ElementType));
                case PinnedType t: return new PinnedType(Visit(t.ElementType));
                case SentinelType t: return new SentinelType(Visit(t.ElementType));
                case RequiredModifierType t: return new RequiredModifierType(Visit(t.ModifierType), Visit(t.ElementType));
                case OptionalModifierType t: return new OptionalModifierType(Visit(t.ModifierType), Visit(t.ElementType));
                case ArrayType t:
                    var array = t.IsVector ? new ArrayType(Visit(t.ElementType)) : new ArrayType(Visit(t.ElementType), t.Rank);
                    if (!t.IsVector)
                        for (var i = 0; i < t.Dimensions.Count; i++)
                            array.Dimensions[i] = new ArrayDimension(t.Dimensions[i].LowerBound, t.Dimensions[i].UpperBound);
                    return array;
                case GenericInstanceType t:
                    var generic = new GenericInstanceType(Visit(t.ElementType));
                    foreach (var arg in t.GenericArguments) generic.GenericArguments.Add(Visit(arg));
                    return generic;
                case FunctionPointerType t:
                    var function = new FunctionPointerType { CallingConvention = t.CallingConvention,
                        HasThis = t.HasThis, ExplicitThis = t.ExplicitThis, ReturnType = Visit(t.ReturnType) };
                    foreach (var p in t.Parameters)
                        function.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, Visit(p.ParameterType)));
                    return function;
                default: return type;
            }
        }
    }
}
