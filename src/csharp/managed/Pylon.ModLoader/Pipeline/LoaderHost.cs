namespace Pylon.ModLoader;

/// <summary>
/// 管线宿主：内容表、钩子总线、安装管线、Mod 集合的唯一持有者。
/// 静态类只是门面，状态都在这里；换进程、换游戏实例不需要清理任何静态字段。
/// </summary>
internal sealed class LoaderHost
{
    public static LoaderHost Current { get; } = new();

    private LoaderHost() => Install = new InstallPipeline(this);

    public IModLog Log { get; private set; } = NullLog.Instance;

    public LoaderPhase Phase { get; private set; } = LoaderPhase.Idle;

    public ModSet Mods { get; } = new();

    public ContentRegistry Content { get; } = new();

    public HookBus Hooks { get; } = new();

    public InstallPipeline Install { get; }

    public void Configure(IModLog log)
    {
        Log = log ?? NullLog.Instance;
        Safe.Sink = Log;
        ContentErrors.Report = Log.Warn;
    }

    public void Register(Mod mod)
    {
        Phase = LoaderPhase.Registering;

        UnloadedItems.Reserve();

        mod.Index = Mods.Add(mod);
        mod.Plan = HookScan.Of(mod.GetType(), typeof(Mod));
        mod.Types = RegistrationPipeline.Register(this, mod);

        LoaderRegistry.InstallAll();
        ModLifecycle.Install();
        Hooks.Rebuild(Mods.Mods);
    }

    public void ContentReady()
    {
        if (Phase is LoaderPhase.Installing or LoaderPhase.Ready) return;
        Phase = LoaderPhase.Installing;
        Install.Arm();
    }

    internal void MarkReady()
    {
        Phase = LoaderPhase.Ready;
        GameContent.Installed = true;
    }
}
