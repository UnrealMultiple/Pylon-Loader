using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 发型管线：发号 + 主贴图 + 发型菜单（创建角色 / 理发师）+ 戴帽子版贴图（<c>PlayerHairAlt</c>）。
/// 菜单按两段式：<c>ListWarrantsRemake</c> 里先算好每个 mod 发型的解锁态，
/// <c>RebuildList</c> 里把可用的塞进列表。
/// </summary>
internal sealed class HairPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static HairPipeline Instance { get; } = new();

    public string Name => "hair";


    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        // 发型菜单：游戏重建可选列表时把 mod 发型按解锁态塞进去。
        On.Terraria.GameContent.HairstyleUnlocksHelper.ListWarrantsRemake += ListWarrantsRemake;
        On.Terraria.GameContent.HairstyleUnlocksHelper.RebuildList += RebuildList;

        // 存档：原版 .plr 只认 0..227，228 读回来会被游戏打回默认（建号界面正常、选人界面变默认）。
        On.Terraria.Player.InternalSavePlayerFile += SavePlayerFile;
        On.Terraria.Player.LoadPlayer += LoadPlayerFile;

        Log.Info("[hair] 已挂（编号 / 扩表 / 主贴图 / 菜单 / 戴帽子版贴图 / 存档按名字）；不接 DrawBackHair");
        return true;
    }

    // ===================== 发型菜单 =====================

    private static readonly Dictionary<int, bool> MenuUnlocked = [];

    /// <summary>按创建角色 / 理发师算出每个 mod 发型当次的解锁态；有变化就让游戏重造列表。</summary>
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
                now = hair.IsUnlocked(creation, !creation);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[hair] {hair.Name}.IsUnlocked 异常：{Safe.Describe(e)}");
                now = false;
            }

            if (!MenuUnlocked.TryGetValue(hair.Type, out var before) || before != now) remake = true;
            MenuUnlocked[hair.Type] = now;
        }

        return remake;
    }

    /// <summary>游戏重造列表之后，把当次可用的 mod 发型补进去。</summary>
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

    /// <summary>创建角色标记（游戏源码里是 private 字段，interop 代理里照常可读）。</summary>
    private static bool ReadCreationFlag(Terraria.GameContent.HairstyleUnlocksHelper self) => self._isAtCharacterCreation;

    private static bool SafeUnlocked(ModHair hair, bool creation)
    {
        try
        {
            return hair.IsUnlocked(creation, !creation);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[hair] {hair.Name}.IsUnlocked 异常：{Safe.Describe(e)}");
            return false;
        }
    }

    public void Refresh() => HairTable.Assign();

    /// <summary>安装期一次：扩表 + 静态默认值。要在写贴图（Surface 阶段）之前跑。</summary>
    internal void Build()
    {
        // TextureAssets.ResizePlayerHair：精确扩
        // PlayerHair 与 PlayerHairAlt 两张表到同一长度。不做类级扫描 ——
        // 那会把"长度刚好相同"的其它贴图表一起扩长，发型表本身只该动这两张。
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

    /// <summary>戴帽子版贴图：有 _Alt 就写它，没有就把主贴图写进 Alt 表（戴帽子时至少还是这个发型）。</summary>
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

    // ===================== 存档 =====================

    /// <summary>侧车里的键名；值是 <c>Mod名/类名</c>，跨设备按名字恢复，不依赖发号。</summary>
    private const string SaveKey = "pylon:hair";

    /// <summary>
    /// 存盘前把 mod 发型临时写回 0（原版 .plr 只认它自己的 228 个），
    /// 名字写进玩家侧车；游戏存完立刻把内存里的值恢复，界面不受影响。
    /// </summary>
    private static void SavePlayerFile(On.Terraria.Player.orig_InternalSavePlayerFile orig, Terraria.IO.PlayerFileData playerFile)
    {
        var player = playerFile is null ? null : playerFile.Player;
        var hair = player is null ? null : HairTable.Of(player.hair);

        if (hair is not null && player is not null) player.hair = 0;
        try
        {
            orig(playerFile);

            if (player is null) return;
            if (DataFile(playerFile) is not { } file) return;

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

    /// <summary>读档后按侧车里的名字把 mod 发型写回 <c>player.hair</c>（选人界面的预览也走这条）。</summary>
    private static Terraria.IO.PlayerFileData LoadPlayerFile(On.Terraria.Player.orig_LoadPlayer orig, string playerPath, bool cloudSave)
    {
        var data = orig(playerPath, cloudSave);
        try
        {
            if (data is null) return null!;

            var player = data.Player;
            if (player is null) return data;
            if (DataFile(data) is not { } file) return data;

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

    /// <summary>玩家侧车路径：<c>&lt;玩家文件&gt;.pl.json</c>（pl = Pylon Loader）；云存档不写。</summary>
    private static string? DataFile(Terraria.IO.PlayerFileData? data)
    {
        if (data is null || data.IsCloudSave) return null;
        var path = data.Path;
        return string.IsNullOrEmpty(path) ? null : path + ".pl.json";
    }
}
