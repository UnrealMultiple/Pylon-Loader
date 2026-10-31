namespace Pylon.ModLoader;

/// <summary>血迹（gore）辅助：<c>&lt;mod&gt;/Gores/&lt;名字&gt;.png</c> 的编号查询。</summary>
public static class ModGore
{
    // 管线未接：Gores/*.png 的自动装载与编号分配还没做，恒回 0。
    // 语义是"没有 = 0"，不能抛异常拖垮调用方（ModWaterStyle.GetDropletGore 直接拿这个值）。
    /// <summary>本 Mod 的 <c>Gores/&lt;name&gt;.png</c> 的 gore 编号；没有则 0。</summary>
    public static int GetTypeByName(string name) => 0;
}
