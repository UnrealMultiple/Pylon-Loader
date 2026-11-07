using System;
using System.Collections.Generic;
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

        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
            "continue","decimal","default","delegate","do","double","else","enum","event","explicit","extern",
            "false","finally","fixed","float","for","foreach","goto","if","implicit","in","int","interface",
            "internal","is","lock","long","namespace","new","null","object","operator","out","override","params",
            "private","protected","public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc",
            "static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked",
            "unsafe","ushort","using","virtual","void","volatile","while"
        };

        private static string Escape(string name) => Keywords.Contains(name) ? "@" + name : name;

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
        private static TypeReference MapTypeArgs(TypeReference type, List<GenericParameter>? typeArgs)
        {
            switch (type)
            {
                case GenericParameter gp:
                    // method-level generic parameters !!i must be lifted to host-class ?i: replaced with the same-named entry in typeArgs
                    return typeArgs != null && gp.Position < typeArgs.Count ? typeArgs[gp.Position] : gp;
                case GenericInstanceType gi:
                    {
                        var mapped = new GenericInstanceType(gi.ElementType);
                        foreach (var a in gi.GenericArguments) mapped.GenericArguments.Add(MapTypeArgs(a, typeArgs));
                        return mapped;
                    }
                default:
                    return type;
            }
        }

        /// <summary>Method-level generic parameter !!i -&gt; host-class generic parameter !i (same position).</summary>
        private static TypeReference MapMethodGeneric(TypeReference type, List<GenericParameter> args)
        {
            switch (type)
            {
                case GenericParameter gp:
                    return gp.Type == GenericParameterType.Method && gp.Position < args.Count
                        ? args[gp.Position] : gp;
                case GenericInstanceType gi:
                    {
                        var mapped = new GenericInstanceType(gi.ElementType);
                        foreach (var a in gi.GenericArguments) mapped.GenericArguments.Add(MapMethodGeneric(a, args));
                        return mapped;
                    }
                default:
                    return type;
            }
        }
    }
}
