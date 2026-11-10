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
// Split across partial files: this file (facade + generator shell), OnHookGenerator.Model.cs
// (candidate model + filter rules), OnHookGenerator.Emit.cs (IL emission), OnHookGenerator.Naming.cs
// (naming, escaping and generic-parameter mapping).
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

public static partial class OnHookGenerator
{
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
        // scope 必须使用模块表里的同一个 AssemblyRef 对象;未入表的新对象没有 RID,
        // 写出的 ResolutionScope 会变成 0,CLR 就会错误地在代理自身寻找运行时类型。
        var detourRef = assembly.MainModule.AssemblyReferences.FirstOrDefault(r => r.FullName == detourAssembly.Name.FullName)
                        ?? new AssemblyNameReference(detourAssembly.Name.Name, detourAssembly.Name.Version);
        if (!assembly.MainModule.AssemblyReferences.Contains(detourRef))
            assembly.MainModule.AssemblyReferences.Add(detourRef);
        foreach (var type in assembly.MainModule.GetTypeReferences())
            if (type.Namespace == "Il2CppInterop.RuntimeDetour") type.Scope = detourRef;

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

    private sealed partial class Generator
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
        private readonly MethodReference _bindCompact;
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
            // Cecil 可能把导入类型的 scope 落成 ModuleDefinition（运行时就会去代理程序集里找
            // HookEndpoint → TypeLoadException）。显式绑到 RuntimeDetour 的 AssemblyRef。
            _endpointType.Scope = _detourRef;
            _bindOnce = _module.ImportReference(hookEndpointDef.Methods.First(m => m.Name == "BindOnce"));
            _bindCompact = _module.ImportReference(hookEndpointDef.Methods.First(
                m => m.Name == "BindOnce" && m.Parameters.Count == 6 && m.Parameters[3].ParameterType.Name == "Int32"));
            _bindWithArgs = _module.ImportReference(
                hookEndpointDef.Methods.First(m => m.Name == "BindOnce" && m.Parameters.Count == 7));
            _add = _module.ImportReference(hookEndpointDef.Methods.First(m => m.Name == "Add"));
            _remove = _module.ImportReference(hookEndpointDef.Methods.First(m => m.Name == "Remove"));

            foreach (var method in new[] { _bindOnce, _bindCompact, _bindWithArgs, _add, _remove })
            {
                if (method.DeclaringType is not null)
                {
                    method.DeclaringType.Scope = _detourRef;
                    FixDetourScope(method.DeclaringType);
                }
                FixDetourScope(method.ReturnType);
                foreach (var parameter in method.Parameters) FixDetourScope(parameter.ParameterType);
            }

            _getTypeFromHandle = new MethodReference("GetTypeFromHandle", _typeType, _typeType) { HasThis = false };
            _getTypeFromHandle.Parameters.Add(new ParameterDefinition(_runtimeTypeHandleType));
        }

        private TypeReference Sys(string name) => new TypeReference("System", name, _module, _corlib);

        /// <summary>Fix the scope of any type reference that belongs to the RuntimeDetour assembly
        /// (including byref/array/generic wrappers) so it resolves via the AssemblyRef, not the proxy module.</summary>
        private void FixDetourScope(TypeReference type)
        {
            switch (type)
            {
                case ByReferenceType byRef:
                    FixDetourScope(byRef.ElementType);
                    return;
                case PointerType pointer:
                    FixDetourScope(pointer.ElementType);
                    return;
                case ArrayType array:
                    FixDetourScope(array.ElementType);
                    return;
                case GenericInstanceType generic:
                    FixDetourScope(generic.ElementType);
                    foreach (var argument in generic.GenericArguments) FixDetourScope(argument);
                    return;
            }

            if (type.Namespace == "Il2CppInterop.RuntimeDetour" && type.Scope != _detourRef)
                type.Scope = _detourRef;
        }

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
    }
}
