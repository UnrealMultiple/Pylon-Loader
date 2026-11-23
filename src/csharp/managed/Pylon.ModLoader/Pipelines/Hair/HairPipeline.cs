using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.SaveData;
using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 发型管线：发号 + 主贴图 + 发型菜单（创建角色 / 理发师）+ 戴帽子版贴图。
/// 两张贴图表随号段扩容并纳入表维护：装配前经扩表步骤扩到位，游戏重建后随巡检重扩（表声明与锚点在 HairTables）。
/// 装配逐发型隔离：SetStaticDefaults 与戴帽子版贴图写槽各自按发型兜异常并上报，坏一个只坏一个。
/// 多人同步：原版玩家信息发完后，mod 发型号另发一条附带包；服务器以连接来源改写玩家索引后全网转发，
/// 编号不在本端注册的包直接拒收，发号按注册序裸传、不做跨端映射。
/// </summary>
internal sealed class HairPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static HairPipeline Instance { get; } = new();

    public string Name => "hair";

    public IReadOnlyList<TableRequest> Tables =>
        [new(HairTables.Set, () => HairTable.Total, early: true),
         new(HairTables.Alt, () => HairTable.Total, early: true)];


    private static IModLog Log => LoaderHost.Current.Log;

    private const int PlayerInfo = 4;
    private const int MaxPlayerIndex = 254;

    private NetPipeline _net = null!;
    private byte _kind;

    /// <summary>hair 管线的安装门：条件与订阅同居一门，由 GateHub 自动发现与派发。</summary>
    internal static class Gates
    {
        internal static bool HasHairs() => ModContent.GetContent<ModHair>().Count > 0;

        internal struct Menu : IGate
        {
            public static bool Wanted() => HasHairs();

            public static void Install()
            {
                On.Terraria.GameContent.HairstyleUnlocksHelper.ListWarrantsRemake += ListWarrantsRemake;
                On.Terraria.GameContent.HairstyleUnlocksHelper.RebuildList += RebuildList;

                Log.Info("[hair] 已挂：发型菜单");
            }
        }

        internal struct Settings : IGate
        {
            public static bool Wanted() => HasHairs();

            public static void Install()
            {
                On.Terraria.Player.GetHairSettings += GetHairSettings;

                Log.Info("[hair] 已挂：后发开关");
            }
        }

        internal struct Save : IGate
        {
            public static bool Wanted() => HasHairs();

            public static void Install()
            {
                On.Terraria.Player.InternalSavePlayerFile += SavePlayerFile;
                On.Terraria.Player.LoadPlayer += LoadPlayerFile;

                Log.Info("[hair] 已挂：存档按名字");
            }
        }

        internal struct Net : IGate
        {
            public static bool Wanted() => HasHairs();

            public static void Install()
            {
                Instance._net = NetPipeline.Instance;
                Instance._kind = Instance._net.OpenChannel(Instance.Receive);
                On.Terraria.NetMessage.SendData += Instance.SendData;

                Log.Info("[hair] 已挂：多人同步");
            }
        }
    }

    public bool Install() => GateHub<HairPipeline>.Run();

    private static readonly Dictionary<int, bool> MenuUnlocked = [];

    private static bool ListWarrantsRemake(On.Terraria.GameContent.HairstyleUnlocksHelper.orig_ListWarrantsRemake orig,
        Terraria.GameContent.HairstyleUnlocksHelper self)
    {
        var remake = orig(self);

        var creation = ReadCreationFlag(self);
        foreach (var hair in ModContent.GetContent<ModHair>())
        {
            if (!hair.Registered) continue;

            bool now;
            try
            {
                now = Available(hair, creation);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[hair] {hair.Name}.GetUnlockConditions 异常：{Safe.Dump(e)}");
                now = false;
            }

            if (!MenuUnlocked.TryGetValue(hair.Type, out var before) || before != now) remake = true;
            MenuUnlocked[hair.Type] = now;
        }

        return remake;
    }

    private static void RebuildList(On.Terraria.GameContent.HairstyleUnlocksHelper.orig_RebuildList orig,
        Terraria.GameContent.HairstyleUnlocksHelper self)
    {
        orig(self);

        var list = self.AvailableHairstyles;
        if (list is null) return;

        var creation = ReadCreationFlag(self);
        foreach (var hair in ModContent.GetContent<ModHair>())
        {
            if (!hair.Registered) continue;

            var available = MenuUnlocked.TryGetValue(hair.Type, out var known)
                ? known
                : SafeUnlocked(hair, creation);
            if (!available) continue;

            try
            {
                if (!list.Contains(hair.Type)) list.Add(hair.Type);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[hair] {hair.Name} 进菜单失败：{Safe.Dump(e)}");
            }
        }
    }

    private static bool ReadCreationFlag(Terraria.GameContent.HairstyleUnlocksHelper self) => self._isAtCharacterCreation;

    private static void GetHairSettings(On.Terraria.Player.orig_GetHairSettings orig, Player self,
        out bool fullHair, out bool hatHair, out bool hideHair, out bool backHairDraw, out bool drawsBackHairWithoutHeadgear)
    {
        orig(self, out fullHair, out hatHair, out hideHair, out backHairDraw, out drawsBackHairWithoutHeadgear);

        var set = HairIdTml.Sets.DrawBackHair;
        var hair = self.hair;
        if (hair >= 0 && hair < set.Length) backHairDraw = set[hair];
    }

    private static bool SafeUnlocked(ModHair hair, bool creation)
    {
        try
        {
            return Available(hair, creation);
        }
        catch (Exception e)
        {
            ContentErrors.Line($"[hair] {hair.Name}.GetUnlockConditions 异常：{Safe.Dump(e)}");
            return false;
        }
    }

    private static bool Available(ModHair hair, bool creation) =>
        creation ? hair.AvailableDuringCharacterCreation
                 : hair.GetUnlockConditions().All(c => c.IsMet());

    public void Refresh() => HairTable.Assign();

    internal void Build()
    {
        Grow();

        foreach (var hair in ModContent.GetContent<ModHair>())
        {
            if (!hair.Registered) continue;

            try
            {
                hair.SetStaticDefaults();
            }
            catch (Exception e)
            {
                ContentErrors.Callback($"[hair] {hair.Name}.SetStaticDefaults", e);
            }
        }

        WriteAltTextures();

        if (HairTable.Count > 0) Log.Info($"[hair] 安装：{HairTable.Count} 个发型");
    }

    private static void Grow()
    {
        var total = HairTable.Total;
        if (total <= HairTable.Base) return;

        try
        {
            TableMaintenance.Ensure(HairTables.Set, total).GrowAll(Log.Info);
            TableMaintenance.Ensure(HairTables.Alt, total).GrowAll(Log.Info);
        }
        catch (Exception e)
        {
            Safe.Report("hair.grow", e);
        }
    }

    private static void WriteAltTextures()
    {
        var written = 0;

        foreach (var hair in ModContent.GetContent<ModHair>())
        {
            if (!hair.Registered) continue;

            try
            {
                var path = hair.AltTexture;
                var hasAlt = !string.IsNullOrEmpty(path) && ModContent.HasAsset(hair.Mod, path);
                var asset = hasAlt ? hair.Mod.LoadAsset(path) : hair.Texture;

                if (asset is null) continue;
                if (TextureSlots.TryWrite(TextureTable.PlayerHairAlt, hair.Type, asset)) written++;
                else Log.Warn($"[hair] {hair.Name} 的戴帽子版贴图没写进 PlayerHairAlt[{hair.Type}]（表长 {TextureSlots.Length(TextureTable.PlayerHairAlt)}）");
            }
            catch (Exception e)
            {
                ContentReports(hair, e);
            }
        }

        if (written > 0) Log.Info($"[hair] 戴帽子版贴图：写 {written}");
    }

    private static void ContentReports(ModHair hair, Exception e) =>
        ContentErrors.Callback($"[hair] {hair.Name} 的戴帽子版贴图", e);

    private const string SaveKey = "pylon:hair";

    private static void SavePlayerFile(On.Terraria.Player.orig_InternalSavePlayerFile orig, Terraria.IO.PlayerFileData playerFile)
    {
        var player = playerFile is null ? null : playerFile.Player;
        var hair = player is null ? null : HairTable.Of(player.hair);

        if (hair is not null && player is not null) player.hair = 0;
        try
        {
            orig(playerFile);

            if (player is null) return;
            if (PlayerSidecar.DataFile(playerFile) is not { } file) return;

            var all = PlayerSidecar.Read(file);
            if (hair is not null)
            {
                all.Set(SaveKey, hair.FullKey);
            }
            else if (all.Get(SaveKey) is string saved && HairTable.Find(saved) > 0)
            {
                all.Remove(SaveKey);
            }
            else
            {
                return;
            }

            if (all.Count > 0) PlayerSidecar.Write(file, all);
            else File.Delete(file);
        }
        catch (Exception e)
        {
            ContentErrors.Line("[hair] 存档失败：" + Safe.Dump(e));
        }
        finally
        {
            if (hair is not null && player is not null) player.hair = hair.Type;
        }
    }

    private static Terraria.IO.PlayerFileData LoadPlayerFile(On.Terraria.Player.orig_LoadPlayer orig, string playerPath, bool cloudSave)
    {
        var data = orig(playerPath, cloudSave);
        try
        {
            if (data is null) return null!;

            var player = data.Player;
            if (player is null) return data;
            if (PlayerSidecar.DataFile(data) is not { } file) return data;

            var all = PlayerSidecar.Read(file);
            if (all.Get(SaveKey) is not string key) return data;

            var type = HairTable.Find(key);
            if (type > 0) player.hair = type;
        }
        catch (Exception e)
        {
            ContentErrors.Line("[hair] 读档失败：" + Safe.Dump(e));
        }

        return data;
    }

    private void SendData(On.Terraria.NetMessage.orig_SendData orig, int msgType, int remoteClient, int ignoreClient,
                          Terraria.Localization.NetworkText text, int number, float number2, float number3,
                          float number4, int number5, int number6, int number7)
    {
        orig(msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6, number7);

        if (msgType != PlayerInfo || Main.netMode == 0 || number < 0 || number > MaxPlayerIndex) return;

        var player = Main.player[number];
        var hair = player is null ? null : HairTable.Of(player.hair);
        if (hair is null) return;

        _net.SendChannel(_kind, writer =>
        {
            writer.Write((byte)number);
            writer.Write(hair.Type);
        }, remoteClient, ignoreClient);
    }

    private void Receive(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var index = (int)reader.ReadByte();
        var hair = reader.ReadInt32();

        if (Main.netMode == 2) index = from;

        if (index < 0 || index > MaxPlayerIndex || HairTable.Of(hair) is null) return;

        var player = Main.player[index];
        if (player is null) return;
        player.hair = hair;

        if (Main.netMode != 2) return;

        _net.SendChannel(_kind, writer =>
        {
            writer.Write((byte)index);
            writer.Write(hair);
        }, -1, from);
    }
}
