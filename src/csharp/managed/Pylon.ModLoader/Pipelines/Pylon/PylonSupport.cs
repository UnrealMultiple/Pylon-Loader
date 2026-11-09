namespace Pylon.ModLoader;

/// <summary>晶塔的模组侧入口（游戏自己的晶塔系统认识 mod 晶塔之后，才有东西可刷）。</summary>
public static class PylonSupport
{
    /// <summary>
    /// 立刻重算一次游戏的晶塔列表，不等下一帧。
    /// 放下/拆掉 mod 晶塔后想让传送、附近判定与地图图标立即生效时调用。
    /// </summary>
    public static void Refresh() => PylonPipeline.RefreshNow();

    /// <summary>这个晶塔类型号对应的 ModPylon；不是 mod 晶塔返回 null。</summary>
    public static ModPylon? OfType(int pylonType) => PylonTable.Of(pylonType);
}
