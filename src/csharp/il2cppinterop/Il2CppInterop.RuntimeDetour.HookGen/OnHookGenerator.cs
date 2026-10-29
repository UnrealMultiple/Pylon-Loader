// 层：HookGen —— 把 On.* 事件直接以 IL 写进 Il2CppInterop 生成的代理程序集。
//
// 形态（与 MonoMod HookGen 一致，但发的是 IL 而不是 C# 源码）：
//   namespace On.Terraria { public static class Main {
//       public delegate void orig_Update(Terraria.Main self, GameTime gameTime);
//       public delegate void hook_Update(orig_Update orig, Terraria.Main self, GameTime gameTime);
//       public static event hook_Update Update { add { ... } remove { ... } }
//   } }
//
// 事件访问器只做两件事：首次订阅时 HookEndpoint.BindOnce(...) 创建端点，然后 Add/Remove。
// 目标方法用 typeof + 名称 + 参数类型数组描述（不看字符串拼签名），运行时端点按精确签名解析。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Il2CppInterop.RuntimeDetour.HookGen
{
    public sealed class HookGenResult
    {
        public int Types;
        public int Hooks;
        public string OutputPath;
    }

    public static class OnHookGenerator
    {
        private sealed class MethodNode
        {
            public MethodDefinition Definition;
            public string EventName;
            public string OrigName => "orig_" + EventName;
            public string HookName => "hook_" + EventName;
            public int Index;

            /// <summary>方法级泛型参数名（为空表示普通方法）。</summary>
            public List<string> TypeParameterNames;
            public bool IsGenericMethod => TypeParameterNames != null && TypeParameterNames.Count > 0;
        }

        private sealed class TypeNode
        {
            public TypeDefinition Definition;
            public string Name;
            public readonly List<MethodNode> Methods = new();
            public readonly List<TypeNode> Children = new();

            /// <summary>类型级泛型参数名（为空表示普通类型）。</summary>
            public List<string> TypeParameterNames;
            public bool IsGenericType => TypeParameterNames != null && TypeParameterNames.Count > 0;
        }

        /// <summary>
        /// 就地给代理程序集补上 On.*；返回统计。runtimeDetourPath 是本次部署会加载的
        /// Il2CppInterop.RuntimeDetour.dll（用来生成与之完全一致的程序集引用）。
        /// </summary>
        public static HookGenResult Generate(string assemblyPath, string runtimeDetourPath)
        {
            if (!File.Exists(assemblyPath)) throw new FileNotFoundException("找不到代理程序集", assemblyPath);
            if (!File.Exists(runtimeDetourPath)) throw new FileNotFoundException("找不到 RuntimeDetour", runtimeDetourPath);

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

                // 代理程序集自己的 corlib 引用（Il2CppInterop 生成物是 System.Runtime 6.x；
                // 运行时统一到实际 BCL，和代理方法签名用的是同一套）。
                _corlib = module.AssemblyReferences.FirstOrDefault(r => r.Name == "System.Runtime")
                          ?? module.AssemblyReferences.First(r => r.Name == "mscorlib" || r.Name == "netstandard");

                // ⚠ 系统类型必须用【目标模块自己的 corlib 引用】：
                //   · 值/特殊类型（Void/Object/IntPtr/String…）用 module.TypeSystem ——
                //     它绑定到模块引用的 corlib（游戏代理是 System.Runtime v6），MetadataType 正确
                //     （手工 new TypeReference 的 MetadataType 是 Class，Void 会被写成
                //      ELEMENT_TYPE_CLASS+TypeRef 而不是 ELEMENT_TYPE_VOID → CLR "value type mismatch"）。
                //   · 绝对不要 ImportReference(typeof(...))：那绑定的是 HookGen 自己运行时的
                //     System.Private.CoreLib v10，写进目标程序集后 Roslyn 报 CS1545
                //     （"事件不受语言支持"），因为事件的委托基类在未引用的 CoreLib 里。
                _objectType = _module.TypeSystem.Object;
                _intPtrType = _module.TypeSystem.IntPtr;
                _voidType = _module.TypeSystem.Void;
                _stringType = _module.TypeSystem.String;

                // class 类型：手工 TypeRef + 模块 corlib 作用域（class 的 MetadataType=Class 本来就对）
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

                // HookEndpoint 及其方法：从真实 RuntimeDetour 程序集 import ——
                // 手写 TypeReference/MethodReference 的 scope 与签名极易错配，
                // 写进代理后 JIT 报 "Could not load type X from assembly"（scope 落成 self）。
                var detourAssemblyResolved = AssemblyDefinition.ReadAssembly(Path.GetFullPath(runtimeDetourPath));
                var hookEndpointDef = detourAssemblyResolved.MainModule.Types
                    .FirstOrDefault(t => t.FullName == "Il2CppInterop.RuntimeDetour.HookEndpoint");
                if (hookEndpointDef == null)
                    throw new InvalidOperationException("RuntimeDetour 里没有 HookEndpoint");
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

                // 标记：下一次 hookgen 先清掉整片 On.*，保证可重复执行
                _module.Types.Add(new TypeDefinition("On", "__PylonHookGen",
                    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    _objectType));

                return new HookGenResult { Types = _typeCount, Hooks = _hookCount };
            }

            /// <summary>重跑 hookgen 时先摘掉上一次生成的整片 On.*（靠标记类型识别）。</summary>
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

            // ---------------- 建树 ----------------

            private TypeNode BuildType(TypeDefinition type)
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
                        // 泛型方法：事件宿主类用"方法名+"泛型参数"；方法级 !!i 在宿主类里降级为类级 !i
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

            /// <summary>去掉泛型 arity 后缀（List`1 → List）与编译器生成名。</summary>
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
                        // 普通方法：NativeMethodInfoPtr_*；
                        // 泛型方法：DeclaringType 是 MethodInfoStoreGeneric_*、字段名 Pointer。
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
                    case GenericParameter: return true;   // 泛型参数：兼容（引用声明类型/方法级泛型参数）
                    case GenericInstanceType generic:
                        {
                            // 闭合泛型实例（Il2CppSystem.Nullable<Color> 等装箱值类型）允许生成：
                            // 引擎（HookPlan.NativeSlot + MirrorTypes）在挂载时按 il2cpp 值大小桥接。
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

            // ---------------- 发 IL ----------------

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

            /// <summary>绑定宿主类泛型参数（owner = 宿主类 TypeDefinition，Position 自动分配）。</summary>
            private static List<GenericParameter> BindGenericParams(TypeDefinition owner, TypeNode node)
            {
                if (!node.IsGenericType) return null;
                var result = new List<GenericParameter>();
                foreach (var name in node.TypeParameterNames)
                {
                    var gp = new GenericParameter(name, owner);
                    owner.GenericParameters.Add(gp);
                    result.Add(gp);
                }
                return result;
            }

            /// <summary>泛型类型的 CIL 名带 arity（X`1 形式；C# 源码名不带）。</summary>
            private static string ArityTypeName(string name, TypeNode node)
            {
                if (!node.IsGenericType) return name;
                return name + "\u0060" + node.TypeParameterNames.Count;
            }

            private void EmitMembers(TypeDefinition type, TypeNode node, TypeDefinition gameType) =>
                EmitMembers(type, node, gameType, null);

            private void EmitMembers(TypeDefinition type, TypeNode node, TypeDefinition gameType,
                                     List<GenericParameter> typeArgs)
            {
                foreach (var method in node.Methods) EmitHook(type, gameType, method, typeArgs);
                foreach (var child in node.Children)
                {
                    // 只给"顶层游戏类型"建 On 类；嵌套类型按嵌套类展开
                    var childGameType = gameType.NestedTypes.First(t => t.Name == child.Definition.Name);
                    EmitNested(type, child, childGameType);
                }
            }

            private void EmitHook(TypeDefinition owner, TypeDefinition gameType, MethodNode node,
                                  List<GenericParameter> typeArgs)
            {
                var method = node.Definition;

                // 泛型方法：生成宿主泛型类，事件放里面（MonoMod 写法），委托带方法级泛型参数
                if (node.IsGenericMethod)
                {
                    EmitGenericMethodHook(owner, gameType, node);
                    return;
                }

                // orig：目标方法的完整托管签名（self + 参数），参数名沿用代理方法的原名；
                // 泛型类型上的方法：self 与参数里的 !!0/!0 引用用自己的泛型参数
                var usedNames = new HashSet<string>(StringComparer.Ordinal) { "orig", "self" };
                var origParams = new List<(TypeReference Type, string Name)>();
                if (!method.IsStatic) origParams.Add((SelfType(gameType, typeArgs), "self"));
                foreach (var parameter in method.Parameters)
                {
                    origParams.Add((MapTypeArgs(parameter.ParameterType, typeArgs), ParameterName(parameter, usedNames)));
                }
                var origDelegate = DefineDelegate(owner, node.OrigName, MapTypeArgs(method.ReturnType, typeArgs), origParams, typeArgs);

                // hook：(orig, self?, 参数…)
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

            /// <summary>泛型类型上的方法：self 用游戏类型的闭合泛型实例 On.<X>&lt;!0&gt;…；普通类型原样。</summary>
            private TypeReference SelfType(TypeDefinition gameType, List<GenericParameter> typeArgs)
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

            /// <summary>把方法签名里的泛型参数（!!0/!0）映射为宿主类的泛型参数，其它原样。</summary>
            private static TypeReference MapTypeArgs(TypeReference type, List<GenericParameter> typeArgs)
            {
                switch (type)
                {
                    case GenericParameter gp:
                        // 方法级泛型参数 !!i 需要提升为宿主类泛型参数 ?i：用 typeArgs 里同名替代
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

            /// <summary>泛型方法：On.X.<方法名>&lt;T&gt; 宿主类，委托/事件都带方法级泛型参数。</summary>
            private void EmitGenericMethodHook(TypeDefinition owner, TypeDefinition gameType, MethodNode node)
            {
                var method = node.Definition;

                // 宿主类：方法名 + 反引号 arity；方法级泛型参数 → 宿主类泛型参数
                var host = new TypeDefinition(string.Empty, node.EventName + "\u0060" + node.TypeParameterNames.Count,
                    TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    _objectType);
                var args = new List<GenericParameter>(); // node.TypeParameters replaced below
                foreach (var name in node.TypeParameterNames)
                {
                    var gp = new GenericParameter(name, host);
                    host.GenericParameters.Add(gp);
                    args.Add(gp);
                }
                owner.NestedTypes.Add(host);
                _typeCount++;

                // 方法级泛型参数在宿主类里是 !i（类型级），方法签名里的 !!i → 映射成宿主类同名参数
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

            /// <summary>方法级泛型参数 !!i → 宿主类泛型参数 !i（同位置替换）。</summary>
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

            /// <summary>泛型方法宿主类生成：把方法级 !!i 映射为宿主类 !i 的参数表辅助（同 MapMethodGeneric）。</summary>

            private void EmitAdd(MethodDefinition method, FieldDefinition field, TypeDefinition gameType,
                                 MethodDefinition target, TypeDefinition origDelegate, TypeDefinition hookDelegate,
                                 List<GenericParameter> typeArgs)
            {
                var il = method.Body.GetILProcessor();
                method.Body.InitLocals = false;

                il.Emit(OpCodes.Ldsflda, field);

                // declaring type：泛型类型传开放定义（ldtoken 得 X<>），typeArgs 由宿主泛型参数生成；
                // HookEndpoint.Bind 会做 MakeGenericType(typeArgs)。
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

                // type args array（泛型类型/泛型方法）；非泛型没有这个参数
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
                    il.Emit(OpCodes.Call, _bindWithArgs);   // 7 参 BindOnce（含 ref field + typeArgs）
                }
                else
                {
                    il.Emit(OpCodes.Call, _bindOnce);       // 6 参 BindOnce（含 ref field）
                }
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, _add);
                il.Emit(OpCodes.Ret);
            }

            /// <summary>泛型方法宿主：self = 方法所属类型（泛型方法多在非泛型类型上）。</summary>
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

                // type args = 方法级泛型参数
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

            /// <summary>栈上放一个 typeof(T)：能 ldtoken 直接给 GetTypeFromHandle；泛型实例用 GenericInstanceType。</summary>
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

            /// <summary>泛型参数保持原引用（ImportReference 会脱绑）；其它类型导入模块。</summary>
            private TypeReference ImportOrSelf(TypeReference type) =>
                type is GenericParameter ? type : _module.ImportReference(type);

            private TypeDefinition DefineDelegate(TypeDefinition owner, string name, TypeReference returnType,
                                                 IList<(TypeReference Type, string Name)> parameters) =>
                DefineDelegate(owner, name, returnType, parameters, null);

            /// <summary>
            /// 泛型宿主的委托必须是嵌套泛型委托（hook_XXX&lt;T&gt;）：Invoke 引用委托自己的泛型参数，
            /// 而不是直接引用外层宿主的 !0（那样 Roslyn 报 CS0570 "语言不支持"）。
            /// </summary>
            private TypeDefinition DefineDelegate(TypeDefinition owner, string name, TypeReference returnType,
                                                 IList<(TypeReference Type, string Name)> parameters,
                                                 List<GenericParameter> hostGenerics)
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

            /// <summary>把签名里引用宿主泛型参数（!0）的替换成委托自己的泛型参数（同位置）。</summary>
            private static TypeReference RemapToSelf(TypeReference type, List<GenericParameter> hostGenerics,
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

            // ---------------- 命名 ----------------

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

            /// <summary>沿用代理方法的参数名；空/非法名退回 argN，重名追加 _1。</summary>
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
}