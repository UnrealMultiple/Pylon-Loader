using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;

static class VerifyGenerated
{
    public static void Run(string root)
    {
        var path = Path.Combine(root, ".work/array-audit/proven-generated/Assembly-CSharp.dll");
        if (!File.Exists(path)) throw new FileNotFoundException("Run isolated CLI generation first", path);
        var asm = AssemblyDefinition.FromFile(path);
        var module = asm.ManifestModule!;
        int count = 0;
        foreach (var view in module.GetAllTypes().Where(t => t.Name == "View" && t.Methods.Any(m => m.Name == "get_Item")))
        {
            var item = view.Methods.Single(m => m.Name == "get_Item");
            if (item.Signature!.ReturnType is not ByReferenceTypeSignature || item.Signature.ParameterTypes.Count != 1)
                throw new Exception("Bad ref signature: " + item.FullName);
            if (view.Methods.Any(m => m.Name != "get_Item" && m.Name != "get_Length"))
                throw new Exception("View forwards native methods: " + view.FullName);
            foreach (var method in view.Methods)
            {
                method.CilMethodBody!.ComputeMaxStack();
                foreach (var instruction in method.CilMethodBody.Instructions)
                {
                    if (instruction.OpCode.Code is CilCode.Ldarga or CilCode.Ldarga_S or CilCode.Stobj or CilCode.Ldobj)
                        throw new Exception("Unexpected value/address conversion: " + method.FullName);
                    if (instruction.Operand is IFieldDescriptor field && field.Name == "_p" &&
                        field.Signature?.FieldType.FullName != "System.IntPtr")
                        throw new Exception("Bad field operand");
                }
            }
            count++;
        }
        var projectile = module.GetAllTypes().Single(t => t.FullName == "Terraria.Projectile");
        foreach (var name in new[] { "ai", "localAI", "oldPos" })
        {
            var prop = projectile.Properties.Single(p => p.Name == name);
            if (!prop.Signature!.ReturnType.FullName.EndsWith("+View") && !prop.Signature.ReturnType.FullName.EndsWith("/View"))
                throw new Exception("Field did not become view: " + prop.FullName);
            if (prop.SetMethod is not null) throw new Exception("View setter present");
            var copy = projectile.Properties.Single(p => p.Name == name + "Copy");
            if (copy.GetMethod is null || copy.SetMethod is null || copy.Signature!.ReturnType.FullName.Contains("View"))
                throw new Exception("Missing original copy semantics");
            prop.GetMethod!.CilMethodBody!.ComputeMaxStack();
            if (prop.GetMethod.CilMethodBody.Instructions.Any(i => i.Operand is IMethodDescriptor m && m.Name == "il2cpp_field_get_offset"))
                throw new Exception("Uncached offset");
        }
        Console.WriteLine($"PASS actual generated assembly: {count} view signatures/bodies, stack-depth checks, Projectile getters/offset caching/no view setters");
    }
}
