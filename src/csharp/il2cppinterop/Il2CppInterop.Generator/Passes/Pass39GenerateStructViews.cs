using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Il2CppInterop.Common;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.Extensions;
using Il2CppInterop.Generator.Utils;

namespace Il2CppInterop.Generator.Passes;

public static class Pass39GenerateStructViews
{
    public static void DoPass(RewriteGlobalContext context)
    {
        foreach (var assembly in context.Assemblies)
        foreach (var type in assembly.Types)
        {
            if (!context.IsViewEligibleFieldType(type.OriginalType.ToTypeSignature()))
                continue;
            var storage = InlineArrayLayout.TryCreate(type);
            if (storage is null)
                continue;

            var module = type.NewType.DeclaringModule!;
            var view = new TypeDefinition(null, PickViewName(type.NewType), TypeAttributes.NestedPublic,
                assembly.Imports.Module.ValueType().ToTypeDefOrRef());
            var pointer = new FieldDefinition("_p", FieldAttributes.Public,
                new FieldSignature(assembly.Imports.Module.IntPtr()));
            view.Fields.Add(pointer);
            // Keep the native owner's GC handle alive when the view is copied into a local.
            view.Fields.Add(new FieldDefinition("_owner", FieldAttributes.Public,
                new FieldSignature(assembly.Imports.Il2CppObjectBase)));
            var viewRef = view.ToTypeSignature().ToTypeDefOrRef();
            var pointerRef = new MemberReference(viewRef, "_p",
                new FieldSignature(assembly.Imports.Module.IntPtr()));
            var intType = assembly.Imports.Module.Int();
            var getter = new MethodDefinition("get_Item",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                MethodSignature.CreateInstance(new ByReferenceTypeSignature(storage.ElementType), intType));
            getter.CilMethodBody = new();
            var body = getter.CilMethodBody.Instructions;
            var invalid = new CilInstructionLabel();
            body.Add(OpCodes.Ldarg_0);
            body.Add(OpCodes.Ldfld, pointerRef);
            body.Add(OpCodes.Brfalse, invalid);
            body.AddLoadArgument(1);
            body.Add(OpCodes.Ldc_I4_0);
            body.Add(OpCodes.Blt, invalid);
            body.AddLoadArgument(1);
            body.Add(OpCodes.Ldarg_0);
            var lengthRef = new MemberReference(viewRef, "get_Length", MethodSignature.CreateInstance(intType));
            body.Add(OpCodes.Call, lengthRef);
            body.Add(OpCodes.Bge, invalid);
            body.Add(OpCodes.Ldarg_0);
            body.Add(OpCodes.Ldfld, pointerRef);
            body.Add(OpCodes.Ldc_I4, storage.BaseOffset);
            body.Add(OpCodes.Add);
            body.AddLoadArgument(1);
            body.Add(OpCodes.Conv_I);
            body.Add(OpCodes.Ldc_I4, storage.Stride);
            body.Add(OpCodes.Mul);
            body.Add(OpCodes.Add);
            var runtimeScope = assembly.Imports.Il2CppObjectBase.ToTypeDefOrRef().Scope;
            var access = new TypeReference(module, runtimeScope, "Il2CppInterop.Runtime", "NativeArrayAccess");
            var asRef = new MemberReference(access, "AsRef", MethodSignature.CreateStatic(
                new ByReferenceTypeSignature(new GenericParameterSignature(GenericParameterType.Method, 0)), 1,
                assembly.Imports.Module.IntPtr()));
            body.Add(OpCodes.Call, asRef.MakeGenericInstanceMethod(storage.ElementType));
            body.Add(OpCodes.Ret);
            invalid.Instruction = body.Add(OpCodes.Newobj, assembly.Imports.Module.IndexOutOfRangeExceptionCtor());
            body.Add(OpCodes.Throw);
            view.Methods.Add(getter);
            var item = new PropertyDefinition("Item", PropertyAttributes.None,
                PropertySignature.CreateInstance(new ByReferenceTypeSignature(storage.ElementType), intType));
            item.SetSemanticMethods(getter, null);
            view.Properties.Add(item);
            view.CustomAttributes.Add(new CustomAttribute(
                ReferenceCreator.CreateInstanceMethodReference(".ctor", assembly.Imports.Module.Void(),
                    assembly.Imports.Module.DefaultMemberAttribute().ToTypeDefOrRef(), assembly.Imports.Module.String()),
                new CustomAttributeSignature(new CustomAttributeArgument(assembly.Imports.Module.String(), "Item"))));

            var lengthGetter = new MethodDefinition("get_Length",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                MethodSignature.CreateInstance(intType));
            lengthGetter.CilMethodBody = new() { InitializeLocals = true };
            var lb = lengthGetter.CilMethodBody.Instructions;
            if (storage.LengthOffset is { } lengthOffset)
            {
                var badLength = new CilInstructionLabel();
                var nativeLength = new CilLocalVariable(intType);
                lengthGetter.CilMethodBody.LocalVariables.Add(nativeLength);
                lb.Add(OpCodes.Ldarg_0);
                lb.Add(OpCodes.Ldfld, pointerRef);
                lb.Add(OpCodes.Brfalse, badLength);
                lb.Add(OpCodes.Ldarg_0);
                lb.Add(OpCodes.Ldfld, pointerRef);
                lb.Add(OpCodes.Ldc_I4, lengthOffset);
                lb.Add(OpCodes.Add);
                lb.Add(OpCodes.Call, new MemberReference(access, "ReadLength",
                    MethodSignature.CreateStatic(intType, assembly.Imports.Module.IntPtr())));
                lb.Add(OpCodes.Stloc, nativeLength);
                lb.Add(OpCodes.Ldloc, nativeLength);
                lb.Add(OpCodes.Ldc_I4, storage.Capacity);
                lb.Add(OpCodes.Bgt_Un, badLength);
                lb.Add(OpCodes.Ldloc, nativeLength);
                lb.Add(OpCodes.Ret);
                badLength.Instruction = lb.Add(OpCodes.Newobj, assembly.Imports.Module.IndexOutOfRangeExceptionCtor());
                lb.Add(OpCodes.Throw);
            }
            else lb.Add(OpCodes.Ldc_I4, storage.Capacity);
            lb.Add(OpCodes.Ret);
            view.Methods.Add(lengthGetter);
            var length = new PropertyDefinition("Length", PropertyAttributes.None,
                PropertySignature.CreateInstance(intType));
            length.SetSemanticMethods(lengthGetter, null);
            view.Properties.Add(length);

            type.NewType.NestedTypes.Add(view);
            context.StructViews[type] = view;
        }

        // Pass20 already resolved FieldInfo pointers. Cache offsets once in the owning cctor,
        // never call il2cpp_field_get_offset on each hot-path view access.
        foreach (var assembly in context.Assemblies)
        foreach (var type in assembly.Types)
        foreach (var field in type.Fields)
        {
            if (field.OriginalField.IsStatic || type.NewType.IsValueType ||
                context.TryGetStructView(field.OriginalField.Signature!.FieldType) is null) continue;
            var offset = new FieldDefinition("NativeFieldOffset_" + field.UnmangledName,
                FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly,
                assembly.Imports.Module.Int());
            type.NewType.Fields.Add(offset);
            field.CachedOffsetField = new MemberReference(type.SelfSubstitutedRef, offset.Name, offset.Signature);
            var cctor = type.NewType.GetOrCreateStaticConstructor().CilMethodBody!.Instructions;
            var index = cctor.Count - 1;
            cctor.Insert(index++, new CilInstruction(OpCodes.Ldsfld, field.PointerField));
            cctor.Insert(index++, new CilInstruction(OpCodes.Call, assembly.Imports.IL2CPP_il2cpp_field_get_offset.Value));
            cctor.Insert(index, new CilInstruction(OpCodes.Stsfld, field.CachedOffsetField));
        }
    }

    private static string PickViewName(TypeDefinition type)
    {
        var name = "View";
        while (type.NestedTypes.Any(n => n.Name == name))
            name += "_";
        return name;
    }

}
