using Pylon.ModLoader.Textures;

namespace Pylon.ModLoader;

/// <summary>
/// 安装计划：唯一的"装什么、按什么顺序装"的事实来源。
/// 步骤按阶段线性排列；要增删内容装配，只改这里。
/// </summary>
internal static class InstallPlan
{
    public static IInstallStep[] Build(LoaderHost host)
    {
        var steps = new List<IInstallStep>(24)
        {
            // 布表：先扩内容表，后面所有写槽都以新长度为前提。
            new GrowTablesStep(host),

            // 名字接管：游戏悬停取名字走 Lang.GetItemNameValue，原版对 mod 类型返回空串 →
            // 空名字让 MouseText 直接跳过整个 tooltip（D-015 根因）。必须先于一切内容安装。
            new Step("名字接管", InstallStage.Static, () => ItemNamePatch.Install(host.Log)),

            // 静态：依赖表长或必须先于写槽的初始化。
            new Step("buff 静态默认值", InstallStage.Static, InstallSteps.BuffStaticDefaults),
            new Step("buffImmune 扩长", InstallStage.Static, GrowBuffImmune),
            new Step("坐骑安装", InstallStage.Static, () => MountTable.Build(host.Log)),
            new Step("前缀安装", InstallStage.Static, () => PrefixTable.Build(host.Log)),
            new Step("稀有度安装", InstallStage.Static, () => RarityTable.StaticDefaults(host.Log)),
            new Step("方块实体安装", InstallStage.Static, () => TileEntityPipeline.Instance.Build()),
            new Step("血腥粒子安装", InstallStage.Static, () => GorePipeline.Instance.Build()),
            new Step("装备贴图安装", InstallStage.Static, () => EquipPipeline.Instance.Build()),
            new Step("成就安装", InstallStage.Static, () => AchievementPipeline.Instance.Build()),
            new Step("尘土安装", InstallStage.Static, () => DustPipeline.Instance.Build()),
            new Step("树安装", InstallStage.Static, () => TreePipeline.Instance.Build()),
            new Step("伤害类别安装", InstallStage.Static, () => DamageClassPipeline.Instance.Build()),
            // 云 / 发型 / 水样式要先扩表再写贴图（ResizeArrays → SetupTextures）。
            // 它们原先排在写贴图之后，表还没扩写槽就全部失败，主菜单第一帧遍历这些表就空指针。
            new Step("云安装", InstallStage.Static, () => CloudPipeline.Instance.Build()),
            new Step("发型安装", InstallStage.Static, () => HairPipeline.Instance.Build()),
            new Step("水样式安装", InstallStage.Static, () => WaterPipeline.Instance.Build()),

            // 表面：上传贴图 → 写表槽与名字 / 介绍。
            new Step("贴图上传", InstallStage.Surface, () => InstallSteps.LoadTextures(host)),
            new Step("写贴图与文本", InstallStage.Surface, () => InstallSteps.WriteTextures(host)),
            // 水/瀑布贴图必须等上传完再写（Static 阶段 Texture 还是 null），和作者的 SetupContent 时机一致。
            new Step("水贴图安装", InstallStage.Surface, () => WaterPipeline.Instance.SetupTextures()),

            // 样例：ContentSamples 与静态默认值。
            new Step("物品样例", InstallStage.Samples, InstallSteps.RegisterItemSamples),
            new Step("墙壁安装", InstallStage.Samples, () => WallPipeline.Instance.Build()),
            new Step("方块安装", InstallStage.Samples, () => TilePipeline.Instance.Build()),
            new Step("弹幕模板", InstallStage.Samples, InstallSteps.ProjectileTemplates),
            new Step("NPC 静态默认值", InstallStage.Samples, InstallSteps.NpcStaticDefaults),
            new Step("NPC 样例", InstallStage.Samples, InstallSteps.RegisterNpcSamples),
            new Step("城镇 NPC 商店", InstallStage.Samples, () => NpcPipeline.Instance.SetupShops(host.Log)),
            new Step("旗帜安装", InstallStage.Samples, () => BannerPipeline.Instance.Build()),
            new Step("表情气泡安装", InstallStage.Static, () => EmoteBubblePipeline.Instance.Build()),
            new Step("生物群系安装", InstallStage.Static, () => BiomePipeline.Instance.Build()),
            new Step("场景效果安装", InstallStage.Static, () => SceneEffectPipeline.Instance.Build()),
            new Step("血条安装", InstallStage.Static, () => BossBarPipeline.Instance.Build()),
            new Step("晶塔安装", InstallStage.Static, () => PylonPipeline.Instance.Build()),
            new Step("商店安装", InstallStage.Static, () => ShopPipeline.Instance.Build()),
            new Step("掉落安装", InstallStage.Mods, () => LootPipeline.Instance.Build()),
            new Step("命令安装", InstallStage.Static, () => CommandPipeline.Instance.Build()),
            new Step("菜单主题安装", InstallStage.Static, () => MenuPipeline.Instance.Build()),
            new Step("资源覆盖层安装", InstallStage.Static, () => ResourceOverlayPipeline.Instance.Build()),
            new Step("玩家绘制层安装", InstallStage.Static, () => PlayerDrawLayerPipeline.Instance.Build()),
            new Step("背景安装", InstallStage.Static, () => BackgroundPipeline.Instance.Build()),

            // Mod 侧回调：数组已定长，作者在这里做跨内容引用。
            new HookStep("ResizeArrays", InstallStage.Mods, HookId.ResizeArrays, static mod => mod.ResizeArrays()),
            new HookStep("Mod.SetupContent", InstallStage.Mods, HookId.SetupContent, static mod => mod.SetupContent()),
            new HookStep("Mod.AddRecipeGroups", InstallStage.Mods, HookId.AddRecipeGroups, static mod => mod.AddRecipeGroups()),
            new Step("GlobalNPC 配方组", InstallStage.Mods, GlobalRecipeGroups),
            new HookStep("Mod.PostSetupContent", InstallStage.Mods, HookId.PostSetupContent, static mod => mod.PostSetupContent()),
            new Step("内容 PostSetupContent", InstallStage.Mods, () => PostSetupContent()),
            new Step("提示重建开关", InstallStage.Mods, () => TooltipPipeline.Instance.Build()),
            new Step("GlobalNPC 静态与配方", InstallStage.Mods, GlobalNpcTasks),

            // 配方：配方 → 作者收尾 → 派生表重建。
            new HookStep("Mod.AddRecipes", InstallStage.Recipes, HookId.AddRecipes, static mod => mod.AddRecipes()),
            new HookStep("Mod.PostAddRecipes", InstallStage.Recipes, HookId.PostAddRecipes, static mod => mod.PostAddRecipes()),
            new Step("配方收尾", InstallStage.Recipes, () => RecipeTable.Finish(LoaderHost.Current.Log)),
            new HookStep("Mod.PostSetupRecipes", InstallStage.Recipes, HookId.PostSetupRecipes, static mod => mod.PostSetupRecipes()),
        };

        return [.. steps];
    }

    private static void GrowBuffImmune() => BuffPipeline.Instance.GrowImmuneArrays();

    /// <summary>每个内容自己的 PostSetupContent（作者写跨内容引用就在这里）。</summary>
    private static void PostSetupContent()
    {
        foreach (var content in ModContent.GetContent<ModType>())
        {
            switch (content)
            {
                case ModItem item: Safe.Run(item.Name + ".PostSetupContent", item.PostSetupContent); break;
                case ModNpc npc: Safe.Run(npc.Name + ".PostSetupContent", npc.PostSetupContent); break;
                case ModBuff buff: Safe.Run(buff.Name + ".PostSetupContent", buff.PostSetupContent); break;
                case ModProjectile projectile: Safe.Run(projectile.Name + ".PostSetupContent", projectile.PostSetupContent); break;
                case ModTile tile: Safe.Run(tile.Name + ".PostSetupContent", tile.PostSetupContent); break;
                case ModWall wall: Safe.Run(wall.Name + ".PostSetupContent", wall.PostSetupContent); break;
                case GlobalBuff global: Safe.Run(global.Name + ".PostSetupContent", global.PostSetupContent); break;
                case GlobalNpc global: Safe.Run(global.Name + ".PostSetupContent", global.PostSetupContent); break;
                case GlobalTile global: Safe.Run(global.Name + ".PostSetupContent", global.PostSetupContent); break;
                case GlobalRecipe global: Safe.Run(global.Name + ".PostSetupContent", global.PostSetupContent); break;
                case GlobalBossBar global: Safe.Run(global.Name + ".PostSetupContent", global.PostSetupContent); break;
            }
        }
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
            Safe.Run(global.Name + ".PostSetupContent", global.PostSetupContent);
        }
    }

    /// <summary>对一个钩子的全部订阅者依次调用；空通道直接返回。</summary>
    private sealed class HookStep(string name, InstallStage stage, HookId hook, Action<Mod> invoke) : IInstallStep
    {
        public string Name { get; } = name;

        public InstallStage Stage { get; } = stage;

        public bool Run()
        {
            var mods = LoaderHost.Current.Hooks.Channel(hook);
            for (var i = 0; i < mods.Length; i++) RunOne(mods[i]);
            return true;
        }

        private void RunOne(Mod mod) => Safe.Run(mod.Name + "." + hook, () => invoke(mod));
    }

    /// <summary>扩内容表：每帧扩一批，全部扩完才让后面的步骤写槽。</summary>
    private sealed class GrowTablesStep(LoaderHost host) : IInstallStep
    {
        private static readonly TypeTables.TableKind[] Order =
        [
            TypeTables.TableKind.Item, TypeTables.TableKind.Projectile,
            TypeTables.TableKind.Npc, TypeTables.TableKind.Buff,
            TypeTables.TableKind.Tile, TypeTables.TableKind.Wall,
        ];

        private const int BatchBudget = 256;

        private readonly int[] _grown = new int[Order.Length];
        private TypeTables.TableGrower? _grower;
        private int _index;

        public string Name => "扩表";

        public InstallStage Stage => InstallStage.Layout;

        public bool Run()
        {
            _grower ??= new TypeTables.TableGrower(Order[_index], ModContent.NextType(KindOf(Order[_index])));

            _grown[_index] += _grower.NextBatch(BatchBudget, host.Log.Info);

            if (!_grower.Done) return false;

            _grower = null;
            if (++_index < Order.Length) return false;

            host.Log.Info($"内容表扩容：物品 {_grown[0]}、弹幕 {_grown[1]}、NPC {_grown[2]}、" +
                          $"Buff {_grown[3]}、方块 {_grown[4]}、墙壁 {_grown[5]}");
            return true;
        }

        private static ContentKind KindOf(TypeTables.TableKind kind) => kind switch
        {
            TypeTables.TableKind.Item => ContentKind.Item,
            TypeTables.TableKind.Projectile => ContentKind.Projectile,
            TypeTables.TableKind.Npc => ContentKind.Npc,
            TypeTables.TableKind.Buff => ContentKind.Buff,
            TypeTables.TableKind.Tile => ContentKind.Tile,
            _ => ContentKind.Wall,
        };
    }
}
