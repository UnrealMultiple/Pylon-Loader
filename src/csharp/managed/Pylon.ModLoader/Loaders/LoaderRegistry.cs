namespace Pylon.ModLoader;

/// <summary>
/// 模块表：数组位置就是安装顺序，读代码即可看到依赖关系。
/// 顺序约束：SetDefaults 补丁先于按类型分派的模块；物品与弹幕先于 NPC（NPC 会查它们的表）。
/// </summary>
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

    private static int _installed;

    /// <summary>
    /// 按顺序安装；遇到未就绪的模块就停下，下次注册再继续。
    /// 挂钩失败不吞：抛出去让调用方看见——半挂的管线比没挂的更难查。
    /// </summary>
    public static void InstallAll()
    {
        while (_installed < Modules.Length)
        {
            if (!Modules[_installed].Install()) return;
            _installed++;
        }

        RefreshAll();
    }

    public static void RefreshAll()
    {
        for (var i = 0; i < _installed; i++)
        {
            if (Modules[i] is ILoaderModuleRefresh refresh) refresh.Refresh();
        }
    }
}
