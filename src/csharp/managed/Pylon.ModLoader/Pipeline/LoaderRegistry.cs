using Pylon.ModLoader.Pipelines;
using Pylon.ModLoader.Tables;
namespace Pylon.ModLoader.Pipeline;
/// <summary>模块表：数组位置就是安装顺序，读代码即可看到依赖关系。</summary>
internal static class LoaderRegistry
{
    private static readonly ILoaderModule[] Modules =
    [
        ItemPipeline.Instance,
        ProjectilePipeline.Instance,
        NpcPipeline.Instance,
        BuffPipeline.Instance,
        MountPipeline.Instance,
        PrefixPipeline.Instance,
        RarityPipeline.Instance,
        RecipePipeline.Instance,
        TilePipeline.Instance,
        TileDrawPipeline.Instance,
        WallPipeline.Instance,
        TileEntityPipeline.Instance,
        GorePipeline.Instance,
        BannerPipeline.Instance,
        EmoteBubblePipeline.Instance,
        BiomePipeline.Instance,
        SceneEffectPipeline.Instance,
        BossBarPipeline.Instance,
        PylonPipeline.Instance,
        CloudPipeline.Instance,
        HairPipeline.Instance,
        ShopPipeline.Instance,
        LootPipeline.Instance,
        CommandPipeline.Instance,
        WaterPipeline.Instance,
        MenuPipeline.Instance,
        ResourceOverlayPipeline.Instance,
        PlayerDrawLayerPipeline.Instance,
        PlayerDrawInfoPipeline.Instance,
        BackgroundPipeline.Instance,
        EquipPipeline.Instance,
        AchievementPipeline.Instance,
        DustPipeline.Instance,
        TreePipeline.Instance,
        DamageClassPipeline.Instance,
        PlayerPipeline.Instance,
        PlayerSavePipeline.Instance,
        PlayerNetPipeline.Instance,
        NetPipeline.Instance,
        BannerNetPipeline.Instance,
        EmoteNetPipeline.Instance,
        SoundPipeline.Instance,
        TouchButtonPipeline.Instance,
        MusicPipeline.Instance,
        KeybindPipeline.Instance,
        ConfigPipeline.Instance,
        ConfigNetPipeline.Instance,
        TooltipPipeline.Instance,
        InterfaceLayerPipeline.Instance,
        SystemDrawPipeline.Instance,
    ];

    private static List<TableRequest>? _tables;

    public static IReadOnlyList<TableRequest> Tables
    {
        get
        {
            if (_tables is not null) return _tables;

            _tables = new List<TableRequest>(32);
            for (var i = 0; i < Modules.Length; i++) _tables.AddRange(Modules[i].Tables);
            return _tables;
        }
    }

    private static readonly bool[] _installed = new bool[Modules.Length];
    private static bool _allInstalled;
    private static string? _lastGates;

    public static void InstallAll()
    {
        if (_allInstalled)
        {
            RefreshAll();
            return;
        }

        var pending = false;
        for (var i = 0; i < Modules.Length; i++)
        {
            if (_installed[i]) continue;
            if (Modules[i].Install())
            {
                _installed[i] = true;
            }
            else
            {
                pending = true;
            }
        }

        LogGates();
        RefreshAll();
        if (!pending) _allInstalled = true;
    }

    private static void LogGates()
    {
        var gates = GateLog.Snapshot();
        if (gates == _lastGates) return;
        _lastGates = gates;
        LoaderHost.Current.Log.Info(gates);
    }

    public static void RefreshAll()
    {
        for (var i = 0; i < Modules.Length; i++)
        {
            if (_installed[i] && Modules[i] is ILoaderModuleRefresh refresh) refresh.Refresh();
        }
    }
}
