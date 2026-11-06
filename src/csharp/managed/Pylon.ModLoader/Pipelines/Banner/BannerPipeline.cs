using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader;

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
        On.Terraria.Main.Update += BuffScan;
        On.Terraria.SceneMetrics.Scan += BuffApply;
        On.Terraria.Player.HasNPCBannerBuff += HasNpcBannerBuff;
        On.Terraria.IO.WorldFile.LoadWorld += LoadWorld;
        On.Terraria.IO.WorldFile.InternalSaveWorld += SaveWorld;

        Log.Info("[banner] 已挂（映射 / 击杀数 / 新旗标 / 领取 / 插地 buff / 存档）");
        return true;
    }

    /// <summary>安装期建表：NPC 的 Banner / BannerItem 在 SetStaticDefaults 里才写完，所以放在 Samples 阶段。</summary>
    internal void Build()
    {
        BannerTable.Build();
        if (BannerTable.Count > 0) Log.Info($"[banner] 安装：{BannerTable.Count} 面旗子");
    }

    // ===================== 映射 =====================

    private static int NpcToBanner(On.Terraria.GameContent.BannerSystem.orig_NPCtoBanner orig, int i)
    {
        var banner = BannerTable.BannerOfNpcType(i);
        return banner > 0 ? banner : orig(i);
    }

    private static int BannerToItem(On.Terraria.GameContent.BannerSystem.orig_BannerToItem orig, int banner)
    {
        var item = BannerTable.ItemForBanner(banner);
        return item > 0 ? item : orig(banner);
    }

    private static int BannerToNpc(On.Terraria.GameContent.BannerSystem.orig_BannerToNPC orig, int i) =>
        BannerTable.IsMod(i) ? i : orig(i);

    // ===================== 计数 =====================

    private static int GetKillCount(On.Terraria.GameContent.BannerSystem.orig_GetKillCount orig, int banner) =>
        BannerTable.IsMod(banner) ? BannerTable.Kills(banner) : orig(banner);

    private static void AddKill(On.Terraria.GameContent.BannerSystem.orig_AddKill orig, int banner)
    {
        if (BannerTable.IsMod(banner))
        {
            BannerTable.AddKill(banner);
            BannerNetPipeline.Instance.PushKill(banner, BannerTable.Kills(banner));
            return;
        }

        orig(banner);
    }

    private static void AddNpcKillBy(On.Terraria.GameContent.BannerSystem.orig_AddNPCKillBy orig, int npcType, int plr)
    {
        var banner = BannerTable.BannerOfNpcType(npcType);
        if (banner > 0)
        {
            BannerTable.AddKill(banner);
            BannerNetPipeline.Instance.PushKill(banner, BannerTable.Kills(banner));
            return;
        }

        orig(npcType, plr);
    }

    private static bool IsNewBanner(On.Terraria.GameContent.BannerSystem.orig_IsNewBanner orig, int banner) =>
        BannerTable.IsMod(banner) ? BannerTable.Fresh(banner) : orig(banner);

    private static void SetNewBanner(On.Terraria.GameContent.BannerSystem.orig_SetNewBanner orig, int banner)
    {
        if (BannerTable.IsMod(banner))
        {
            BannerTable.SetFresh(banner, true);
            return;
        }

        orig(banner);
    }

    private static void ClearNewBanner(On.Terraria.GameContent.BannerSystem.orig_ClearNewBanner orig, int banner)
    {
        if (BannerTable.IsMod(banner))
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

    // ===================== 玩家侧读表 =====================

    // SceneMetrics.NPCBannerBuff 是玩家侧按 NPC/旗子编号读的实例表，本版不扩。
    // 这三个是它的公开读取入口：mod 编号一律直接答"没有 buff"，既不越界、也不用真机去赌。

    /// <summary>
    /// 领取界面读的那张"每面可领几个"表：原版数组照给，再把 mod 旗子的格子补到够长（原版长度之外全是 0）。
    /// UI 按数组长度扫，多出来的格就是 mod 旗子；不扩游戏那张静态表，只换这一次返回的副本。
    /// </summary>
    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ushort> ClaimableCounts(
        On.Terraria.GameContent.BannerSystem.orig_GetClaimableBannerCounts orig)
    {
        var vanilla = orig();
        if (BannerTable.Count == 0) return vanilla;

        var max = BannerTable.MaxBanner;
        if (max <= 0) return vanilla;

        var length = Math.Max(vanilla?.Length ?? 0, max + 1);
        var merged = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ushort>(length);

        if (vanilla is not null)
        {
            for (var i = 0; i < vanilla.Length; i++) merged[i] = vanilla[i];
        }

        for (var banner = VanillaCounts.Npc + 1; banner <= max; banner++)
        {
            var count = BannerTable.Claimable(banner);
            if (count > 0 && count <= ushort.MaxValue) merged[banner] = (ushort)count;
        }

        return merged;
    }

    /// <summary>
    /// 领 mod 旗子：原版那条路只认它自己的表，所以 mod 编号在这里结算。
    /// 单机/服务器当场扣数发物品；客户端只发请求——扣数与发物品都由服务器做，否则两边各发一份。
    /// </summary>
    private static void RequestClaim(On.Terraria.GameContent.BannerSystem.orig_RequestBannerClaim orig, int banner,
                                     int amount)
    {
        if (!BannerTable.IsMod(banner))
        {
            orig(banner, amount);
            return;
        }

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

    // ===================== 插在地上的旗子：减伤 buff =====================

    /// <summary>附近有哪些 mod 旗子（每 <see cref="BuffInterval"/> 帧重算一次）。</summary>
    private static int[] _nearby = [];

    /// <summary>多少帧扫一次附近：旗子不会瞬移，半秒一次足够。</summary>
    private const int BuffInterval = 20;

    /// <summary>多远算"在旗下"（格）。原版把旗子算进场景扫描窗口，这里取与扫描窗口同量级的 40 格。</summary>
    private const int BuffRange = 40;

    /// <summary>定期扫玩家附近的旗子方块，记下这一批旗子号。</summary>
    private static void BuffScan(On.Terraria.Main.orig_Update orig, Main self,
                                 Il2CppMicrosoft.Xna.Framework.GameTime gameTime)
    {
        orig(self, gameTime);

        if (Main.gameMenu || Main.dedServ || BannerTable.Count == 0) return;
        if (Main.GameUpdateCount % BuffInterval != 0) return;

        var player = Main.LocalPlayer;
        if (player is null || !player.active) return;

        var center = player.Center;
        var cx = (int)(center.X / 16f);
        var cy = (int)(center.Y / 16f);

        List<int> found = [];
        for (var x = cx - BuffRange; x <= cx + BuffRange; x++)
        {
            if (x < 0 || x >= Main.maxTilesX) continue;

            for (var y = cy - BuffRange; y <= cy + BuffRange; y++)
            {
                if (y < 0 || y >= Main.maxTilesY) continue;

                var tile = Main.tile[x, y];
                if (!tile.active()) continue;

                // 旗子是 StyleHorizontal：样式在左上角那格，一格的宽度是 18 像素。
                var style = tile.frameX / 18;
                var banner = BannerTable.BannerOfTileAt(tile.type, style);
                if (banner > 0 && !found.Contains(banner)) found.Add(banner);
            }
        }

        _nearby = [.. found];
    }

    /// <summary>
    /// 原版每次场景扫描都会把 <c>NPCBannerBuff</c> 清空再按方块点亮；mod 旗子方块没有原版那套 NearbyEffects，
    /// 所以在扫描之后补点亮。顺便把这张表补到能容纳 mod 旗子号（实例字段，直接换整块数组）。
    /// </summary>
    private static void BuffApply(On.Terraria.SceneMetrics.orig_Scan orig, SceneMetrics self, SceneMetricsScanSettings settings)
    {
        orig(self, settings);

        if (_nearby.Length == 0) return;

        var max = BannerTable.MaxBanner;
        if (max <= 0) return;

        var table = self.NPCBannerBuff;
        if (table is null || table.Length <= max)
        {
            var longer = new bool[max + 1];
            if (table is not null) Array.Copy(table, longer, table.Length);

            self.NPCBannerBuff = longer;
            table = longer;
        }

        foreach (var banner in _nearby)
        {
            if (banner < table.Length) table[banner] = true;
        }
    }

    /// <summary>
    /// servamp：两张表（<c>NPCBannerBuff</c> 与 <c>ItemID.Sets.BannerStrength</c>）都已补到容纳 mod 编号，
    /// 所以 mod 旗子直接问原版即可——减伤数值也走原版那套，我们不复刻。越界前先兜一下，免得表没补上时崩。
    /// 注意：不要再钩 <c>Player.GetBannerBuffEffect</c>（两个重载都不行）。
    /// 它的 <c>out ItemID.BannerEffect</c> 是含引用字段的非 blittable struct（24 字节），
    /// 插件的 out 封送会把 out 指针传成 null，原版往里写 → 攻击 NPC 时 SIGSEGV fault 0x10。
    /// 表扩长后原版逻辑对 mod 旗子本来就成立，这两个钩子属多余。
    /// </summary>
    private static bool HasNpcBannerBuff(On.Terraria.Player.orig_HasNPCBannerBuff orig, Player self, int bannerType)
    {
        var table = Main.SceneMetrics?.NPCBannerBuff;
        if (bannerType >= VanillaCounts.Npc && (table is null || bannerType >= table.Length)) return false;

        return orig(self, bannerType);
    }

    /// <summary>这面 mod 旗子此刻有没有被点亮（表还没补就当作没有）。</summary>
    private static bool HasBuff(int banner)
    {
        var table = Main.SceneMetrics?.NPCBannerBuff;
        return table is not null && banner < table.Length && table[banner];
    }

    // ===================== 存档 =====================

    private static void LoadWorld(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
    {
        orig(canCheckFileState);
        Load();
    }

    private static void Load()
    {
        if (DataFile() is not { } file) return;

        var entries = TagJson.Read(file).GetList(SaveSection);
        var loaded = 0;

        foreach (var node in entries)
        {
            if (node is not TagCompound entry) continue;

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
        if (DataFile() is not { } file) return;

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

        var root = TagJson.Read(file);
        if (entries.Count > 0) root.Set(SaveSection, entries);
        else if (!root.ContainsKey(SaveSection)) return;
        else root.Remove(SaveSection);

        try
        {
            TagJson.Write(file, root);
        }
        catch (Exception e)
        {
            Safe.Report("banner.save", e);
        }
    }

    private static string? DataFile()
    {
        var path = Main.worldPathName;
        return string.IsNullOrEmpty(path) ? null : path + ".wld.banners.json";
    }
}