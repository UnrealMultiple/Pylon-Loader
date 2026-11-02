namespace Pylon.ModLoader;

/// <summary>需要在每次 Mod 注册后刷新缓存的模块（例如 Global 覆写门控）。</summary>
internal interface ILoaderModuleRefresh
{
    void Refresh();
}
