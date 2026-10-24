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
        private sealed record DelegateParameter(TypeReference Type, string Name,
            ParameterAttributes Attributes = ParameterAttributes.None, ParameterDefinition? Source = null);

        internal void EmitTopLevel(TypeNode node)
        {
            var ns = string.IsNullOrEmpty(node.Definition.Namespace) ? "On" : "On." + node.Definition.Namespace;
            var type = new TypeDefinition(ns, ArityTypeName(node.Name, node),
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _objectType);
            var args = BindGenericParams(type, node.Definition.GenericParameters);
            _module.Types.Add(type);
            _typeCount++;
            EmitMembers(type, node, args);
        }

        private void EmitNested(TypeDefinition parent, TypeNode node)
        {
            var type = new TypeDefinition(string.Empty, ArityTypeName(node.Name, node),
                TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _objectType);
            var args = BindGenericParams(type, node.Definition.GenericParameters);
            parent.NestedTypes.Add(type);
            _typeCount++;
            EmitMembers(type, node, args);
        }

        private static string ArityTypeName(string name, TypeNode node)
        {
            var tick = node.Definition.Name.IndexOf('`');
            return tick < 0 ? name : name + node.Definition.Name.Substring(tick);
        }

        private static List<GenericParameter> BindGenericParams(TypeDefinition owner, IEnumerable<GenericParameter> source)
        {
            var originals = source.ToList();
            var args = originals.Select(p => new GenericParameter(p.Name, owner) { Attributes = p.Attributes }).ToList();
            foreach (var arg in args) owner.GenericParameters.Add(arg);
            CopyConstraints(originals, args, t => MapSignature(t, p =>
                p.Position < args.Count ? args[p.Position] : p));
            return args;
        }

        private static void CopyConstraints(IList<GenericParameter> source, IList<GenericParameter> destination,
            Func<TypeReference, TypeReference> map)
        {
            for (var i = 0; i < source.Count; i++)
            {
                foreach (var attribute in source[i].CustomAttributes)
                    destination[i].CustomAttributes.Add(attribute);
                foreach (var constraint in source[i].Constraints)
                {
                    var copy = new GenericParameterConstraint(map(constraint.ConstraintType));
                    foreach (var attribute in constraint.CustomAttributes) copy.CustomAttributes.Add(attribute);
                    destination[i].Constraints.Add(copy);
                }
            }
        }

        private void EmitMembers(TypeDefinition owner, TypeNode node, List<GenericParameter> typeArgs)
        {
            foreach (var method in node.Methods) EmitHook(owner, node.Definition, method, typeArgs);
            foreach (var child in node.Children) EmitNested(owner, child);
        }

        private void EmitHook(TypeDefinition owner, TypeDefinition gameType, MethodNode node, List<GenericParameter> typeArgs)
        {
            var target = node.Definition;
            var methodArgs = new List<GenericParameter>();
            if (target.HasGenericParameters)
            {
                var host = new TypeDefinition(string.Empty, node.EventName + "`" + target.GenericParameters.Count,
                    TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _objectType);
                owner.NestedTypes.Add(host);
                _typeCount++;
                // Nested generic metadata carries all enclosing parameters before its own parameters.
                typeArgs = BindGenericParams(host, gameType.GenericParameters);
                foreach (var p in target.GenericParameters)
                {
                    var arg = new GenericParameter(p.Name, host) { Attributes = p.Attributes };
                    host.GenericParameters.Add(arg);
                    methodArgs.Add(arg);
                }
                CopyConstraints(target.GenericParameters, methodArgs, t => MapTarget(t, typeArgs, methodArgs));
                owner = host;
            }

            var allArgs = owner.GenericParameters.ToList();
            var usedNames = new HashSet<string>(StringComparer.Ordinal) { "orig", "self" };
            var parameters = new List<DelegateParameter>();
            if (!target.IsStatic) parameters.Add(new DelegateParameter(Close(gameType, typeArgs), "self"));
            foreach (var p in target.Parameters)
                parameters.Add(new DelegateParameter(MapTarget(p.ParameterType, typeArgs, methodArgs),
                    ParameterName(p, usedNames), p.Attributes & (ParameterAttributes.In | ParameterAttributes.Out), p));
            var returnType = target.IsConstructor ? _voidType : MapTarget(target.ReturnType, typeArgs, methodArgs);
            var orig = DefineDelegate(owner, node.OrigName, returnType, parameters, allArgs);
            var handlerParameters = new List<DelegateParameter> { new(orig, "orig") };
            handlerParameters.AddRange(parameters);
            var handler = DefineDelegate(owner, node.HookName, returnType, handlerParameters, allArgs);
            DefineEvent(owner, gameType, target, node, orig, handler, typeArgs, methodArgs);
        }

        private void DefineEvent(TypeDefinition owner, TypeDefinition gameType, MethodDefinition target,
            MethodNode node, TypeReference orig, TypeReference handler,
            List<GenericParameter> typeArgs, List<GenericParameter> methodArgs)
        {
            var field = new FieldDefinition("__pylon_ep_" + node.Index, FieldAttributes.Private | FieldAttributes.Static, _endpointType);
            owner.Fields.Add(field);
            var fieldRef = new FieldReference(field.Name, field.FieldType, Close(owner, owner.GenericParameters));
            MethodDefinition Accessor(string prefix)
            {
                var method = new MethodDefinition(prefix + node.EventName,
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName, _voidType);
                method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, handler));
                method.Body.InitLocals = false;
                owner.Methods.Add(method);
                return method;
            }
            var add = Accessor("add_");
            var remove = Accessor("remove_");
            EmitAdd(add, fieldRef, gameType, target, orig, handler, typeArgs, methodArgs);
            EmitRemove(remove, fieldRef);
            add.Body.OptimizeMacros();
            remove.Body.OptimizeMacros();
            owner.Events.Add(new EventDefinition(node.EventName, EventAttributes.None, handler) { AddMethod = add, RemoveMethod = remove });
            _hookCount++;
        }

        private static bool IsCompactCandidate(TypeDefinition gameType, MethodDefinition target) =>
            !target.IsConstructor && !target.HasGenericParameters &&
            gameType.Methods.Count(m => !m.IsConstructor && m.Name == target.Name &&
                m.GenericParameters.Count == target.GenericParameters.Count && m.Parameters.Count == target.Parameters.Count) == 1;

        private void EmitAdd(MethodDefinition accessor, FieldReference field, TypeDefinition gameType,
            MethodDefinition target, TypeReference orig, TypeReference handler,
            List<GenericParameter> typeArgs, List<GenericParameter> methodArgs)
        {
            if (methodArgs.Count > 0 && _bindGenericOnce == null)
                throw new InvalidOperationException("RuntimeDetour must expose the 8-parameter HookEndpoint.BindGenericOnce protocol to generate generic method events.");
            var il = accessor.Body.GetILProcessor();
            il.Emit(OpCodes.Ldsflda, field);
            EmitTypeOf(il, gameType);
            il.Emit(OpCodes.Ldstr, target.Name);
            var compact = typeArgs.Count == 0 && methodArgs.Count == 0 && IsCompactCandidate(gameType, target);
            if (compact) il.Emit(OpCodes.Ldc_I4, target.Parameters.Count);
            else EmitTypes(il, target.Parameters.Select(p => MapTarget(p.ParameterType, typeArgs, methodArgs)));
            if (methodArgs.Count > 0)
            {
                EmitTypes(il, typeArgs);
                EmitTypes(il, methodArgs);
            }
            else if (typeArgs.Count > 0) EmitTypes(il, typeArgs);
            EmitTypeOf(il, orig);
            EmitTypeOf(il, handler);
            il.Emit(OpCodes.Call, methodArgs.Count > 0 ? _bindGenericOnce :
                typeArgs.Count > 0 ? _bindWithArgs : compact ? _bindCompact : _bindOnce);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _add);
            il.Emit(OpCodes.Ret);
        }

        private void EmitTypes(ILProcessor il, IEnumerable<TypeReference> types)
        {
            var array = types.ToArray();
            il.Emit(OpCodes.Ldc_I4, array.Length);
            il.Emit(OpCodes.Newarr, _typeType);
            for (var i = 0; i < array.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                EmitTypeOf(il, array[i]);
                il.Emit(OpCodes.Stelem_Ref);
            }
        }

        private void EmitTypeOf(ILProcessor il, TypeReference type)
        {
            il.Emit(OpCodes.Ldtoken, type);
            il.Emit(OpCodes.Call, _getTypeFromHandle);
        }

        private void EmitRemove(MethodDefinition method, FieldReference field)
        {
            var il = method.Body.GetILProcessor();
            var end = il.Create(OpCodes.Ret);
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Brfalse, end);
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _remove);
            il.Append(end);
        }

        private TypeReference DefineDelegate(TypeDefinition owner, string name, TypeReference returnType,
            IList<DelegateParameter> parameters, List<GenericParameter> hostArgs)
        {
            var type = new TypeDefinition(string.Empty, name,
                TypeAttributes.NestedPublic | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _multicastDelegateType);
            owner.NestedTypes.Add(type);
            var args = BindGenericParams(type, hostArgs);
            TypeReference Map(TypeReference t) => MapSignature(t, p =>
                hostArgs.IndexOf(p) is var index && index >= 0 ? args[index] : p);
            var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig |
                MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, _voidType)
            { ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed };
            ctor.Parameters.Add(new ParameterDefinition("object", ParameterAttributes.None, _objectType));
            ctor.Parameters.Add(new ParameterDefinition("method", ParameterAttributes.None, _intPtrType));
            type.Methods.Add(ctor);
            var invoke = new MethodDefinition("Invoke", MethodAttributes.Public | MethodAttributes.HideBySig |
                MethodAttributes.NewSlot | MethodAttributes.Virtual, Map(returnType))
            { ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed };
            foreach (var p in parameters)
            {
                var parameterType = Map(p.Type);
                // C# requires the InAttribute modreq on delegate Invoke's readonly byrefs,
                // even when the source method only carries [In] + IsReadOnlyAttribute.
                if ((p.Attributes & ParameterAttributes.In) != 0 && parameterType is ByReferenceType &&
                    p.Source?.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute") == true)
                    parameterType = new RequiredModifierType(new TypeReference("System.Runtime.InteropServices", "InAttribute", _module, _module.TypeSystem.CoreLibrary), parameterType);
                var parameter = new ParameterDefinition(p.Name, p.Attributes, parameterType);
                if (p.Source != null)
                    foreach (var attribute in p.Source.CustomAttributes)
                        parameter.CustomAttributes.Add(attribute);
                invoke.Parameters.Add(parameter);
            }
            type.Methods.Add(invoke);
            return Close(type, hostArgs);
        }

        private static TypeReference Close(TypeReference type, IEnumerable<GenericParameter> arguments)
        {
            var args = arguments.ToArray();
            if (args.Length == 0) return type;
            var closed = new GenericInstanceType(type);
            foreach (var arg in args) closed.GenericArguments.Add(arg);
            return closed;
        }
    }
}