// OnHookGenerator: emits On.* hooks directly as IL into the Il2CppInterop-generated proxy assemblies.
//
// Shape (same as MonoMod HookGen, but emits IL instead of C# source):
//   namespace On.Terraria { public static class Main {
//       public delegate void orig_Update(Terraria.Main self, GameTime gameTime);
//       public delegate void hook_Update(orig_Update orig, Terraria.Main self, GameTime gameTime);
//       public static event hook_Update Update { add { ... } remove { ... } }
//   } }
//
// Event accessors only do two things: BindOnce(...) creates the endpoint on first subscription, then Add/Remove forward to it.
// Targets are described as typeof + name + parameter type array (no string-built signatures); the runtime endpoint resolves by exact signature.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Il2CppInterop.RuntimeDetour.HookGen;
public sealed class HookGenResult
{
    public int Types { get; set; }
    public int Hooks { get; set; }
    public string OutputPath { get; set; } = string.Empty;
}

public static class OnHookGenerator
{
    private sealed class MethodNode
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

    private sealed class TypeNode
    {
        public TypeDefinition Definition = null!;
        public string Name = null!;
        public readonly List<MethodNode> Methods = new();
        public readonly List<TypeNode> Children = new();

        /// <summary>Type-level generic parameter names (empty for non-generic types).</summary>
        public List<string>? TypeParameterNames;
        public bool IsGenericType => TypeParameterNames is { Count: > 0 };
    }

    /// <summary>
    ///     Adds On.* to the proxy assembly in place and returns statistics. runtimeDetourPath is the
    ///     Il2CppInterop.RuntimeDetour.dll this deployment will load (used to generate exactly matching assembly references).
    /// </summary>
    public static HookGenResult Generate(string assemblyPath, string runtimeDetourPath)
    {
        if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Proxy assembly not found", assemblyPath);
        if (!File.Exists(runtimeDetourPath)) throw new FileNotFoundException("RuntimeDetour assembly not found", runtimeDetourPath);

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(assemblyPath)));
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { AssemblyResolver = resolver });

        using var detourAssembly = AssemblyDefinition.ReadAssembly(Path.GetFullPath(runtimeDetourPath));
        var detourRef = new AssemblyNameReference(detourAssembly.Name.Name, detourAssembly.Name.Version);

        var generator = new Generator(assembly.MainModule, detourRef, Path.GetFullPath(runtimeDetourPath));
        var result = generator.Run();
        result.OutputPath = assemblyPath;

        var temp = assemblyPath + ".hookgen.tmp";
        assembly.Write(temp);
        assembly.Dispose();
        File.Copy(temp, assemblyPath, overwrite: true);
        File.Delete(temp);
        return result;
    }

    private sealed class Generator
    {
        private readonly ModuleDefinition _module;
        private readonly AssemblyNameReference _corlib;
        private readonly AssemblyNameReference _detourRef;

        private readonly TypeReference _objectType;
        private readonly TypeReference _delegateType;
        private readonly TypeReference _multicastDelegateType;
        private readonly TypeReference _intPtrType;
        private readonly TypeReference _voidType;
        private readonly TypeReference _stringType;
        private readonly TypeReference _typeType;
        private readonly TypeReference _runtimeTypeHandleType;
        private readonly TypeReference _endpointType;

        private readonly MethodReference _getTypeFromHandle;
        private readonly MethodReference _bindOnce;
        private readonly MethodReference _bindWithArgs;
        private readonly MethodReference _add;
        private readonly MethodReference _remove;

        private int _hookCount;
        private int _typeCount;

        internal Generator(ModuleDefinition module, AssemblyNameReference detourRef, string runtimeDetourPath)
        {
            _module = module;
            _detourRef = detourRef;

            // The proxy's own corlib reference (Il2CppInterop output targets System.Runtime for the
            // running runtime; at load time it is unified to the real BCL and matches proxy method signatures).
            _corlib = module.AssemblyReferences.FirstOrDefault(r => r.Name == "System.Runtime")
                      ?? module.AssemblyReferences.First(r => r.Name == "mscorlib" || r.Name == "netstandard");

            // System types MUST use the target module's own corlib reference:
            //   · value/special types (Void/Object/IntPtr/String...) come from module.TypeSystem -
            //     bound to the module's corlib (System.Runtime), so MetadataType is correct
            //     (a hand-built TypeReference defaults MetadataType to Class and would emit Void as
            //     ELEMENT_TYPE_CLASS+TypeRef instead of ELEMENT_TYPE_VOID -> CLR "value type mismatch").
            //   · NEVER ImportReference(typeof(...)): that binds to HookGen's own running
            //     System.Private.CoreLib; written into the target assembly it yields Roslyn CS1545
            //     ("event not supported by language") because the event's delegate base class lives in an unreferenced CoreLib.
            _objectType = _module.TypeSystem.Object;
            _intPtrType = _module.TypeSystem.IntPtr;
            _voidType = _module.TypeSystem.Void;
            _stringType = _module.TypeSystem.String;

            // class types: hand-built TypeRef + the module's corlib scope (a class's MetadataType=Class is inherently correct)
            var corlib = _module.TypeSystem.CoreLibrary as AssemblyNameReference
                         ?? _module.AssemblyReferences.First(r => r.Name is "System.Runtime" or "mscorlib" or "netstandard");
            _delegateType = new TypeReference("System", "Delegate", _module, corlib);
            _multicastDelegateType = new TypeReference("System", "MulticastDelegate", _module, corlib);
            _typeType = new TypeReference("System", "Type", _module, corlib);
            _runtimeTypeHandleType = new TypeReference("System", "RuntimeTypeHandle", _module, corlib)
            {
                IsValueType = true
            };

            if (!module.AssemblyReferences.Any(r => r.Name == _detourRef.Name && r.Version == _detourRef.Version))
                module.AssemblyReferences.Add(_detourRef);

            // HookEndpoint and its methods: imported from the real RuntimeDetour assembly -
            // hand-written TypeReference/MethodReference scopes and signatures are easy to mismatch,
            // and would make the JIT fail with "Could not load type X from assembly" (scope landing on self).
            var detourAssemblyResolved = AssemblyDefinition.ReadAssembly(Path.GetFullPath(runtimeDetourPath));
            var hookEndpointDef = detourAssemblyResolved.MainModule.Types
                .FirstOrDefault(t => t.FullName == "Il2CppInterop.RuntimeDetour.HookEndpoint");
            if (hookEndpointDef == null)
                throw new InvalidOperationException("RuntimeDetour does not contain HookEndpoint");
            _endpointType = _module.ImportReference(hookEndpointDef);
            _bindOnce = _module.ImportReference(hookEndpointDef.Methods.First(m => m.Name == "BindOnce"));
            _bindWithArgs = _module.ImportReference(
                hookEndpointDef.Methods.First(m => m.Name == "BindOnce" && m.Parameters.Count == 7));
            _add = _module.ImportReference(hookEndpointDef.Methods.First(m => m.Name == "Add"));
            _remove = _module.ImportReference(hookEndpointDef.Methods.First(m => m.Name == "Remove"));

            _getTypeFromHandle = new MethodReference("GetTypeFromHandle", _typeType, _typeType) { HasThis = false };
            _getTypeFromHandle.Parameters.Add(new ParameterDefinition(_runtimeTypeHandleType));

        }

        private TypeReference Sys(string name) => new TypeReference("System", name, _module, _corlib);

        internal HookGenResult Run()
        {
            ClearPrevious();
            DeduplicateAssemblyReferences();

            var roots = new List<TypeNode>();
            foreach (var type in _module.Types)
            {
                if (type.Name == "<Module>") continue;
                var node = BuildType(type);
                if (node != null) roots.Add(node);
            }

            foreach (var node in roots.OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                EmitTopLevel(node);
            }

            // Marker: the next hookgen run clears the whole On.* namespace first, keeping runs repeatable
            _module.Types.Add(new TypeDefinition("On", "__PylonHookGen",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                _objectType));

            return new HookGenResult { Types = _typeCount, Hooks = _hookCount };
        }

        /// <summary>
        ///     Cecil import can add the same assembly reference twice (e.g. once from
        ///     module.AssemblyReferences.Add and once from importing a type whose scope is the
        ///     same assembly); collapse duplicates so the emitted metadata is clean.
        /// </summary>
        private void DeduplicateAssemblyReferences()
        {
            var distinct = _module.AssemblyReferences
                .GroupBy(reference => reference.FullName)
                .Select(group => group.First())
                .ToList();

            _module.AssemblyReferences.Clear();
            foreach (var reference in distinct) _module.AssemblyReferences.Add(reference);
        }

        /// <summary>Removes the previously generated On.* namespace on re-runs (identified via the marker type).</summary>
        private void ClearPrevious()
        {
            var marker = _module.Types.FirstOrDefault(t => t.FullName == "On.__PylonHookGen");
            if (marker == null) return;

            foreach (var type in _module.Types
                         .Where(t => t != marker && (t.Namespace == "On" || t.Namespace.StartsWith("On.", StringComparison.Ordinal)))
                         .ToList())
            {
                _module.Types.Remove(type);
            }
            _module.Types.Remove(marker);
        }

        // ---------------- Building the tree ----------------

        private TypeNode? BuildType(TypeDefinition type)
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
            foreach (var method in type.Methods)
            {
                if (!IsHookable(method)) continue;
                var eventName = UniqueName(Escape(method.Name), used);
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
            if (!type.IsClass) return false;
            if (type.IsValueType || type.IsEnum || type.IsInterface) return false;
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
            if (method.IsConstructor) return false;
            if (method.IsSpecialName) return false;
            if (method.IsAbstract) return false;
            if (!IsValidIdentifier(method.Name)) return false;
            if (method.ReturnType is ByReferenceType) return false;
            if (!IsRepresentable(method.ReturnType)) return false;
            foreach (var parameter in method.Parameters)
                if (!IsRepresentable(parameter.ParameterType)) return false;
            return HasMethodInfoField(method);
        }

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

        // ---------------- Emitting IL ----------------

        private void EmitTopLevel(TypeNode node)
        {
            var ns = string.IsNullOrEmpty(node.Definition.Namespace) ? "On" : "On." + node.Definition.Namespace;
            var type = new TypeDefinition(ns, ArityTypeName(node.Name, node),
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                _objectType);
            var typeArgs = BindGenericParams(type, node);
            _module.Types.Add(type);
            _typeCount++;
            EmitMembers(type, node, node.Definition, typeArgs);
        }

        private void EmitNested(TypeDefinition parent, TypeNode node, TypeDefinition gameType)
        {
            var type = new TypeDefinition(string.Empty, ArityTypeName(node.Name, node),
                TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                _objectType);
            var typeArgs = BindGenericParams(type, node);
            parent.NestedTypes.Add(type);
            _typeCount++;
            EmitMembers(type, node, gameType, typeArgs);
        }

        /// <summary>Binds the host class's generic parameters (owner = host TypeDefinition; positions are auto-assigned).</summary>
        private static List<GenericParameter>? BindGenericParams(TypeDefinition owner, TypeNode node)
        {
            if (!node.IsGenericType) return null;
            var result = new List<GenericParameter>();
            foreach (var name in node.TypeParameterNames!)
            {
                var gp = new GenericParameter(name, owner);
                owner.GenericParameters.Add(gp);
                result.Add(gp);
            }
            return result;
        }

        /// <summary>Generic types use a CIL name with arity (X`1 form; C# source names omit it).</summary>
        private static string ArityTypeName(string name, TypeNode node)
        {
            if (!node.IsGenericType) return name;
            return name + "\u0060" + node.TypeParameterNames!.Count;
        }

        private void EmitMembers(TypeDefinition type, TypeNode node, TypeDefinition gameType) =>
            EmitMembers(type, node, gameType, null);

        private void EmitMembers(TypeDefinition type, TypeNode node, TypeDefinition gameType,
                                 List<GenericParameter>? typeArgs)
        {
            foreach (var method in node.Methods) EmitHook(type, gameType, method, typeArgs);
            foreach (var child in node.Children)
            {
                // only top-level game types get an On class; nested types expand into nested classes
                var childGameType = gameType.NestedTypes.First(t => t.Name == child.Definition.Name);
                EmitNested(type, child, childGameType);
            }
        }

        private void EmitHook(TypeDefinition owner, TypeDefinition gameType, MethodNode node,
                              List<GenericParameter>? typeArgs)
        {
            var method = node.Definition;

            // generic methods: emit a generic host class holding the event (MonoMod style); delegates carry method-level generic parameters
            if (node.IsGenericMethod)
            {
                EmitGenericMethodHook(owner, gameType, node);
                return;
            }

            // orig: the full managed signature of the target (self + parameters), parameter names copied from the proxy method;
            // methods on generic types: self and !!0/!0 references in parameters use their own generic parameters
            var usedNames = new HashSet<string>(StringComparer.Ordinal) { "orig", "self" };
            var origParams = new List<(TypeReference Type, string Name)>();
            if (!method.IsStatic) origParams.Add((SelfType(gameType, typeArgs), "self"));
            foreach (var parameter in method.Parameters)
            {
                origParams.Add((MapTypeArgs(parameter.ParameterType, typeArgs), ParameterName(parameter, usedNames)));
            }
            var origDelegate = DefineDelegate(owner, node.OrigName, MapTypeArgs(method.ReturnType, typeArgs), origParams, typeArgs);

            // hook: (orig, self?, parameters...)
            var hookParams = new List<(TypeReference Type, string Name)> { (origDelegate, "orig") };
            hookParams.AddRange(origParams);
            var hookDelegate = DefineDelegate(owner, node.HookName, MapTypeArgs(method.ReturnType, typeArgs), hookParams, typeArgs);

            var field = new FieldDefinition("__pylon_ep_" + node.Index,
                FieldAttributes.Private | FieldAttributes.Static, _endpointType);
            owner.Fields.Add(field);

            var add = new MethodDefinition("add_" + node.EventName,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                _voidType);
            add.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, hookDelegate));

            var remove = new MethodDefinition("remove_" + node.EventName,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                _voidType);
            remove.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, hookDelegate));

            EmitAdd(add, field, gameType, method, origDelegate, hookDelegate, typeArgs);
            EmitRemove(remove, field);

            owner.Methods.Add(add);
            owner.Methods.Add(remove);
            owner.Events.Add(new EventDefinition(node.EventName, EventAttributes.None, hookDelegate)
            {
                AddMethod = add,
                RemoveMethod = remove
            });

            _hookCount++;
        }

        /// <summary>For methods on generic types, self is the closed generic instance On.&lt;X&gt;&lt;!0&gt;...; ordinary types are returned as-is.</summary>
        private TypeReference SelfType(TypeDefinition gameType, List<GenericParameter>? typeArgs)
        {
            if (typeArgs == null || typeArgs.Count == 0) return gameType;
            var gi = new GenericInstanceType(gameType);
            foreach (var p in typeArgs) gi.GenericArguments.Add(p);
            return gi;
        }

        private static TypeReference MakeGenericInstance(TypeReference element, IEnumerable<TypeReference> args)
        {
            var gi = new GenericInstanceType(element);
            foreach (var a in args) gi.GenericArguments.Add(a);
            return gi;
        }

        /// <summary>Maps generic parameters (!!0/!0) in a signature to the host class's generic parameters; everything else is unchanged.</summary>
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

        /// <summary>Generic methods: an On.X.&lt;methodName&gt;&lt;T&gt; host class, with delegates/events carrying method-level generic parameters.</summary>
        private void EmitGenericMethodHook(TypeDefinition owner, TypeDefinition gameType, MethodNode node)
        {
            var method = node.Definition;

            // host class: method name + backtick arity; method-level generic parameters become host-class generic parameters
            var host = new TypeDefinition(string.Empty, node.EventName + "\u0060" + node.TypeParameterNames!.Count,
                TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                _objectType);
            var args = new List<GenericParameter>(); // node.TypeParameters replaced below
            foreach (var name in node.TypeParameterNames!)
            {
                var gp = new GenericParameter(name, host);
                host.GenericParameters.Add(gp);
                args.Add(gp);
            }
            owner.NestedTypes.Add(host);
            _typeCount++;

            // method-level generic parameters are host-class type-level !i; !!i in the method signature maps to the same-named host parameter
            var usedNames = new HashSet<string>(StringComparer.Ordinal) { "orig", "self" };
            var origParams = new List<(TypeReference Type, string Name)>();
            if (!method.IsStatic) origParams.Add((gameType, "self"));
            foreach (var parameter in method.Parameters)
            {
                origParams.Add((MapMethodGeneric(parameter.ParameterType, args), ParameterName(parameter, usedNames)));
            }
            var origDelegate = DefineDelegate(host, node.OrigName, MapMethodGeneric(method.ReturnType, args), origParams, args);
            var hookParams = new List<(TypeReference Type, string Name)> { (origDelegate, "orig") };
            hookParams.AddRange(origParams);
            var hookDelegate = DefineDelegate(host, node.HookName, MapMethodGeneric(method.ReturnType, args), hookParams, args);

            var field = new FieldDefinition("__pylon_ep_" + node.Index,
                FieldAttributes.Private | FieldAttributes.Static, _endpointType);
            host.Fields.Add(field);

            var add = new MethodDefinition("add_" + node.EventName,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                _voidType);
            add.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, hookDelegate));
            var remove = new MethodDefinition("remove_" + node.EventName,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                _voidType);
            remove.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, hookDelegate));

            EmitAddGeneric(add, field, gameType, method, origDelegate, hookDelegate, args);
            EmitRemove(remove, field);

            host.Methods.Add(add);
            host.Methods.Add(remove);
            host.Events.Add(new EventDefinition(node.EventName, EventAttributes.None, hookDelegate)
            {
                AddMethod = add,
                RemoveMethod = remove
            });
            _hookCount++;
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



        private void EmitAdd(MethodDefinition method, FieldDefinition field, TypeDefinition gameType,
                             MethodDefinition target, TypeDefinition origDelegate, TypeDefinition hookDelegate,
                             List<GenericParameter>? typeArgs)
        {
            var il = method.Body.GetILProcessor();
            method.Body.InitLocals = false;

            il.Emit(OpCodes.Ldsflda, field);

            // declaring type: for generic types, pass the open definition (ldtoken yields X<>); typeArgs come from the host generic parameters;
            // HookEndpoint.Bind applies MakeGenericType(typeArgs).
            il.Emit(OpCodes.Ldtoken, gameType);
            il.Emit(OpCodes.Call, _getTypeFromHandle);

            // method name
            il.Emit(OpCodes.Ldstr, target.Name);

            // parameter types
            il.Emit(OpCodes.Ldc_I4, target.Parameters.Count);
            il.Emit(OpCodes.Newarr, _typeType);
            for (var i = 0; i < target.Parameters.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                EmitTypeOf(il, MapTypeArgs(target.Parameters[i].ParameterType, typeArgs));
                il.Emit(OpCodes.Stelem_Ref);
            }

            // type args array (generic types/generic methods); absent for non-generic
            if (typeArgs != null && typeArgs.Count > 0)
            {
                il.Emit(OpCodes.Ldc_I4, typeArgs.Count);
                il.Emit(OpCodes.Newarr, _typeType);
                for (var i = 0; i < typeArgs.Count; i++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);
                    EmitTypeOf(il, typeArgs[i]);
                    il.Emit(OpCodes.Stelem_Ref);
                }
            }

            EmitTypeOf(il, origDelegate);
            EmitTypeOf(il, hookDelegate);

            if (typeArgs != null && typeArgs.Count > 0)
            {
                il.Emit(OpCodes.Call, _bindWithArgs);   // 7-param BindOnce (incl. ref field + typeArgs)
            }
            else
            {
                il.Emit(OpCodes.Call, _bindOnce);       // 6-param BindOnce (incl. ref field)
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _add);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>Generic method hosts: self is the declaring type (generic methods usually live on non-generic types).</summary>
        private void EmitAddGeneric(MethodDefinition method, FieldDefinition field, TypeDefinition gameType,
                                    MethodDefinition target, TypeDefinition origDelegate, TypeDefinition hookDelegate,
                                    List<GenericParameter> args)
        {
            var il = method.Body.GetILProcessor();
            method.Body.InitLocals = false;

            il.Emit(OpCodes.Ldsflda, field);

            il.Emit(OpCodes.Ldtoken, gameType);
            il.Emit(OpCodes.Call, _getTypeFromHandle);

            il.Emit(OpCodes.Ldstr, target.Name);

            il.Emit(OpCodes.Ldc_I4, target.Parameters.Count);
            il.Emit(OpCodes.Newarr, _typeType);
            for (var i = 0; i < target.Parameters.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                EmitTypeOf(il, MapMethodGeneric(target.Parameters[i].ParameterType, args));
                il.Emit(OpCodes.Stelem_Ref);
            }

            // type args = method-level generic parameters
            il.Emit(OpCodes.Ldc_I4, args.Count);
            il.Emit(OpCodes.Newarr, _typeType);
            for (var i = 0; i < args.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                EmitTypeOf(il, args[i]);
                il.Emit(OpCodes.Stelem_Ref);
            }

            EmitTypeOf(il, origDelegate);
            EmitTypeOf(il, hookDelegate);

            il.Emit(OpCodes.Call, _bindWithArgs);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _add);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>Pushes typeof(T) on the stack: ldtoken straight to GetTypeFromHandle; generic instances use GenericInstanceType.</summary>
        private void EmitTypeOf(ILProcessor il, TypeReference type)
        {
            il.Emit(OpCodes.Ldtoken, type);
            il.Emit(OpCodes.Call, _getTypeFromHandle);
        }

        private void EmitRemove(MethodDefinition method, FieldDefinition field)
        {
            var il = method.Body.GetILProcessor();
            method.Body.InitLocals = false;

            var end = il.Create(OpCodes.Ret);
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Brfalse, end);
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _remove);
            il.Append(end);
        }

        /// <summary>Generic parameters keep their original reference (ImportReference would detach them); other types are imported into the module.</summary>
        private TypeReference ImportOrSelf(TypeReference type) =>
            type is GenericParameter ? type : _module.ImportReference(type);

        private TypeDefinition DefineDelegate(TypeDefinition owner, string name, TypeReference returnType,
                                             IList<(TypeReference Type, string Name)> parameters) =>
            DefineDelegate(owner, name, returnType, parameters, null);

        /// <summary>
        ///     Delegates of a generic host must be nested generic delegates (hook_XXX&lt;T&gt;): Invoke references the delegate's own generic parameters,
        ///     instead of the outer host's !0 (which produces Roslyn CS0570 "not supported by language").
        /// </summary>
        private TypeDefinition DefineDelegate(TypeDefinition owner, string name, TypeReference returnType,
                                             IList<(TypeReference Type, string Name)> parameters,
                                             List<GenericParameter>? hostGenerics)
        {
            var type = new TypeDefinition(string.Empty, name,
                TypeAttributes.NestedPublic | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                _multicastDelegateType);
            var selfGenerics = new List<GenericParameter>();
            if (hostGenerics != null)
                foreach (var p in hostGenerics)
                {
                    var gp = new GenericParameter(p.Name, type);
                    type.GenericParameters.Add(gp);
                    selfGenerics.Add(gp);
                }
            owner.NestedTypes.Add(type);

            var ctor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                _voidType)
            {
                ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed
            };
            ctor.Parameters.Add(new ParameterDefinition("object", ParameterAttributes.None, _objectType));
            ctor.Parameters.Add(new ParameterDefinition("method", ParameterAttributes.None, _intPtrType));
            type.Methods.Add(ctor);

            var invoke = new MethodDefinition("Invoke",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                RemapToSelf(returnType, hostGenerics, selfGenerics))
            {
                ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed
            };
            for (var i = 0; i < parameters.Count; i++)
            {
                invoke.Parameters.Add(new ParameterDefinition(parameters[i].Name, ParameterAttributes.None,
                    RemapToSelf(parameters[i].Type, hostGenerics, selfGenerics)));
            }
            type.Methods.Add(invoke);
            return type;
        }

        /// <summary>Replaces host generic parameter references (!0) in a signature with the delegate's own (same position).</summary>
        private static TypeReference RemapToSelf(TypeReference type, List<GenericParameter>? hostGenerics,
                                                 List<GenericParameter> selfGenerics)
        {
            if (hostGenerics == null || hostGenerics.Count == 0) return type;
            switch (type)
            {
                case GenericParameter gp:
                    var i = hostGenerics.IndexOf(gp);
                    return i >= 0 && i < selfGenerics.Count ? selfGenerics[i] : gp;
                case GenericInstanceType gi:
                    {
                        var mapped = new GenericInstanceType(gi.ElementType);
                        foreach (var a in gi.GenericArguments) mapped.GenericArguments.Add(RemapToSelf(a, hostGenerics, selfGenerics));
                        return mapped;
                    }
                default:
                    return type;
            }
        }

        // ---------------- Naming ----------------

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
    }
}
