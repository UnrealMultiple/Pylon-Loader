using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader;
using Terraria.GameContent;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace PipelineTest.Tests;

internal static class ArrayInteropTests
{
    public static void Register() => PT.Group("array-interop", Phase.Ready, Run);

    private static void Run()
    {
        PT.Case("ARRAY-REF1", c =>
        {
            var source = TextureAssets.NpcHead[0];
            var array = new Il2CppReferenceArray<Asset<Texture2D>>(3);
            array[0] = source;
            array[1] = source;
            c.Eq(array[0].Pointer, source.Pointer, "长度构造后逐项写引用");
            c.Eq(array[1].Pointer, source.Pointer, "第二个引用");
            var copy = new Il2CppReferenceArray<Asset<Texture2D>>(new[] { source });
            c.Eq(copy[0].Pointer, source.Pointer, "托管数组构造");
        });
        PT.Case("ARRAY-VAL1", c =>
        {
            var array = new Il2CppStructArray<int>(3);
            array[0] = 17;
            array[1] = -29;
            array[2] = 41;
            var copy = new Il2CppStructArray<int>(new[] { 17, -29, 41 });
            for (var i = 0; i < 3; i++) c.Eq(array[i], copy[i], $"值数组两种构造[{i}]");
        });
        PT.Case("ARRAY-STR1", c =>
        {
            var array = new Il2CppStringArray(2);
            array[0] = "Pylon 数组回归";
            array[1] = null!;
            c.Eq(array[0], "Pylon 数组回归", "字符串写入");
            c.True(array[1] is null, "null 字符串写入");
        });
    }
}
