using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.Extensions;

namespace Il2CppInterop.Generator.Utils;

/// <summary>
/// An inline array is not an IL2CPP heap array. Require its original indexer contract,
/// explicit native offsets and a fully blittable, contiguous storage layout. Field names
/// (in particular val0/data0) are deliberately not part of this decision.
/// </summary>
internal sealed record InlineArrayLayout(int BaseOffset, int Stride, int Capacity,
    TypeSignature ElementType, int? LengthOffset)
{
    public static InlineArrayLayout? TryCreate(TypeRewriteContext context)
    {
        var type = context.OriginalType;
        if (!context.AssemblyContext.GlobalContext.IsViewEligibleFieldType(type.ToTypeSignature()))
            return null;
        var item = type.Properties.SingleOrDefault(p => p.Name == "Item" &&
            p.Signature is { HasThis: true } s && s.ParameterTypes.Count == 1 &&
            s.ParameterTypes[0].FullName == "System.Int32");
        var getter = item?.GetMethod;
        if (getter is null || getter.IsStatic || getter.Signature is null)
            return null;
        var originalElement = getter.Signature.ReturnType is ByReferenceTypeSignature byRef
            ? byRef.BaseType : getter.Signature.ReturnType;
        if (context.AssemblyContext.GlobalContext.JudgeSpecificsByOriginalType(originalElement) !=
            TypeRewriteContext.TypeSpecifics.BlittableStruct)
            return null;
        var fields = type.Fields.Where(f => !f.IsStatic).ToList();
        int? lengthOffset = null;
        // The dynamic inline-array contract has a public mutable Length field and a ref indexer.
        // A heap array or a pointer-backed container must never enter this path.
        var length = fields.SingleOrDefault(f => f.Name == "Length" && f.IsPublic &&
            f.Signature?.FieldType.FullName == "System.Int32");
        if (length is not null)
        {
            if (getter.Signature.ReturnType is not ByReferenceTypeSignature || !TryOffset(length, out var offset))
                return null;
            lengthOffset = offset;
            fields.Remove(length);
        }
        else
        {
            var lengthGetter = type.Properties.SingleOrDefault(p => p.Name == "Length")?.GetMethod;
            var setter = item!.SetMethod;
            if (lengthGetter?.Signature is not { HasThis: true } ls || ls.ParameterTypes.Count != 0 ||
                ls.ReturnType.FullName != "System.Int32" || setter?.Signature is not { HasThis: true } ss ||
                ss.ParameterTypes.Count != 2 || ss.ParameterTypes[0].FullName != "System.Int32" ||
                ss.ParameterTypes[1].FullName != originalElement.FullName || ss.ReturnType.FullName != "System.Void")
                return null;
        }
        if (fields.Count == 0 || fields.Any(f => f.Signature?.FieldType.FullName != originalElement.FullName))
            return null;
        var offsets = new List<int>();
        foreach (var field in fields)
        {
            if (!TryOffset(field, out var offset)) return null;
            offsets.Add(offset);
        }
        offsets.Sort();
        var stride = ManagedSize(context, originalElement, new HashSet<string>());
        if (stride is null || stride <= 0) return null;
        for (var i = 0; i < offsets.Count; ++i)
            if ((long)offsets[0] + (long)i * stride.Value != offsets[i]) return null;
        // No headers, hidden state, pointers or trailing metadata are accepted.
        if (lengthOffset is { } lo)
        {
            if (lo != 0 || offsets[0] != 4) return null;
        }
        else if (offsets[0] != 0) return null;
        var layout = new InlineArrayLayout(offsets[0], stride.Value, fields.Count,
            context.AssemblyContext.RewriteTypeRef(originalElement), lengthOffset);
        return InlineArrayNativeProof.Verify(context.AssemblyContext.GlobalContext.Options.InlineArrayNativeImage,
            getter, item!.SetMethod, type.Properties.SingleOrDefault(p => p.Name == "Length")?.GetMethod, layout)
            ? layout : null;
    }

    private static bool TryOffset(FieldDefinition field, out int offset)
    {
        offset = 0;
        // ExtractFieldOffset otherwise silently returns zero for absent metadata.
        if (!field.CustomAttributes.Any(a => a.Constructor?.DeclaringType?.Name == "FieldOffsetAttribute"))
            return false;
        offset = field.ExtractFieldOffset();
        return offset >= 0 && offset < 0x8000000;
    }

    private static int? ManagedSize(TypeRewriteContext context, TypeSignature signature, HashSet<string> visiting)
    {
        // Pointer-sized, generic, Nullable and reference-containing elements are intentionally excluded.
        switch (signature.FullName)
        {
            case "System.Boolean": case "System.Byte": case "System.SByte": return 1;
            case "System.Char": case "System.Int16": case "System.UInt16": return 2;
            case "System.Int32": case "System.UInt32": case "System.Single": return 4;
            case "System.Int64": case "System.UInt64": case "System.Double": return 8;
        }
        if (signature is GenericInstanceTypeSignature or GenericParameterSignature or PointerTypeSignature ||
            !visiting.Add(signature.FullName)) return null;
        try
        {
            var type = signature.Resolve();
            if (type is null) return null;
            if (type.IsEnum)
                return ManagedSize(context, type.Fields.Single(f => f.Name == "value__").Signature!.FieldType, visiting);
            var rewritten = context.AssemblyContext.GlobalContext.TryGetNewTypeForOriginal(type);
            if (rewritten?.ComputedTypeSpecifics != TypeRewriteContext.TypeSpecifics.BlittableStruct) return null;
            var size = 0;
            var alignment = 1;
            foreach (var field in type.Fields.Where(f => !f.IsStatic))
            {
                var fieldSize = ManagedSize(context, field.Signature!.FieldType, visiting);
                if (fieldSize is null || !TryOffset(field, out var offset)) return null;
                // Only naturally aligned, non-overlapping fields; unions/custom packing fall back.
                if (offset < size || offset % Math.Min(fieldSize.Value, 8) != 0) return null;
                size = checked(offset + fieldSize.Value);
                alignment = Math.Max(alignment, Math.Min(fieldSize.Value, 8));
            }
            return size == 0 ? null : checked((size + alignment - 1) / alignment * alignment);
        }
        finally { visiting.Remove(signature.FullName); }
    }
}
