using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.SaveData;
using Terraria.Graphics;
using Terraria.IO;
using Terraria.Localization;
using Terraria.Map;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 系统内容基类：一个 Mod 可以有多个实例，每个实例独立接收装载、世界、更新、绘制与网络钩子。
/// 生命线：注册占位 → SetStaticDefaults（表定型后）→ OnWorldLoad / 每帧钩子 → OnWorldUnload；
/// 世界数据按 "<c>Mod.Uid/类型名</c>" 键存进世界旁车文件，网络包按同一键寻址。
/// </summary>
public abstract partial class ModSystem : ModType
{
    public virtual void OnModLoad() { }

    public virtual void OnModUnload() { }

    public virtual void PostSetupContent() { }

    public virtual void OnLocalizationsLoaded() { }

    public virtual void ResizeArrays() { }

    public virtual void AddRecipeGroups() { }

    public virtual void AddRecipes() { }

    public virtual void PostAddRecipes() { }

    public virtual void PostSetupRecipes() { }

    public virtual void OnWorldLoad() { }

    public virtual void OnWorldUnload() { }

    public virtual void ClearWorld() { }

    public virtual void PreSaveAndQuit() { }

    public virtual void SaveWorldData(TagCompound tag) { }

    public virtual void LoadWorldData(TagCompound tag) { }

    public virtual void SaveWorldHeader(TagCompound tag) { }

    public virtual bool CanWorldBePlayed(PlayerFileData playerData, WorldFileData worldFileData) => true;

    public virtual string WorldCanBePlayedRejectionMessage(PlayerFileData playerData, WorldFileData worldData) =>
        "The selected character " + playerData.Name + " cannot enter the selected world " + worldData.Name;

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

    public virtual void PreUpdateTime() { }

    public virtual void PostUpdateTime() { }

    public virtual void PreUpdateWorld() { }

    public virtual void PostUpdateWorld() { }

    public virtual void PreUpdateInvasions() { }

    public virtual void PostUpdateInvasions() { }

    public virtual void PostUpdateEverything() { }

    public virtual void UpdateUI(GameTime gameTime) { }

    public virtual void PostUpdateInput() { }

    public virtual void ModifyTimeRate(ref double timeRate, ref double tileUpdateRate, ref double eventUpdateRate) { }

    public virtual void ResetNearbyTileEffects() { }

    public virtual void TileCountsAvailable(ReadOnlySpan<int> tileCounts) { }

    public virtual void ModifyScreenPosition() { }

    public virtual void ModifyTransformMatrix(ref SpriteViewMatrix Transform) { }

    public virtual void ModifySunLightColor(ref Color tileColor, ref Color backgroundColor) { }

    public virtual void ModifyLightingBrightness(ref float scale) { }

    public virtual void PostDrawTiles() { }

    public virtual void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) { }

    public virtual void PostDrawInterface(SpriteBatch spriteBatch) { }

    public virtual void PreDrawMapIconOverlay(IReadOnlyList<IMapLayer> layers, MapOverlayDrawContext mapOverlayDrawContext) { }

    public virtual void PostDrawFullscreenMap(ref string mouseText) { }

    public virtual void NetSend(System.IO.BinaryWriter writer) { }

    public virtual void NetReceive(System.IO.BinaryReader reader) { }

    public virtual bool HijackGetData(ref byte messageType, ref System.IO.BinaryReader reader, int playerNumber) => false;

    public virtual bool HijackSendData(int whoAmI, int msgType, int remoteClient, int ignoreClient, NetworkText text,
                                       int number, float number2, float number3, float number4, int number5,
                                       int number6, int number7) => false;
}
