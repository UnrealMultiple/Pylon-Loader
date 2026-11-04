namespace Pylon.ModLoader;

/// <summary>安装管线的一步。返回 false 表示"还没做完"，下一帧接着来（可续步骤）。</summary>
internal interface IInstallStep
{
    string Name { get; }

    InstallStage Stage { get; }

    bool Run();
}

/// <summary>一次性步骤的适配器：跑完即完成。</summary>
internal sealed class Step(string name, InstallStage stage, Action action) : IInstallStep
{
    public string Name { get; } = name;

    public InstallStage Stage { get; } = stage;

    public bool Run()
    {
        action();
        return true;
    }
}
