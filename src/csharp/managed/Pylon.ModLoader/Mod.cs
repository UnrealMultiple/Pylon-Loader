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
    public string Name { get; private set; } = string.Empty;

    public string Version { get; private set; } = string.Empty;

    public string Directory { get; private set; } = string.Empty;

    /// <summary>可写数据目录 Mods/_data/&lt;id&gt;：升级 Mod 不会清掉。</summary>
    public string DataDirectory { get; private set; } = string.Empty;

    public ModLogger Logger { get; private set; } = null!;

    /// <summary>已注册的内容，按发号顺序。</summary>
    public IReadOnlyList<ModType> Types { get; internal set; } = [];

    /// <summary>覆写了哪些生命周期钩子；注册时算一次，分派靠它。</summary>
    internal HookMask Plan { get; set; }

    /// <summary>加载顺序里的位置，与内容发号绑定。</summary>
    internal int Index { get; set; }

    protected void RegisterImGuiTab(IModImGuiTab tab) => UI.ModUI.RegisterImGuiTab(tab);

    public Asset<Texture2D> LoadAsset(string relativePath) => PylonTexture.LoadAsset(this, relativePath);

    public Texture2D LoadTexture(string relativePath) => PylonTexture.LoadTexture(this, relativePath);

    /// <summary>本 Mod 已注册、可赋给 T 的内容。</summary>
    public List<T> GetContent<T>() where T : ModType => ModContent.GetContent<T>().FindAll(instance => instance.Mod == this);

    public virtual object? Call(params object[] args) => null;

    public ModPacket GetPacket() => new(this);

    public virtual void HandlePacket(NetReader reader, int whoAmI) { }

    public virtual void NetSend(NetWriter writer) { }

    public virtual void NetReceive(NetReader reader) { }

    public virtual bool HijackGetData(ref byte messageType, ref Il2CppSystem.IO.BinaryReader reader, int playerNumber) => false;

    public virtual bool HijackSendData(int whoAmI, int msgType, int remoteClient, int ignoreClient, NetworkText text,
                                       int number, float number2, float number3, float number4, int number5,
                                       int number6, int number7) => false;

    public virtual int AddBossHeadTexture(string texture, int npcType = -1) => -1;

    // ===================== 加载期 =====================

    /// <summary>内容已占号、游戏还没就绪；作者的注册代码放这里。</summary>
    public virtual void Load() { }

    public virtual void OnModLoad() { }

    public virtual void OnModUnload() { }

    public virtual void OnLocalizationsLoaded() { }

    // ===================== 安装期（游戏线程，表已定型） =====================

    public virtual void ResizeArrays() { }

    // ===================== 系统时机（界面层 / 绘制） =====================

    /// <summary>
    /// 改界面层序列：在这份列表里增删、换序，把某层的 <see cref="GameInterfaceLayer.Active"/> 置 false 即隐藏。
    /// 每帧调用一次；序列没变时管线不会重建游戏侧列表。
    /// </summary>
    public virtual void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) { }

    /// <summary>整块界面画完、鼠标提示之前。</summary>
    public virtual void PostDrawInterface(SpriteBatch spriteBatch) { }

    /// <summary>世界里的方块与投影都画完、界面之前。</summary>
    public virtual void PostDrawTiles() { }

    public virtual void SetupContent() { }

    public virtual void AddRecipeGroups() { }

    public virtual void AddRecipes() { }

    public virtual void PostAddRecipes() { }

    public virtual void PostSetupRecipes() { }

    public virtual void PostSetupContent() { }

    // ===================== 世界 =====================

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

    // ===================== 每帧 =====================

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

    // ===================== IPylonMod =====================

    void IPylonMod.Load(IModContext context)
    {
        Name = context.Id;
        Version = context.Version;
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

        ProjectilePipeline.Instance.WarnUnwired(this);

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
