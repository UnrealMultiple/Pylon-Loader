using System.Diagnostics;
using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;

namespace Pylon.ModLoader;

/// <summary>安装计划：唯一的"装什么、按什么顺序装"的事实来源。</summary>
internal static class InstallPlan
{
    public static IInstallStep[] Build(LoaderHost host)
    {
        var steps = new List<IInstallStep>(24)
        {
            new Step("内容发号", InstallStage.Layout, LoaderRegistry.RefreshAll),

            new GrowTablesStep(host),

            new Step("名字接管", InstallStage.Static, () => ItemNamePatch.Install(host.Log)),

            new Step("buff 静态默认值", InstallStage.Static, InstallSteps.BuffStaticDefaults),
            new Step("buffImmune 扩长", InstallStage.Static, GrowBuffImmune),
            new Step("实例表扩容", InstallStage.Static, InstanceTables.Install),
            new Step("坐骑安装", InstallStage.Static, () => MountTable.Build(host.Log)),
            new Step("前缀安装", InstallStage.Static, () => PrefixTable.Build(host.Log)),
            new Step("稀有度安装", InstallStage.Static, () => RarityTable.StaticDefaults(host.Log)),
            new Step("方块实体安装", InstallStage.Static, () => TileEntityPipeline.Instance.Build()),
            new Step("血腥粒子安装", InstallStage.Static, () => GorePipeline.Instance.Build()),
            new Step("装备贴图安装", InstallStage.Static, () => EquipPipeline.Instance.Build()),
            new Step("成就安装", InstallStage.Static, () => AchievementPipeline.Instance.Build()),
            new Step("尘土安装", InstallStage.Static, () => DustPipeline.Instance.Build()),
            new Step("伤害类别安装", InstallStage.Static, () => DamageClassPipeline.Instance.Build()),
            new Step("云安装", InstallStage.Static, () => CloudPipeline.Instance.Build()),
            new Step("发型安装", InstallStage.Static, () => HairPipeline.Instance.Build()),
            new Step("水样式安装", InstallStage.Static, () => WaterPipeline.Instance.Build()),

            new Step("贴图上传", InstallStage.Surface, () => InstallSteps.LoadTextures(host)),
            new Step("写贴图与文本", InstallStage.Surface, () => InstallSteps.WriteTextures(host)),
            new Step("水贴图安装", InstallStage.Surface, () => WaterPipeline.Instance.SetupTextures()),

            new Step("物品样例", InstallStage.Samples, InstallSteps.RegisterItemSamples),
            new Step("聊天标签上限", InstallStage.Samples, () => ChatTagPatches.Build(host.Log)),
            new Step("物品排序白名单", InstallStage.Samples, ItemSortingHooks.Classify),
            new Step("墙壁安装", InstallStage.Samples, () => WallPipeline.Instance.Build()),
            new Step("方块安装", InstallStage.Samples, () => TilePipeline.Instance.Build()),
            new Step("树安装", InstallStage.Samples, () => TreePipeline.Instance.Build()),
            new Step("弹幕模板", InstallStage.Samples, InstallSteps.ProjectileTemplates),
            new Step("NPC 静态默认值", InstallStage.Samples, InstallSteps.NpcStaticDefaults),
            new Step("NPC 样例", InstallStage.Samples, InstallSteps.RegisterNpcSamples),
            new Step("城镇 NPC 档案", InstallStage.Samples, NpcProfilePipeline.Build),
            new Step("NPC 头像", InstallStage.Samples, NpcHeadHooks.Build),
            new Step("城镇 NPC 商店", InstallStage.Samples, () => NpcPipeline.Instance.SetupShops(host.Log)),
            new Step("旗帜安装", InstallStage.Samples, () => BannerPipeline.Instance.Build()),
            new Step("表情气泡安装", InstallStage.Static, () => EmoteBubblePipeline.Instance.Build()),
            new Step("生物群系安装", InstallStage.Static, () => BiomePipeline.Instance.Build()),
            new Step("场景效果安装", InstallStage.Static, () => SceneEffectPipeline.Instance.Build()),
            new Step("血条安装", InstallStage.Static, () => BossBarPipeline.Instance.Build()),
            new Step("晶塔安装", InstallStage.Static, () => PylonPipeline.Instance.Build()),
            new Step("商店安装", InstallStage.Static, () => ShopPipeline.Instance.Build()),
            new Step("掉落安装", InstallStage.Mods, () => LootPipeline.Instance.Build()),
            new Step("NPC 图鉴", InstallStage.Mods, NpcBestiaryPipeline.Build),
            new Step("命令安装", InstallStage.Static, () => CommandPipeline.Instance.Build()),
            new Step("菜单主题安装", InstallStage.Static, () => MenuPipeline.Instance.Build()),
            new Step("资源覆盖层安装", InstallStage.Static, () => ResourceOverlayPipeline.Instance.Build()),
            new Step("玩家绘制层安装", InstallStage.Static, () => PlayerDrawLayerPipeline.Instance.Build()),
            new Step("背景安装", InstallStage.Static, () => BackgroundPipeline.Instance.Build()),
            new Step("额外跳跃安装", InstallStage.Static, () => ExtraJumpTable.Build(host.Log)),
            new Step("系统阶段", InstallStage.Static, () => SystemStages.Build(host.Log)),
            new Step("系统地图时机", InstallStage.Static, SystemMapHooks.Install),

            new HookStep("ResizeArrays", InstallStage.Mods, HookId.ResizeArrays, static mod => mod.ResizeArrays()),
            new Step("System.ResizeArrays", InstallStage.Mods, SystemLoader.ResizeArrays),
            new HookStep("Mod.SetupContent", InstallStage.Mods, HookId.SetupContent, static mod => mod.SetupContent()),
            new Step("System.SetStaticDefaults", InstallStage.Mods, SystemStaticDefaults),
            new HookStep("Mod.AddRecipeGroups", InstallStage.Mods, HookId.AddRecipeGroups, static mod => mod.AddRecipeGroups()),
            new Step("System.AddRecipeGroups", InstallStage.Mods, SystemRecipeGroups),
            new Step("GlobalNPC 配方组", InstallStage.Mods, GlobalRecipeGroups),
            new HookStep("Mod.PostSetupContent", InstallStage.Mods, HookId.PostSetupContent, static mod => mod.PostSetupContent()),
            new Step("System.PostSetupContent", InstallStage.Mods, SystemPostSetupContent),
            new Step("渔夫任务鱼", InstallStage.Mods, ItemAnglerQuest.Build),
            new Step("本地化加载完成", InstallStage.Mods, ModEnvironmentHooks.LocalizationsLoaded),
            new Step("提示重建开关", InstallStage.Mods, () => TooltipPipeline.Instance.Build()),
            new Step("GlobalNPC 静态与配方", InstallStage.Mods, GlobalNpcTasks),

            new Step("配方注册", InstallStage.Recipes, RecipesRegister),
            new HookStep("Mod.PostAddRecipes", InstallStage.Recipes, HookId.PostAddRecipes, static mod => mod.PostAddRecipes()),
            new Step("System.PostAddRecipes", InstallStage.Recipes, SystemPostAddRecipes),
            new Step("配方收尾", InstallStage.Recipes, () => RecipeTable.Finish(LoaderHost.Current.Log)),
            new HookStep("Mod.PostSetupRecipes", InstallStage.Recipes, HookId.PostSetupRecipes, static mod => mod.PostSetupRecipes()),
            new Step("System.PostSetupRecipes", InstallStage.Recipes, SystemLoader.PostSetupRecipes),
        };

        return [.. steps];
    }

    private static void GrowBuffImmune() => BuffPipeline.Instance.GrowImmuneArrays();

    private static void RecipesRegister()
    {
        foreach (var mod in LoaderHost.Current.Mods.Mods)
        {
            Safe.Run(mod.Name + ".AddRecipes", mod.AddRecipes);
            foreach (var system in SystemLoader.Of(mod))
            {
                Safe.Run($"{system.FullKey}.AddRecipes", system.AddRecipes);
            }
            foreach (var item in ModContent.GetContent<ModItem>())
            {
                if (item.Mod != mod) continue;
                Safe.Run($"{mod.Name}/{item.Name}.AddRecipes", item.AddRecipes);
            }
            foreach (var global in ModContent.GetContent<GlobalItem>())
            {
                if (global.Mod != mod) continue;
                Safe.Run($"{mod.Name}/{global.Name}.AddRecipes", global.AddRecipes);
            }
        }
    }

    private static void SystemStaticDefaults()
    {
        foreach (var mod in LoaderHost.Current.Mods.Mods) SystemLoader.SetupContent(mod);
    }

    private static void SystemPostSetupContent()
    {
        foreach (var mod in LoaderHost.Current.Mods.Mods) SystemLoader.PostSetupContent(mod);
    }

    private static void SystemRecipeGroups()
    {
        foreach (var mod in LoaderHost.Current.Mods.Mods) SystemLoader.AddRecipeGroups(mod);
    }

    private static void SystemPostAddRecipes()
    {
        foreach (var mod in LoaderHost.Current.Mods.Mods) SystemLoader.PostAddRecipes(mod);
    }

    private static void GlobalRecipeGroups()
    {
        foreach (var global in GlobalNpcRegistry.All) Safe.Run(global.Name + ".AddRecipeGroups", global.AddRecipeGroups);
    }

    private static void GlobalNpcTasks()
    {
        foreach (var global in GlobalNpcRegistry.All)
        {
            Safe.Run(global.Name + ".SetStaticDefaults", global.SetStaticDefaults);
            Safe.Run(global.Name + ".AddRecipes", global.AddRecipes);
        }
    }

    private sealed class HookStep(string name, InstallStage stage, HookId hook, Action<Mod> invoke) : IInstallStep
    {
        public string Name { get; } = name;

        public InstallStage Stage { get; } = stage;

        public bool Run()
        {
            var mods = LoaderHost.Current.Hooks.Channel(hook);
            var timed = HookProfile.Enabled;
            for (var i = 0; i < mods.Length; i++)
            {
                var start = timed ? Stopwatch.GetTimestamp() : 0;
                RunOne(mods[i]);
                if (timed) HookProfile.Record((int)hook, mods[i].Index, HookProfile.TicksToNs(Stopwatch.GetTimestamp() - start));
            }
            return true;
        }

        private void RunOne(Mod mod) => Safe.Run(mod.Name + "." + hook, () => invoke(mod));
    }

    /// <summary>扩"内容装配之前必须扩好"的表；谁用到谁声明，框架统一执行。</summary>
    private sealed class GrowTablesStep(LoaderHost host) : IInstallStep
    {
        public string Name => "扩表";

        public InstallStage Stage => InstallStage.Layout;

        public bool Run() => TableMaintenance.PumpEarly(host.Log.Info);
    }
}
