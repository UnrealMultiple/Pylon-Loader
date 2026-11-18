using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>发型管线：发号 + 主贴图 + 发型菜单（创建角色 / 理发师）+ 戴帽子版贴图。</summary>
internal sealed class HairPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static HairPipeline Instance { get; } = new();

    public string Name => "hair";


    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.GameContent.HairstyleUnlocksHelper.ListWarrantsRemake += ListWarrantsRemake;
        On.Terraria.GameContent.HairstyleUnlocksHelper.RebuildList += RebuildList;
        On.Terraria.Player.GetHairSettings += GetHairSettings;

        On.Terraria.Player.InternalSavePlayerFile += SavePlayerFile;
        On.Terraria.Player.LoadPlayer += LoadPlayerFile;

        Log.Info("[hair] 已挂（编号 / 扩表 / 主贴图 / 菜单 / 戴帽子版贴图 / 存档按名字 / 后发开关）");
        return true;
    }

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
                ContentErrors.Report($"[hair] {hair.Name}.GetUnlockConditions 异常：{Safe.Describe(e)}");
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
                ContentErrors.Report($"[hair] {hair.Name} 进菜单失败：{Safe.Describe(e)}");
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
            ContentErrors.Report($"[hair] {hair.Name}.GetUnlockConditions 异常：{Safe.Describe(e)}");
            return false;
        }
    }

    private static bool Available(ModHair hair, bool creation) =>
        creation ? hair.AvailableDuringCharacterCreation
                 : hair.GetUnlockConditions().All(c => c.IsMet());

    public void Refresh() => HairTable.Assign();

    internal void Build()
    {
        var total = HairTable.Total;
        if (total > HairTable.Base && total > 0)
        {
            var hair = Terraria.GameContent.TextureAssets.PlayerHair;
            var hairAlt = Terraria.GameContent.TextureAssets.PlayerHairAlt;
            if (ArrayGrow.Grow(ref hair, total)) Terraria.GameContent.TextureAssets.PlayerHair = hair;
            if (ArrayGrow.Grow(ref hairAlt, total)) Terraria.GameContent.TextureAssets.PlayerHairAlt = hairAlt;
        }

        foreach (var hair in ModContent.GetContent<ModHair>())
        {
            if (!hair.Registered) continue;
            hair.SetStaticDefaults();
        }

        WriteAltTextures();

        if (HairTable.Count > 0) Log.Info($"[hair] 安装：{HairTable.Count} 个发型");
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
        ContentErrors.Report($"[hair] {hair.Name} 的戴帽子版贴图失败：{Safe.Describe(e)}");

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

            var all = TagJson.Read(file);
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

            if (all.Count > 0) TagJson.Write(file, all);
            else File.Delete(file);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[hair] 存档失败：" + Safe.Describe(e));
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

            var all = TagJson.Read(file);
            if (all.Get(SaveKey) is not string key) return data;

            var type = HairTable.Find(key);
            if (type > 0) player.hair = type;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[hair] 读档失败：" + Safe.Describe(e));
        }

        return data;
    }
}
