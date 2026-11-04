namespace Pylon.ModLoader;

/// <summary>
/// 一个内容模块的装配单元：把某类内容的游戏钩子挂上去。
/// 顺序由 <see cref="LoaderRegistry"/> 的数组位置决定，不再有排序字段。
/// </summary>
internal interface ILoaderModule
{
    /// <summary>日志与排障用的短名。</summary>
    string Name { get; }

    /// <summary>挂钩子；幂等。返回 false 表示前提未满足，下次注册再试。</summary>
    bool Install();
}

/// <summary>需要在每次 Mod 注册后刷新门控缓存的模块（例如 Global 覆写判定）。</summary>
internal interface ILoaderModuleRefresh
{
    void Refresh();
}
