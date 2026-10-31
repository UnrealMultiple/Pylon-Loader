using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.Mod;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria.Graphics;
using Terraria.IO;
using Terraria.Localization;
using Terraria.WorldBuilding;

namespace Pylon.ModLoader;

/// <summary>Mod 入口：作者的类派生自它。宿主创建实例后先 <see cref="Load"/>，内容就绪后再 <see cref="OnContentReady"/>。</summary>
public abstract class Mod : IPylonMod
{
    private readonly List<ModType> _types = [];

    public string Name { get; private set; } = string.Empty;

    public string Version { get; private set; } = string.Empty;

    public string Directory { get; private set; } = string.Empty;

    /// <summary>可写数据目录 Mods/_data/&lt;id&gt;：在 Mod 目录外，升级 Mod 不会清掉。</summary>
    public string DataDirectory { get; private set; } = string.Empty;

    public ModLogger Logger { get; private set; } = null!; // Load 时注入

    public IReadOnlyList<ModType> Types => _types;

    /// <summary>注册一个 ImGui 菜单页；等价于 context.RegisterImGuiTab。</summary>
    protected void RegisterImGuiTab(IModImGuiTab tab) => UI.ModUI.RegisterImGuiTab(tab);

    /// <summary>上传贴图并包成已加载的 Asset&lt;Texture2D&gt;（相对路径按 Mod 目录；只能游戏线程调）。</summary>
    public Asset<Texture2D> LoadAsset(string relativePath) => PylonTexture.LoadAsset(this, relativePath);

    /// <summary>上传贴图并拿游戏 Texture2D 本身（只能游戏线程调）。</summary>
    public Texture2D LoadTexture(string relativePath) => PylonTexture.LoadTexture(this, relativePath);

    // ===================== 生命周期 =====================

    /// <summary>早阶段：注册内容、读配置。此时内容已占号，但游戏还没就绪。</summary>
    public virtual void Load() { }

    public virtual void OnModLoad() { }

    /// <summary>正常退出或引擎关停时一次（被系统强杀不跑）。</summary>
    public virtual void OnModUnload() { }

    public virtual void OnLocalizationsLoaded() { }

    public virtual void ResizeArrays() { }

    /// <summary>晚阶段：游戏就绪后（游戏线程）。需要游戏表的钩子放这里。</summary>
    public virtual void SetupContent() { }

    public virtual void AddRecipeGroups() { }

    public virtual void AddRecipes() { }

    public virtual void PostAddRecipes() { }

    public virtual void PostSetupRecipes() { }

    public virtual void PostSetupContent() { }

    // ===================== 世界生命周期 =====================

    public virtual void OnWorldLoad() { }

    /// <summary>世界侧车数据（&lt;世界&gt;.wld.bl.json）读回来了；没有这个键也会给空 compound。</summary>
    public virtual void LoadWorldData(TagCompound tag) { }

    public virtual void PostWorldLoad() { }

    /// <summary>写世界侧车数据；空 compound 不会落盘。</summary>
    public virtual void SaveWorldData(TagCompound tag) { }

    public virtual void SaveWorldHeader(TagCompound tag) { }

    /// <summary>false：这对人物和世界不能进（选世界和加载时都会问）。</summary>
    public virtual bool CanWorldBePlayed(PlayerFileData playerData, WorldFileData worldData) => true;

    public virtual string WorldCanBePlayedRejectionMessage(PlayerFileData playerData, WorldFileData worldData) =>
        "O personagem " + playerData.Name + " não pode entrar no mundo " + worldData.Name + ".";

    public virtual void PreWorldGen() { }

    public virtual void ModifyWorldGenTasks(List<GenPass> tasks) { }

    public virtual void PostWorldGen() { }

    public virtual void ModifyHardmodeTasks(List<GenPass> tasks) { }

    /// <summary>进任何世界时、加载它之前（生成世界之前也跑）。</summary>
    public virtual void ClearWorld() { }

    /// <summary>点退出时、保存之前。</summary>
    public virtual void PreSaveAndQuit() { }

    public virtual void OnWorldUnload() { }

    // ===================== 每帧阶段 =====================

    public virtual void PreUpdateWorld() { }
    public virtual void PostUpdateWorld() { }
    public virtual void PreUpdateTime() { }
    public virtual void PostUpdateTime() { }
    public virtual void PostUpdateEverything() { }
    public virtual void UpdateUI(GameTime gameTime) { }
    public virtual void PostUpdateInput() { }
    public virtual void PreUpdateEntities() { }
    public virtual void PreUpdatePlayers() { }
    public virtual void PostUpdatePlayers() { }
    public virtual void PreUpdateNPCs() { }
    public virtual void PostUpdateNPCs() { }
    public virtual void PreUpdateGores() { }
    public virtual void PostUpdateGores() { }
    public virtual void PreUpdateProjectiles() { }
    public virtual void PostUpdateProjectiles() { }
    public virtual void PreUpdateItems() { }
    public virtual void PostUpdateItems() { }
    public virtual void PreUpdateDusts() { }
    public virtual void PostUpdateDusts() { }
    public virtual void PreUpdateInvasions() { }
    public virtual void PostUpdateInvasions() { }

    public virtual void ModifyTimeRate(ref double timeRate, ref double tileUpdateRate, ref double eventUpdateRate) { }

    public virtual void ModifySunLightColor(ref Color tileColor, ref Color backgroundColor) { }

    public virtual void ModifyLightingBrightness(ref float scale) { }

    public virtual void ModifyScreenPosition() { }

    public virtual void ModifyTransformMatrix(ref SpriteViewMatrix transform) { }

    public virtual void ResetNearbyTileEffects() { }

    public virtual void TileCountsAvailable(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> tileCounts) { }

    // ===================== 网络 =====================

    /// <summary>原生世界同步（消息 7）之后，把本 Mod 的世界数据写进信封。</summary>
    public virtual void NetSend(NetWriter writer) { }

    public virtual void NetReceive(NetReader reader) { }

    /// <summary>原生包解出来之前；messageType 和 reader 是可写引用，任一 true 取消整个包。</summary>
    public virtual bool HijackGetData(ref byte messageType, ref Il2CppSystem.IO.BinaryReader reader, int playerNumber) => false;

    /// <summary>发出去之前；任一 true 取消这次发送（连实体数据包一起）。</summary>
    public virtual bool HijackSendData(int whoAmI, int msgType, int remoteClient, int ignoreClient, NetworkText text,
                                       int number, float number2, float number3, float number4, int number5,
                                       int number6, int number7) => false;

    /// <summary>注册一张 Boss 头像贴图（第二形态用）；返回槽位，文件缺失时 -1。</summary>
    public virtual int AddBossHeadTexture(string texture, int npcType = -1) => -1;

    /// <summary>本 Mod 已注册、且可赋给 T 的全部内容实例。</summary>
    public List<T> GetContent<T>() where T : ModType => ModContent.GetContent<T>().FindAll(instance => instance.Mod == this);

    /// <summary>建一个发给本 Mod 的自定义网络包。</summary>
    public ModPacket GetPacket() => new(this);

    /// <summary>收到本 Mod 的包时（包的数据在 reader 里）。</summary>
    public virtual void HandlePacket(NetReader reader, int whoAmI) { }

    /// <summary>被别的 Mod 调用；约定首参是命令名，未知命令返回 null。</summary>
    public virtual object? Call(params object[] args) => null;

    // ===================== IPylonMod =====================

    void IPylonMod.Load(IModContext context)
    {
        Name = context.Id;
        Version = context.Version;
        Directory = context.Directory;
        DataDirectory = ModContent.DataDirectoryFor(context.Directory, context.Id);
        Logger = new ModLogger(context);

        DiscoverTypes();
        ModContent.Register(this, _types);
        ModLoader.Register(this);
        LoaderBootstrap.Install(Logger);

        ModLifecycle.Register(this);
        ModConfigLoader.ModConfigLog = Logger.Error;
        ModConfigLoader.ModConfigTrace = Logger.Info;
        LoadConfigs();

        Logger.Info($"加载：{_types.Count} 个内容");
        Load();
        Safe.Run(Name + ".OnModLoad", OnModLoad);
    }

    void IPylonMod.OnContentReady(IModContext context)
    {
        Logger.Info("内容就绪：进入晚阶段");

        UI.ModUI.EnsureInitialized(Logger); // UI 是 Unity 对象，只能在这时建
        ItemSetDefaultsPatch.Error = Logger.Warn;
        ProjectileSetDefaultsPatch.Error = Logger.Warn;
        NpcSetDefaultsPatch.Error = Logger.Warn;

        ContentInstaller.InstallTextures(this, _types);
        ProjectileLoader.WarnUnwired(this);

        // 游戏原版只认原版物品，悬停 Mod 物品会空引用；接管 tooltip 绘制。
        Tooltips.TooltipLoader.Install(Logger);
        GameContent.Installed = true;
        Logger.Info($"内容注入完成：{GameContent.Pending} 个自定义内容已对游戏可见");
    }

    /// <summary>找本程序集的 ModType 派生类，按全名序数排序后实例化（顺序即占号顺序，必须跨设备一致）。</summary>
    private void DiscoverTypes()
    {
        var assembly = GetType().Assembly;
        var candidates = FindTypes(assembly)
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ModType).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        foreach (var type in candidates)
        {
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                Logger.Warn($"{type.FullName} 没有公开无参构造函数，跳过");
                continue;
            }

            var instance = (ModType)Activator.CreateInstance(type)!;
            instance.Mod = this;
            instance.Plan = OverridePlan.Build(type, typeof(ModType));
            _types.Add(instance);
            RegisterGlobal(instance);
        }

        // ModPlayer 不是 ModType（不占 ID），单独发现；每玩家实例由 PlayerLoader 懒建。
        foreach (var type in FindTypes(assembly))
        {
            if (type is not { IsClass: true, IsAbstract: false } || !typeof(ModPlayer).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;

            var instance = (ModPlayer)Activator.CreateInstance(type)!;
            instance.Mod = this;
            PlayerLoader.Register(instance);
        }
    }

    private static void RegisterGlobal(ModType instance)
    {
        switch (instance)
        {
            case GlobalItem globalItem: GlobalItemRegistry.Register(globalItem); break;
            case GlobalProjectile globalProjectile: GlobalProjectileRegistry.Register(globalProjectile); break;
            case GlobalNpc globalNpc: GlobalNpcRegistry.Register(globalNpc); break;
            case GlobalBuff globalBuff: GlobalBuffRegistry.Register(globalBuff); break;
            case GlobalTile globalTile: GlobalTileRegistry.Register(globalTile); break;
        }
    }

    private void LoadConfigs()
    {
        foreach (var type in _types)
        {
            if (type is not ModConfig config) continue;
            Safe.Run($"{Name}.{type.Name}.Config", () => ModConfigLoader.Add(config));
        }
    }

    /// <summary>GetTypes 的容错版：依赖缺失时 ReflectionTypeLoadException 会带走全部类型。</summary>
    private static IEnumerable<Type> FindTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }
}
