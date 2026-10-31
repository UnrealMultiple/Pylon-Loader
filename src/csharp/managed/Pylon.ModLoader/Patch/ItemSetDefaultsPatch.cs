// 原版 SetDefaults 会把 type >= ItemID.Count 的物品归零，所以自定义物品拦在 On.Terraria.Item.SetDefaults：
// 不调 orig（跳过原版），ResetStats + 补 type + 跑 ModItem.SetDefaults + RebuildTooltip。
// hook 处理器只调静态方法（实例/虚拟方法内联易炸），逻辑抽到 Run。
using Terraria;

namespace Pylon.ModLoader;

/// <summary>Item.SetDefaults 的 mod 类型接管（跳过原版）。</summary>
internal sealed class ItemSetDefaultsPatch : ILoaderModule
{
    public ItemSetDefaultsPatch(ModLogger logger) => _ = logger; // 本管线不用日志

    public string Name => "item.setdefaults";

    public int Order => 110;

    /// <summary>自定义物品的最后失败原因（给 Mod 侧日志取证）。</summary>
    public static Action<string> Error = _ => { };

    /// <summary>注册期装一次（幂等）。</summary>
    public bool Install()
    {
        On.Terraria.Item.SetDefaults += SetDefaults;
        return true;
    }

    /// <summary>已注册的 mod 类型：跳过原版（不调 orig）；其余照跑原版。</summary>
    private static void SetDefaults(On.Terraria.Item.orig_SetDefaults orig, Item self, int type,
                                    Terraria.GameContent.Items.ItemVariant variant)
    {
        if (type < 0 || ModContent.Find(ContentKind.Item, type) is null)
        {
            orig(self, type, variant);
            return;
        }
        Run(self, type);
    }

    /// <summary>实际逻辑（普通静态方法，避开 hook 处理器的内联限制）。</summary>
    private static void Run(Item item, int type)
    {
        try
        {
            item.ResetStats(type);
            item.type = type;
            // 实体实例：每个生成/换类型的实体都有自己的一份 ModItem。
            var mod = ItemEntities.InstanceOf(item);
            if (mod is not null)
            {
                mod.SetDefaults(item);
                mod.PostSetDefaults(item);
            }
            item.RebuildTooltip();
        }
        catch (Exception e)
        {
            try { Error?.Invoke($"Item.SetDefaults 自定义分支异常：{e.Message}"); } catch { }
        }
    }
}
