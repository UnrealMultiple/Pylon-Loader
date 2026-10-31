using Terraria;

namespace Pylon.ModLoader;

/// <summary>作用于所有配方（游戏的与 mod 的）的横切类；一个类一个实例，钩子收到配方自己挑。</summary>
public abstract class GlobalRecipe : ModType
{
    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>所有内容安装完成之后一次（跨 Mod 引用放这里）。</summary>
    public virtual void PostSetupContent() { }

    /// <summary>false：配方从制作菜单里消失（向导那里还是显示）。</summary>
    public virtual bool RecipeAvailable(Recipe recipe) => true;

    /// <summary>每种材料每做一次实际消耗多少；amount 是可写引用，已带炼金桌的折扣（type 是这组材料里图标的那个）。</summary>
    public virtual void ConsumeItem(Recipe recipe, int type, ref int amount) { }

    /// <summary>做出来的物品，在进到光标或背包之前。</summary>
    public virtual void OnCraft(Item item, Recipe recipe) { }
}

// 未移植 register：注册与反查走 Pylon 的自动发现 + ModContent，不照抄静态注册表。
