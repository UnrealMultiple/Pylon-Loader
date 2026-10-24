namespace Pylon.Mod;

/// <summary>
/// Mod 入口契约。宿主反射发现实现类并创建实例：先 <see cref="Load"/>（早阶段），
/// 游戏内容就绪后再 <see cref="OnContentReady"/>（晚阶段）。
/// </summary>
public interface IPylonMod
{
    /// <summary>早阶段：注册内容、读取配置。此时游戏的内容表还没建好。</summary>
    void Load(IModContext context);

    /// <summary>晚阶段：游戏内容已就绪，适合安装依赖游戏状态的钩子。</summary>
    void OnContentReady(IModContext context) { }
}
