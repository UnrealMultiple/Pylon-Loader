using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.Utils;

namespace Il2CppInterop.Generator.Passes;

public static class Pass40GenerateFieldAccessors
{
    public static void DoPass(RewriteGlobalContext context)
    {
        foreach (var assemblyContext in context.Assemblies)
        {
            foreach (var typeContext in assemblyContext.Types)
            {
                foreach (var fieldContext in typeContext.Fields)
                {
                    if (typeContext.ComputedTypeSpecifics == TypeRewriteContext.TypeSpecifics.BlittableStruct &&
                        !fieldContext.OriginalField.IsStatic) continue;

                    var field = fieldContext.OriginalField;
                    var unmangleFieldName = fieldContext.UnmangledName;

                    if (!field.IsStatic && !typeContext.NewType.IsValueType &&
                        context.TryGetStructView(field.Signature!.FieldType) is { } viewType)
                    {
                        var viewSig = typeContext.NewType.DeclaringModule!.DefaultImporter.ImportType(viewType).ToTypeSignature();
                        var viewProperty = new PropertyDefinition(unmangleFieldName, PropertyAttributes.None,
                            PropertySignature.CreateInstance(viewSig));
                        typeContext.NewType.Properties.Add(viewProperty);
                        FieldAccessorGenerator.MakeViewGetter(field, fieldContext, viewProperty, assemblyContext.Imports, viewSig);
                        // Keep value semantics available explicitly; a view cannot be passed as the
                        // original native struct or assigned as a struct value.
                        var copyName = unmangleFieldName + "Copy";
                        while (typeContext.NewType.Properties.Any(p => p.Name == copyName) ||
                               typeContext.Fields.Any(f => f.UnmangledName == copyName)) copyName += "_";
                        var copyProperty = new PropertyDefinition(copyName, PropertyAttributes.None,
                            PropertySignature.CreateInstance(assemblyContext.RewriteTypeRef(field.Signature.FieldType)));
                        typeContext.NewType.Properties.Add(copyProperty);
                        FieldAccessorGenerator.MakeGetter(field, fieldContext, copyProperty, assemblyContext.Imports);
                        FieldAccessorGenerator.MakeSetter(field, fieldContext, copyProperty, assemblyContext.Imports);
                        fieldContext.CopyAccessor = copyProperty;
                        continue;
                    }

                    var propertyType = assemblyContext.RewriteTypeRef(fieldContext.OriginalField.Signature!.FieldType);
                    var signature = field.IsStatic
                        ? PropertySignature.CreateStatic(propertyType)
                        : PropertySignature.CreateInstance(propertyType);
                    var property = new PropertyDefinition(unmangleFieldName, PropertyAttributes.None, signature);
                    typeContext.NewType.Properties.Add(property);

                    FieldAccessorGenerator.MakeGetter(field, fieldContext, property, assemblyContext.Imports);
                    FieldAccessorGenerator.MakeSetter(field, fieldContext, property, assemblyContext.Imports);
                }
            }
        }
    }
}
