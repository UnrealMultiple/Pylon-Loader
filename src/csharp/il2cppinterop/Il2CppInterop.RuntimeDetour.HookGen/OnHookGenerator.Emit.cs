using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Il2CppInterop.RuntimeDetour.HookGen;

public static partial class OnHookGenerator
{
    private sealed partial class Generator
    {
        // ---------------- Emitting IL ----------------

        internal void EmitTopLevel(TypeNode node)
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

            // constructors: hook_ ctor(orig, self, parameters...) - orig returns void and allocates;
            // subscribers run after the original constructor body and can modify the fresh object.
            if (method.IsConstructor)
            {
                EmitConstructorHook(owner, gameType, node, typeArgs);
                return;
            }

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
            // ref T return: delegates expose the element type (the engine bridges pointer <-> value).
            var returnView = method.ReturnType is ByReferenceType byRef
                ? byRef.ElementType : method.ReturnType;
            var origDelegate = DefineDelegate(owner, node.OrigName, MapTypeArgs(returnView, typeArgs), origParams, typeArgs);

            // hook: (orig, self?, parameters...)
            var hookParams = new List<(TypeReference Type, string Name)> { (origDelegate, "orig") };
            hookParams.AddRange(origParams);
            var hookDelegate = DefineDelegate(owner, node.HookName, MapTypeArgs(returnView, typeArgs), hookParams, typeArgs);

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

            // Size optimization: unambiguous names resolve by name+arity at runtime, so the
            // accessor emits no parameter-type array at all (~90% of game methods).
            var hasOverloads = CountSameNameMethods(gameType, method) > 1;
            EmitAdd(add, field, gameType, method, origDelegate, hookDelegate, typeArgs, hasOverloads);
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

        /// <summary>
        ///     Emits the On.* event for a constructor: delegates orig_XXX(self, parameters...) /
        ///     hook_XXX(orig, self, parameters...) with a void return. The endpoint resolves the
        ///     target as ".ctor" with the constructor's parameter list.
        /// </summary>
        private void EmitConstructorHook(TypeDefinition owner, TypeDefinition gameType, MethodNode node,
                                         List<GenericParameter>? typeArgs)
        {
            var method = node.Definition;
            var usedNames = new HashSet<string>(StringComparer.Ordinal) { "orig", "self" };

            // orig: (self, parameters...), void return
            var origParams = new List<(TypeReference Type, string Name)> { (SelfType(gameType, typeArgs), "self") };
            foreach (var parameter in method.Parameters)
                origParams.Add((MapTypeArgs(parameter.ParameterType, typeArgs), ParameterName(parameter, usedNames)));
            var origDelegate = DefineDelegate(owner, node.OrigName, _voidType, origParams, typeArgs);

            // hook: (orig, self, parameters...)
            var hookParams = new List<(TypeReference Type, string Name)> { (origDelegate, "orig") };
            hookParams.AddRange(origParams);
            var hookDelegate = DefineDelegate(owner, node.HookName, _voidType, hookParams, typeArgs);

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

            // BindOnce(ref field, typeof(gameType), ".ctor", paramTypes, typeof(orig), typeof(hook))
            var il = add.Body.GetILProcessor();
            add.Body.InitLocals = false;
            il.Emit(OpCodes.Ldsflda, field);
            il.Emit(OpCodes.Ldtoken, gameType);
            il.Emit(OpCodes.Call, _getTypeFromHandle);
            il.Emit(OpCodes.Ldstr, ".ctor");
            // ctors: compact only when the type has a single instance ctor
            var ctorCount = 0;
            foreach (var c in gameType.Methods) { if (c.IsConstructor && !c.IsStatic && c.HasBody) ctorCount++; }
            if (ctorCount == 1)
            {
                il.Emit(OpCodes.Ldc_I4, method.Parameters.Count);
                EmitTypeOf(il, origDelegate);
                EmitTypeOf(il, hookDelegate);
                il.Emit(OpCodes.Call, _bindCompact);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, _add);
                il.Emit(OpCodes.Ret);
                return;
            }
            il.Emit(OpCodes.Ldc_I4, method.Parameters.Count);
            il.Emit(OpCodes.Newarr, _typeType);
            for (var i = 0; i < method.Parameters.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                EmitTypeOf(il, MapTypeArgs(method.Parameters[i].ParameterType, typeArgs));
                il.Emit(OpCodes.Stelem_Ref);
            }
            EmitTypeOf(il, origDelegate);
            EmitTypeOf(il, hookDelegate);
            il.Emit(OpCodes.Call, _bindOnce);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _add);
            il.Emit(OpCodes.Ret);

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

        /// <summary>How many hookable methods share this name (overload detection for the compact bind path).</summary>
        private static int CountSameNameMethods(TypeDefinition gameType, MethodDefinition method)
        {
            var count = 0;
            foreach (var candidate in gameType.Methods)
            {
                if (candidate.Name == method.Name && !candidate.IsConstructor) count++;
            }
            return count;
        }

        private void EmitAdd(MethodDefinition method, FieldDefinition field, TypeDefinition gameType,
                             MethodDefinition target, TypeDefinition origDelegate, TypeDefinition hookDelegate,
                             List<GenericParameter>? typeArgs, bool hasOverloads)
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

            if (typeArgs is not { Count: > 0 })
            {
                if (!hasOverloads)
                {
                    // compact: BindOnce(ref field, typeof(T), "name", paramCount, typeof(orig), typeof(hook))
                    il.Emit(OpCodes.Ldc_I4, target.Parameters.Count);
                    EmitTypeOf(il, origDelegate);
                    EmitTypeOf(il, hookDelegate);
                    il.Emit(OpCodes.Call, _bindCompact);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Callvirt, _add);
                    il.Emit(OpCodes.Ret);
                    return;
                }

                // overloaded but non-generic: still skip the element types? No - keep full array for correctness
            }

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

        /// <summary>
        ///     Defines the nested delegate type (ctor + Invoke). Delegates of a generic host must be
        ///     nested generic delegates (hook_XXX&lt;T&gt;): Invoke references the delegate's own generic
        ///     parameters, instead of the outer host's !0 (which produces Roslyn CS0570 "not supported by language").
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
    }
}
