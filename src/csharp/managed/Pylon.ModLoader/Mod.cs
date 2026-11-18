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

/// <summary>
/// Mod 入口。宿主实例化后调用 <see cref="IPylonMod.Load"/>：注册内容、装配模块、再执行作者代码。
/// 内容注入游戏由 <see cref="LoaderHost"/> 的安装管线统一推进。
/// </summary>
public abstract class Mod : IPylonMod
{
    private ModAssets? _assets;

    public string Uid { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public Version Version { get; private set; } = new(0, 0);

    internal string Directory { get; private set; } = string.Empty;

    internal string DataDirectory { get; private set; } = string.Empty;

    public string Folder => Directory;

    public bool ContentAutoloadingEnabled { get; init; } = true;

    public bool BackgroundAutoloadingEnabled { get; init; } = true;

    public bool CloudAutoloadingEnabled { get; init; } = true;

    public ModLogger Logger { get; private set; } = null!;

    public ModAssets Assets => _assets ??= new ModAssets(this);

    internal List<ModType> Types { get; set; } = [];

    internal HookMask Plan { get; set; }

    internal int Index { get; set; }

    protected void RegisterImGuiTab(IModImGuiTab tab) => UI.ModUI.RegisterImGuiTab(tab);

    internal Asset<Texture2D> LoadAsset(string relativePath) => Assets.Request<Texture2D>(relativePath);

    internal Texture2D LoadTexture(string relativePath) => LoadAsset(relativePath).Value;

    public IEnumerable<ModType> GetContent() => ModContent.ContentOf<ModType>(this);

    public IEnumerable<T> GetContent<T>() where T : class => ModContent.ContentOf<T>(this);

    public virtual object? Call(params object[] args) => null;

    public ModPacket GetPacket(int capacity = 256) => new(this, capacity);

    public virtual void HandlePacket(System.IO.BinaryReader reader, int whoAmI) { }

    public virtual bool HijackGetData(ref byte messageType, ref Il2CppSystem.IO.BinaryReader reader, int playerNumber) => false;

    public virtual bool HijackSendData(int whoAmI, int msgType, int remoteClient, int ignoreClient, NetworkText text,
                                       int number, float number2, float number3, float number4, int number5,
                                       int number6, int number7) => false;

    public int AddBossHeadTexture(string texture, int npcType = -1) => NpcHeadHooks.AddBossHead(this, texture, npcType);

    public int AddNPCHeadTexture(int npcType, string texture) => NpcHeadHooks.AddTownHead(this, npcType, texture);

    public bool AddContent(ModType instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.Mod is not null && instance.Mod != this)
            throw new InvalidOperationException($"{instance.GetType().FullName} 已属于 {instance.Mod.Name}");

        foreach (var existing in Types)
        {
            if (existing.GetType() == instance.GetType()) return false;
        }

        instance.Mod = this;
        instance.Plan = HookScan.Of(instance.GetType());
        Types.Add(instance);
        RegistrationPipeline.RegisterGlobal(instance);
        LoaderHost.Current.Content.Register(this, [instance]);
        return true;
    }

    public bool AddContent<T>() where T : ModType, new() => AddContent(new T());

    /// <summary>以这个编号为结果新开一份配方（链式填完 Register）。</summary>
    public ModRecipe CreateRecipe(int result, int amount = 1) => new ModRecipe { Mod = this }.SetResult(result, amount);

    /// <summary>按名字找本 Mod 的内容实例；没有就抛。</summary>
    public T Find<T>(string name) where T : ModType => ModContent.Find<T>(Name + "/" + name);

    /// <summary>按名字找本 Mod 的内容实例；没有返回 false。</summary>
    public bool TryFind<T>(string name, out T? value) where T : ModType =>
        ModContent.TryFind(Name + "/" + name, out value);

    /// <summary>本地化键：Mods.&lt;Mod&gt;.&lt;suffix&gt;。</summary>
    public string GetLocalizationKey(string suffix) => $"Mods.{Name}.{suffix}";

    public LocalizedText GetLocalization(string suffix, Func<string>? makeDefaultValue = null)
    {
        var key = GetLocalizationKey(suffix);
        var text = new LocalizedText(key, makeDefaultValue?.Invoke() ?? suffix);
        LocalizationKeys.Register(key, text);
        return text;
    }

    /// <summary>Mod 目录下有没有这个文件。</summary>
    public bool FileExists(string name) => File.Exists(Path.Combine(Directory, name.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>读 Mod 目录下的文件字节。</summary>
    public byte[] GetFileBytes(string name) => File.ReadAllBytes(Path.Combine(Directory, name.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>打开 Mod 目录下的文件流。</summary>
    public Stream GetFileStream(string name, bool newFileStream = false) =>
        File.OpenRead(Path.Combine(Directory, name.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Mod 目录下全部文件（相对路径，'/' 分隔）。</summary>
    public List<string> GetFileNames()
    {
        var result = new List<string>();
        if (!System.IO.Directory.Exists(Directory)) return result;

        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.AllDirectories))
            result.Add(Path.GetRelativePath(Directory, path).Replace(Path.DirectorySeparatorChar, '/'));
        return result;
    }

    public virtual void Unload() { }

    public virtual void Load() { }

    public virtual void OnModLoad() { }

    public virtual void OnModUnload() { }

    public virtual void OnLocalizationsLoaded() { }

    public virtual void ResizeArrays() { }

    public virtual void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) { }

    public virtual void PostDrawInterface(SpriteBatch spriteBatch) { }

    public virtual void PostDrawTiles() { }

    public virtual void SetupContent() { }

    public virtual void AddRecipeGroups() { }

    public virtual void AddRecipes() { }

    public virtual void PostAddRecipes() { }

    public virtual void PostSetupRecipes() { }

    public virtual void PostSetupContent() { }

    public virtual void OnWorldLoad() { }

    public virtual void LoadWorldData(TagCompound tag) { }

    public virtual void PostWorldLoad() { }

    public virtual void SaveWorldData(TagCompound tag) { }

    public virtual void SaveWorldHeader(TagCompound tag) { }

    public virtual bool CanWorldBePlayed(PlayerFileData playerData, WorldFileData worldData) => true;

    public virtual string WorldCanBePlayedRejectionMessage(PlayerFileData playerData, WorldFileData worldData) =>
        "人物 " + playerData.Name + " 不能进入世界 " + worldData.Name + "。";

    public virtual void PreWorldGen() { }

    public virtual void ModifyWorldGenTasks(List<GenPass> tasks) { }

    public virtual void PostWorldGen() { }

    public virtual void ModifyHardmodeTasks(List<GenPass> tasks) { }

    public virtual void ClearWorld() { }

    public virtual void PreSaveAndQuit() { }

    public virtual void OnWorldUnload() { }

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

    public virtual bool? ConsumeBait(ref int baitTypeUsed) => null;

    public virtual void GetDyeTraderReward(ref List<int> pool) { }

    public virtual void ModifyTimeRate(ref double timeRate, ref double tileUpdateRate, ref double eventUpdateRate) { }

    public virtual void ModifySunLightColor(ref Color tileColor, ref Color backgroundColor) { }

    public virtual void ModifyLightingBrightness(ref float scale) { }

    public virtual void ModifyScreenPosition() { }

    public virtual void ModifyTransformMatrix(ref SpriteViewMatrix transform) { }

    public virtual void ResetNearbyTileEffects() { }

    public virtual void TileCountsAvailable(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> tileCounts) { }

    void IPylonMod.Load(IModContext context)
    {
        Uid = context.Id;
        Name = GetType().Assembly.GetName().Name ?? GetType().Name;
        DisplayName = context.Id;
        Version = System.Version.TryParse(context.Version, out var version) ? version : new System.Version(0, 0);
        Directory = context.Directory;
        DataDirectory = ModContent.DataDirectoryFor(context.Directory, context.Id);
        Logger = new ModLogger(context);

        LoaderHost.Current.Register(this);

        ModConfigLoader.ModConfigLog = Logger.Error;
        ModConfigLoader.ModConfigTrace = Logger.Info;
        LoadConfigs();

        Logger.Info($"加载：{Types.Count} 个内容");
        Load();
        Safe.Run(Name + ".OnModLoad", OnModLoad);
    }

    void IPylonMod.OnContentReady(IModContext context)
    {
        Logger.Info("内容就绪：进入晚阶段");

        UI.ModUI.EnsureInitialized(Logger);

        LoaderHost.Current.ContentReady();
        Logger.Info($"内容已登记：{GameContent.Pending} 个自定义内容等待注入");
    }

    private void LoadConfigs()
    {
        foreach (var type in Types)
        {
            if (type is not ModConfig config) continue;
            Safe.Run($"{Name}.{type.Name}.Config", () => ModConfigLoader.Add(config));
        }
    }
}
