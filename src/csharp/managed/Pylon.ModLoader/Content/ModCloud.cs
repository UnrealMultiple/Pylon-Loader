using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader.Content;
/// <summary>自定义云：一个类型一个实例；贴图由管线按 &lt;Name&gt; 找，SpawnChance 是它在云朵抽签里的权重。</summary>
public abstract class ModCloud : ModType
{
    /// <summary>相对 ModCloud 覆写了哪些钩子：注册时算一次挂在实例上，分派期只做位运算。</summary>
    internal HookMask Hooks { get; set; }

    /// <summary>这个钩子要不要分派给本实例。</summary>
    internal bool HookEnabled(HookId hook) => Hooks.Has(hook);

    /// <summary>稀有云（对的是游戏那批稀有云，不是 22 朵普通云）；false = 普通云。</summary>
    public virtual bool RareCloud => false;

    /// <summary>抽签权重（0 = 不参与）；普通云对游戏 22 朵，稀有云对游戏那批稀有云。</summary>
    public virtual float SpawnChance() => 1f;

    /// <summary>这朵云出现之后（云已经是活的，位置和大小都定了）。</summary>
    public virtual void OnSpawn(Cloud cloud) { }

    /// <summary>
    /// 逐云绘制（原版每帧画这朵云时进来；drawData 是游戏按原版公式算好的那张）。
    /// 返回 true 且没改 <paramref name="drawData"/> = 原版照画；
    /// 改了 <paramref name="drawData"/> = 只画改后的那张；
    /// 返回 false = 原版这张不画（往 <paramref name="drawDataCache"/> 塞的每条都会跟着画）。
    /// </summary>
    public virtual bool Draw(List<DrawData> drawDataCache, Cloud cloud, int cloudIndex, ref DrawData drawData) => true;
}
