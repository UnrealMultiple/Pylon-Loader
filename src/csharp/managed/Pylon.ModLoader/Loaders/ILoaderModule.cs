namespace Pylon.ModLoader;

/// <summary>
/// 一个内容管线的安装单元。实现类由 <see cref="LoaderBootstrap"/> 反射发现并构造（日志在构造时注入），
/// 按 <see cref="Order"/> 升序安装；"装过没有"只由加载阶段记录。
/// </summary>
internal interface ILoaderModule
{
    /// <summary>日志与排障用的短名。</summary>
    string Name { get; }

    /// <summary>安装顺序，小的先装（SetDefaults 补丁要早于按类型分派的 Loader）。</summary>
    int Order { get; }

    /// <summary>安装管线；返回 false 表示内容还没出现，下一次 Mod 注册时再试。</summary>
    bool Install();
}

/// <summary>需要在每次 Mod 注册后刷新缓存的模块（例如 Global 覆写门控）。</summary>
internal interface ILoaderModuleRefresh
{
    void Refresh();
}
