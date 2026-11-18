using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.SaveData;
using Pylon.ModLoader.Tables;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 敌人旗子管线：把 mod NPC 接进游戏的旗子系统（映射 / 击杀数 / 新旗标 / 存档）。
/// 游戏那三张平行表一张都不扩，mod 编号的访问全部由 <see cref="BannerTable"/> 回答。
/// </summary>
internal sealed class BannerPipeline : ILoaderModule
{
    private const string SaveSection = "banners";

    public static BannerPipeline Instance { get; } = new();

    private readonly HashSet<long> _reported = [];

    public string Name => "banner";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.GameContent.BannerSystem.NPCtoBanner += NpcToBanner;
        On.Terraria.GameContent.BannerSystem.BannerToItem += BannerToItem;
        On.Terraria.GameContent.BannerSystem.BannerToNPC += BannerToNpc;
        On.Terraria.GameContent.BannerSystem.GetKillCount += GetKillCount;
        On.Terraria.GameContent.BannerSystem.AddKill += AddKill;
        On.Terraria.GameContent.BannerSystem.AddNPCKillBy += AddNpcKillBy;
        On.Terraria.GameContent.BannerSystem.IsNewBanner += IsNewBanner;
        On.Terraria.GameContent.BannerSystem.SetNewBanner += SetNewBanner;
        On.Terraria.GameContent.BannerSystem.ClearNewBanner += ClearNewBanner;
        On.Terraria.GameContent.BannerSystem.Clear += Clear;
        On.Terraria.GameContent.BannerSystem.GetClaimableBannerCounts += ClaimableCounts;
        On.Terraria.GameContent.BannerSystem.RequestBannerClaim += RequestClaim;
        On.Terraria.Main.MouseText_DrawBuffTooltip += BuffTooltip;
        On.Terraria.IO.WorldFile.LoadWorld += LoadWorld;
        On.Terraria.IO.WorldFile.InternalSaveWorld += SaveWorld;

        Log.Info("[banner] 已挂（映射 / 击杀数 / 新旗标 / 领取 / 插地 buff / 存档）");
        return true;
    }

    internal void Build()
    {
        BannerTable.Build();
        if (BannerTable.Count > 0) Log.Info($"[banner] 安装：{BannerTable.Count} 面旗子");
    }

    private static bool IsModId(int id) => id >= VanillaCounts.Npc;

    private static int NpcToBanner(On.Terraria.GameContent.BannerSystem.orig_NPCtoBanner orig, int i)
    {
        var banner = BannerTable.BannerOfNpcType(i);
        if (banner > 0) return banner;
        return IsModId(i) ? i : orig(i);
    }

    private static int BannerToItem(On.Terraria.GameContent.BannerSystem.orig_BannerToItem orig, int banner)
    {
        var item = BannerTable.ItemForBanner(banner);
        if (item > 0) return item;
        return IsModId(banner) ? 0 : orig(banner);
    }

    private static int BannerToNpc(On.Terraria.GameContent.BannerSystem.orig_BannerToNPC orig, int i) =>
        IsModId(i) ? i : orig(i);

    private static int GetKillCount(On.Terraria.GameContent.BannerSystem.orig_GetKillCount orig, int banner) =>
        IsModId(banner) ? BannerTable.Kills(banner) : orig(banner);

    private static void AddKill(On.Terraria.GameContent.BannerSystem.orig_AddKill orig, int banner)
    {
        if (IsModId(banner))
        {
            if (!BannerTable.IsMod(banner)) return;
            BannerTable.AddKill(banner);
            BannerNetPipeline.Instance.PushKill(banner, BannerTable.Kills(banner));
            return;
        }

        orig(banner);
    }

    private static void AddNpcKillBy(On.Terraria.GameContent.BannerSystem.orig_AddNPCKillBy orig, int npcType, int plr)
    {
        var banner = BannerTable.BannerOfNpcType(npcType);
        if (banner > 0 && BannerTable.IsMod(banner))
        {
            BannerTable.AddKill(banner);
            BannerNetPipeline.Instance.PushKill(banner, BannerTable.Kills(banner));
            return;
        }

        if (IsModId(npcType)) return;

        orig(npcType, plr);
    }

    private static bool IsNewBanner(On.Terraria.GameContent.BannerSystem.orig_IsNewBanner orig, int banner) =>
        IsModId(banner) ? BannerTable.Fresh(banner) : orig(banner);

    private static void SetNewBanner(On.Terraria.GameContent.BannerSystem.orig_SetNewBanner orig, int banner)
    {
        if (IsModId(banner))
        {
            BannerTable.SetFresh(banner, true);
            return;
        }

        orig(banner);
    }

    private static void ClearNewBanner(On.Terraria.GameContent.BannerSystem.orig_ClearNewBanner orig, int banner)
    {
        if (IsModId(banner))
        {
            BannerTable.SetFresh(banner, false);
            return;
        }

        orig(banner);
    }

    private static void Clear(On.Terraria.GameContent.BannerSystem.orig_Clear orig)
    {
        orig();
        BannerTable.ClearState();
    }

    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ushort> ClaimableCounts(
        On.Terraria.GameContent.BannerSystem.orig_GetClaimableBannerCounts orig)
    {
        var vanilla = orig();
        if (BannerTable.Count == 0) return vanilla;

        var max = BannerTable.MaxBanner;
        if (max <= 0) return vanilla;

        if (vanilla is null) return null!;

        var length = Math.Max(vanilla.Length, max + 1);
        if (length == vanilla.Length) return vanilla;

        var grown = ArrayGrow.Grow(vanilla.Pointer, length);
        if (grown == IntPtr.Zero) return vanilla;

        var merged = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ushort>(grown);
        var span = merged.AsSpan();
        for (var banner = VanillaCounts.Npc + 1; banner <= max && banner < span.Length; banner++)
        {
            var count = BannerTable.Claimable(banner);
            if (count > 0) span[banner] = (ushort)Math.Min(count, ushort.MaxValue);
        }

        return merged;
    }

    private static void RequestClaim(On.Terraria.GameContent.BannerSystem.orig_RequestBannerClaim orig, int banner,
                                     int amount)
    {
        if (!IsModId(banner))
        {
            orig(banner, amount);
            return;
        }

        if (!BannerTable.IsMod(banner)) return;

        if (Main.netMode == 1)
        {
            BannerNetPipeline.Instance.RequestClaim(banner, amount);
            return;
        }

        var granted = BannerTable.Claim(banner, amount);
        if (granted <= 0) return;

        var item = BannerTable.ItemForBanner(banner);
        if (item <= 0) return;

        Main.LocalPlayer.QuickSpawnItem(null, item, granted);
        BannerTable.SetFresh(banner, false);
    }

    private static void BuffTooltip(On.Terraria.Main.orig_MouseText_DrawBuffTooltip orig, Main self,
                                    string cursorText, string buffString, ref int x, ref int y)
    {
        var max = BannerTable.MaxBanner;
        var previous = 0;
        var widened = false;

        if (max >= VanillaCounts.Npc)
        {
            previous = BannerSystem.MaxBannerTypes;
            if (previous > 0 && previous <= max)
            {
                BannerSystem.MaxBannerTypes = max + 1;
                widened = true;
            }
        }

        try
        {
            orig(self, cursorText, buffString, ref x, ref y);
        }
        finally
        {
            if (widened)
                BannerSystem.MaxBannerTypes = previous;
        }
    }

    private static bool HasBuff(int banner)
    {
        var table = Main.SceneMetrics?.NPCBannerBuff;
        return table is not null && banner < table.Length && table[banner];
    }

    private static void LoadWorld(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
    {
        orig(canCheckFileState);
        Load();
    }

    private static void Load()
    {
        if (DataFile() is null) return;

        var entries = WorldSidecar.Read().GetList<TagCompound>(SaveSection);
        var loaded = 0;

        foreach (var entry in entries)
        {
            var key = entry.GetString("k");
            if (string.IsNullOrEmpty(key)) continue;

            BannerTable.Restore(key, entry.GetInt("c"), entry.GetBool("f"));
            loaded++;
        }

        if (loaded > 0) Log.Info($"[banner] 读档：{loaded} 面旗子");
    }

    private static void SaveWorld(On.Terraria.IO.WorldFile.orig_InternalSaveWorld orig, bool useCloudSaving,
                                  bool resetTime)
    {
        orig(useCloudSaving, resetTime);
        if (useCloudSaving) return;
        Save();
    }

    private static void Save()
    {
        if (DataFile() is null) return;

        var entries = new List<object?>();
        foreach (var banner in BannerTable.Dirty())
        {
            entries.Add(new TagCompound
            {
                ["k"] = BannerTable.KeyOf(banner),
                ["c"] = BannerTable.Kills(banner),
                ["f"] = BannerTable.Fresh(banner),
            });
        }

        var root = WorldSidecar.Read();
        if (entries.Count > 0) root.Set(SaveSection, entries);
        else if (!root.ContainsKey(SaveSection)) return;
        else root.Remove(SaveSection);

        WorldSidecar.Write(root);
    }

    private static string? DataFile()
    {
        var path = Main.worldPathName;
        if (string.IsNullOrEmpty(path)) return null;
        if (path.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return path + ".wld.banners.json";
    }
}

