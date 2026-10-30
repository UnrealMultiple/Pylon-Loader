using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.Mod;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria.Graphics;
using Terraria.IO;
using Terraria.Localization;
using Terraria.WorldBuilding;

namespace Pylon.ModLoader
{
    /// <summary>Mod 入口：作者的类派生自它。宿主建实例 → Load（早）→ OnContentReady（晚）。</summary>
    public abstract class Mod : IPylonMod
    {
        private readonly List<ModType> _types = new();

        public string Name { get; private set; }
        public string Version { get; private set; }
        public string Directory { get; private set; }

        /// <summary>可写数据目录：Mods/_data/&lt;id&gt;（在 Mod 目录外，升级 Mod 不会清掉）。</summary>
        public string DataDirectory { get; private set; }

        public ModLogger Logger { get; private set; }
        public IReadOnlyList<ModType> Types => _types;

        /// <summary>注册一个 ImGui 菜单 Tab（标题进侧边栏，Draw 每帧在菜单内容区调用）；也可直接操作 context.RegisterImGuiTab。</summary>
        protected void RegisterImGuiTab(IModImGuiTab tab)
            => UI.ModUI.RegisterImGuiTab(tab);

        /// <summary>上传贴图并包成已加载的 Asset&lt;Texture2D&gt;（相对路径按本 Mod 目录；只能游戏线程调）。</summary>
        public Asset<Texture2D> LoadAsset(string relativePath) => PylonTexture.LoadAsset(this, relativePath);

        /// <summary>上传贴图并拿游戏 Texture2D 本身（只能游戏线程调）。</summary>
        public Texture2D LoadTexture(string relativePath) => PylonTexture.LoadTexture(this, relativePath);

        /// <summary>早阶段：注册内容、读配置。内容已占号，但游戏还没就绪。</summary>
        public virtual void Load() { }

        /// <summary>Load 之后一次（原 ModSystem.OnModLoad）。</summary>
        public virtual void OnModLoad() { }

        /// <summary>正常退出或引擎关停时一次（被系统强杀不跑）。</summary>
        public virtual void OnModUnload() { }

        /// <summary>mod 的文本应用之后，首次与每次换语言各一次。</summary>
        public virtual void OnLocalizationsLoaded() { }

        /// <summary>原生内容就绪之后、mod 的 setup 阶段之前（数组扩容回调）。</summary>
        public virtual void ResizeArrays() { }

        /// <summary>晚阶段：游戏就绪后（游戏线程）。需要游戏表的、或装早了会打空的 hook 放这里。</summary>
        public virtual void SetupContent() { }

        /// <summary>注册配方组（内容就绪后、PostSetupContent 前一次）。</summary>
        public virtual void AddRecipeGroups() { }

        /// <summary>注册配方（内容就绪后、PostSetupContent 前一次，晚于 AddRecipeGroups）。</summary>
        public virtual void AddRecipes() { }

        /// <summary>AddRecipes 之后一次。</summary>
        public virtual void PostAddRecipes() { }

        /// <summary>所有配方、post 钩子和缓存重建之后一次。</summary>
        public virtual void PostSetupRecipes() { }

        public virtual void PostSetupContent() { }

        // ===================== 世界生命周期 =====================

        /// <summary>世界打开了；多人客户端是收到服务器世界时。</summary>
        public virtual void OnWorldLoad() { }

        /// <summary>世界侧车数据（&lt;世界&gt;.wld.bl.json）读回来了；没有这个键也会给空 compound。</summary>
        public virtual void LoadWorldData(TagCompound tag) { }

        /// <summary>世界数据读完（LoadWorldData）之后。</summary>
        public virtual void PostWorldLoad() { }

        /// <summary>写世界侧车数据；空 compound 不会落盘。</summary>
        public virtual void SaveWorldData(TagCompound tag) { }

        /// <summary>写世界头部侧车数据（选世界界面用）；Pylon 暂未接头部文件。</summary>
        public virtual void SaveWorldHeader(TagCompound tag) { }

        /// <summary>false：这对人物和世界不能进（选世界和加载时都问）。</summary>
        public virtual bool CanWorldBePlayed(PlayerFileData playerData, WorldFileData worldData) => true;

        /// <summary>拦住时给玩家看的话。</summary>
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

        /// <summary>离开世界之后。</summary>
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
        public virtual void TileCountsAvailable(Il2CppStructArray<int> tileCounts) { }

        // ===================== 网络 =====================

        /// <summary>原生世界同步（消息 7）之后，把本 Mod 的世界数据写进信封。</summary>
        public virtual void NetSend(NetWriter writer) { }

        /// <summary>收到本 Mod 的世界数据。</summary>
        public virtual void NetReceive(NetReader reader) { }

        /// <summary>原生包解出来之前；messageType 和 reader 是可写引用，任一 true 取消整个包。</summary>
        public virtual bool HijackGetData(ref byte messageType, ref Il2CppSystem.IO.BinaryReader reader, int playerNumber) => false;

        /// <summary>发出去之前；任一 true 取消这次发送（连实体数据包一起）。</summary>
        public virtual bool HijackSendData(int whoAmI, int msgType, int remoteClient, int ignoreClient, NetworkText text,
                                           int number, float number2, float number3, float number4, int number5, int number6, int number7) => false;

        /// <summary>注册一张 Boss 头像贴图（第二形态用）；返回槽位，文件缺失时 -1，槽位注册表在管线里。</summary>
        public virtual int AddBossHeadTexture(string texture, int npcType = -1) => -1;

        /// <summary>本 Mod 已注册、且可赋给 T 的全部内容实例。</summary>
        public List<T> GetContent<T>() where T : ModType =>
            ModContent.GetContent<T>().FindAll(instance => instance.Mod == this);

        /// <summary>建一个发给本 Mod 的自定义网络包。</summary>
        public ModPacket GetPacket() => new ModPacket(this);

        /// <summary>收到本 Mod 的包时（包的数据在 reader 里）。</summary>
        public virtual void HandlePacket(NetReader reader, int whoAmI) { }

        /// <summary>被别的 Mod 调用；约定首参是命令名，未知命令返回 null。</summary>
        public virtual object Call(params object[] args) => null;

        void IPylonMod.Load(IModContext context)
        {
            Name = context.Id;
            Version = context.Version;
            Directory = context.Directory;
            DataDirectory = ModContent.DataDirectoryFor(context.Directory, context.Id);
            Logger = new ModLogger(context);

            DiscoverTypes();
            ModContent.Register(this, _types);
            ModLoader.Register(this);   // 跨 Mod 查找（ModLoader.GetMod/HasMod/Mods）用
            // 注册期装内容管线的 hook（幂等：第一个 mod 装一次）
            ItemLoader.Install(Logger);
            ItemSetDefaultsPatch.Install(Logger);
            ProjectileLoader.Install(Logger);
            ProjectileSetDefaultsPatch.Install(Logger);
            NpcLoader.Install(Logger);
            NpcSetDefaultsPatch.Install(Logger);
            TownNpcLoader.Install(Logger);
            BuffLoader.Install(Logger);
            MountLoader.Install(Logger);
            PrefixLoader.Install(Logger);
            RarityLoader.Install(Logger);
            RecipeLoader.Install(Logger);
            TileLoader.Install(Logger);
            WallLoader.Install(Logger);
            PlayerLoader.Install(Logger);
            ModNet.Install(Logger);
            SoundLoader.Install(Logger);
            ModMusic.Configure(Logger);
            ModMusic.Install();
            ModLifecycle.Register(this);
            ModConfigLoader.ModConfigLog = Logger.Error;
            ModConfigLoader.ModConfigTrace = Logger.Info;
            foreach (var type in _types) // 模块里声明的 ModConfig：校验声明 + 读盘 + OnLoaded（对照 ConfigLoader.Add）
            {
                if (type is not ModConfig config) continue;
                try { ModConfigLoader.Add(config); }
                catch (Exception e) { Logger.Error($"{type.Name} 的配置没装上：{e.Message}"); }
            }
            Logger.Info($"加载：{_types.Count} 个内容");
            Load();
            Safe.Run(Name + ".OnModLoad", OnModLoad);
        }

        void IPylonMod.OnContentReady(IModContext context)
        {
            Logger.Info("内容就绪：进入晚阶段");
            UI.ModUI.EnsureInitialized(Logger);   // UI 是 Unity 对象，只能在这时建
            ItemSetDefaultsPatch.Error = m => Logger.Warn(m);   // 自定义物品 SetDefaults 分支取证
            ProjectileSetDefaultsPatch.Error = m => Logger.Warn(m);   // 自定义弹幕 SetDefaults 分支取证
            NpcSetDefaultsPatch.Error = m => Logger.Warn(m);   // 自定义 NPC SetDefaults 分支取证
            // 晚阶段：贴图缓存 + 表定型后扩表 + 写槽，然后回调 SetupContent 系（时机与扩表一致）。
            ContentInstaller.InstallTextures(this, _types);
            ProjectileLoader.WarnUnwired(this);   // 晚阶段才有 Safe.Log，警告放这里才看得见
            // mod 物品的 tooltip：游戏原版只认原版物品，悬停 mod 物品会空引用；接管绘制。
            Tooltips.TooltipLoader.Install(Logger);
            GameContent.Installed = true;
            Logger.Info($"内容注入完成：{GameContent.Pending} 个自定义内容已对游戏可见");
        }

        /// <summary>找本程序集的 ModType 派生类，按全名序数排序（顺序即占号顺序，必须跨设备一致）。</summary>
        private void DiscoverTypes()
        {
            var candidates = new List<Type>();
            foreach (var type in SafeGetTypes(GetType().Assembly))
            {
                if (type == null || !type.IsClass || type.IsAbstract) continue;
                if (!typeof(ModType).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    Logger.Warn($"{type.FullName} 没有公开无参构造函数，跳过");
                    continue;
                }
                candidates.Add(type);
            }

            candidates.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            foreach (var type in candidates)
            {
                var instance = (ModType)Activator.CreateInstance(type);
                instance.Mod = this;
                instance.Plan = OverridePlan.Build(type, typeof(ModType));
                _types.Add(instance);

                // GlobalItem 不占游戏 ID：发现即进注册表（ItemLoader 分派靠它）。
                if (instance is GlobalItem globalItem) GlobalItemRegistry.Register(globalItem);
                // GlobalProjectile 同理（ProjectileLoader 分派靠它）。
                if (instance is GlobalProjectile globalProjectile) GlobalProjectileRegistry.Register(globalProjectile);
                // GlobalNpc 同理（NpcLoader 分派靠它）。
                if (instance is GlobalNpc globalNpc) GlobalNpcRegistry.Register(globalNpc);
                // GlobalBuff 作用于所有 buff（含原版），注册后 BuffLoader 的遍历就能看到它。
                if (instance is GlobalBuff globalBuff) GlobalBuffRegistry.Register(globalBuff);
                if (instance is GlobalTile globalTile) GlobalTileRegistry.Register(globalTile);
                // 其余 Global*（Loot/BossBar 等）在各自 Loader 迁移时挂进对应注册表。
            }

            // ModPlayer 不是 ModType（不占 ID），单独发现；每玩家实例由 PlayerLoader 懒建。
            foreach (var type in SafeGetTypes(GetType().Assembly))
            {
                if (type == null || !type.IsClass || type.IsAbstract) continue;
                if (!typeof(ModPlayer).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                var instance = (ModPlayer)Activator.CreateInstance(type);
                instance.Mod = this;
                PlayerLoader.Register(instance);
            }
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                var types = new List<Type>();
                foreach (var type in e.Types) if (type != null) types.Add(type);
                return types.ToArray();
            }
        }
    }
}