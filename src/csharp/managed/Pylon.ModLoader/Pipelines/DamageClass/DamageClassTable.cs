namespace Pylon.ModLoader;

/// <summary>伤害类别表：物品编号 → 类别（只收作者声明了 <c>DamageClass</c> 的物品）。</summary>
internal static class DamageClassTable
{
    private static readonly Dictionary<int, ModDamageClass> ByItem = [];

    internal static int Count => ByItem.Count;

    internal static ModDamageClass? Of(int itemType) => ByItem.TryGetValue(itemType, out var cls) ? cls : null;

    /// <summary>收表；幂等（同一个物品重复注册时后者覆盖）。</summary>
    internal static void Rebuild()
    {
        ByItem.Clear();

        foreach (var item in ModContent.GetContent<ModItem>())
        {
            ModDamageClass? cls;
            try
            {
                cls = item.DamageClass;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[damageclass] {item.Name}.DamageClass 异常：{Safe.Describe(e)}");
                continue;
            }

            if (cls is null) continue;
            if (item.Type <= 0) continue;

            ByItem[item.Type] = cls;
        }
    }
}
