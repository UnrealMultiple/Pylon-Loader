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

    public string DataDirectory { get; private set; } = string.Empty;

    public ModLogger Logger { get; private set; } = null!;

    public IReadOnlyList<ModType> Types { get; internal set; } = [];

    internal HookMask Plan { get; set; }

    internal int Index { get; set; }

    protected void RegisterImGuiTab(IModImGuiTab tab) => UI.ModUI.RegisterImGuiTab(tab);

    public Asset<Texture2D> LoadAsset(string relativePath) => PylonTexture.LoadAsset(this, relativePath);

    public Texture2D LoadTexture(string relativePath) => PylonTexture.LoadTexture(this, relativePath);

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
