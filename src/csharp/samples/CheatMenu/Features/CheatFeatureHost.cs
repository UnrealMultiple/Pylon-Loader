using System;
using System.Collections.Generic;
using System.IO;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;

namespace CheatMenu.Features;

/// <summary>
/// 特性注册表与统一分派。集中安装 hook、每帧 tick、菜单绘制与收包，
/// 避免每个功能各自订阅一堆 On.* 导致热路径分派链失控。
/// 当前只移植原版「主要」页；其余页按原版逐个补齐，不放假功能。
/// </summary>
public static class CheatFeatureHost
{
    private static readonly List<CheatFeature> Features = new();
    private static readonly HashSet<string> Failed = new();

    private static CheatConfig _config;
    private static bool _installed;
    private static bool _dirty;
    private static long _dirtyAt;

    public static CheatConfig Config
    {
        get
        {
            if (_config is null) _config = ModContent.GetInstance<CheatConfig>();
            return _config;
        }
    }

    public static IReadOnlyList<CheatFeature> All => Features;

    public static bool Installed => _installed;

    public static bool IsFailed(string id) => Failed.Contains(id);

    /// <summary>分类/子页集合变化时递增；UI 用它决定要不要重建导航缓存。</summary>
    public static int RegistryVersion { get; private set; } = -1;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        var config = Config;
        CheatMenuMod.Log?.Info(config is null
            ? "[cheat] 配置未注册（CheatConfig 不会被保存）"
            : "[cheat] 配置已注册：" + config.FullName);

        // 顺序 = 原版 Tools.cpp 的注册顺序；角色处理卡排最后（原版也是追加）。
        Features.Add(new PlayerCheatsFeature());
        Features.Add(new FullBrightFeature());
        Features.Add(new DiggingFeature());
        Features.Add(new GhostFeature());
        Features.Add(new RespawnFeature());
        Features.Add(new LoadoutFeature());
        Features.Add(new FishFeature());
        Features.Add(new BuffGenFeature());
        Features.Add(new CharacterFeature());

        // ---- 世界（子页：常用 / 世界编辑） ----
        Features.Add(new WorldFeature());
        Features.Add(new WorldEditBrushFeature());
        Features.Add(new WorldEditCopyPasteFeature());

        // ---- 实体（子页：NPC生成 / 生成设置 / 附加弹幕 / 屠杀NPC / 屠杀玩家） ----
        Features.Add(new NpcSpawnFeature());
        Features.Add(new NpcSpawnSettingsFeature());
        Features.Add(new ProjectileFeature());
        Features.Add(new ButcherNpcFeature());
        Features.Add(new PlayerButcherFeature());

        // ---- 物品（子页：物品生成 / Mod 物品 / 物品修改器） ----
        Features.Add(new ItemBrowserFeature());
        Features.Add(new ModItemsFeature());
        Features.Add(new ItemModifierFeature());

        // ---- 杂项（子页：解锁 / 漏洞利用；网易音乐是独立子系统，暂不迁移） ----
        Features.Add(new AchievementUnlockFeature());
        Features.Add(new CameraScaleFeature());
        Features.Add(new SyncChestSizeFeature());
        Features.Add(new NpcImmortalFeature());
        Features.Add(new GemStaffForm2Feature());   // 常驻后台，不出卡片

        // ---- 网络（NetworkTool 包解析按计划暂缓） ----

        // ---- 设置 ----
        Features.Add(new SettingsFeature());

        // ---- 视觉 ----
        Features.Add(new EspFeature());
        Features.Add(new PlayerHudFeature());
        Features.Add(new ProjectilePredictionFeature());

        // ---- 检查器（子页：物品 / NPC / 玩家 / 弹幕 / 图格） ----
        Features.Add(new InspectorItemFeature());
        Features.Add(new InspectorNpcFeature());
        Features.Add(new InspectorPlayerFeature());
        Features.Add(new InspectorProjectileFeature());
        Features.Add(new InspectorTileFeature());

        foreach (var feature in Features)
        {
            try
            {
                feature.Install();
                CheatMenuMod.Log?.Info($"[cheat] 已装载 {feature.Id} → {feature.Category}/{feature.SubPage}");
            }
            catch (Exception e)
            {
                Fail(feature, e);
            }
        }

        RegistryVersion++;
    }

    public static void Tick()
    {
        if (!_installed) return;
        foreach (var feature in Features)
        {
            if (Failed.Contains(feature.Id)) continue;
            try { feature.Tick(); }
            catch (Exception e) { Fail(feature, e); }
        }
        Flush();
    }

    public static void DrawCategory(CheatCategory category, string subPage)
    {
        if (!_installed)
        {
            Pylon.ModLoader.ImGuiHost.ModernUI.DimText("作弊模块尚未装配（等待内容就绪）");
            return;
        }

        var drawn = false;
        foreach (var feature in Features)
        {
            if (feature.Category != category || Failed.Contains(feature.Id)) continue;
            if (feature.SubPage is null) continue;   // 常驻后台功能：无 DrawMenu
            if (!string.Equals(feature.SubPage, subPage, StringComparison.Ordinal)) continue;

            drawn = true;
            try { feature.DrawMenu(); }
            catch (Exception e) { Fail(feature, e); }
        }

        if (!drawn)
        {
            Pylon.ModLoader.ImGuiHost.ModernUI.DimText("这个子页还没有迁移完成的功能");
        }
    }

    public static void DrawWorldTiles(SpriteBatch batch)
    {
        if (!_installed || batch is null) return;
        foreach (var feature in Features)
        {
            if (Failed.Contains(feature.Id)) continue;
            try { feature.DrawWorldTiles(batch); }
            catch (Exception e) { Fail(feature, e); }
        }
    }

    /// <summary>收包分派：任一功能返回 true 表示吞掉该包。</summary>
    public static bool OnGetData(ref byte messageType, ref BinaryReader reader, int playerNumber)
    {
        if (!_installed) return false;
        foreach (var feature in Features)
        {
            if (Failed.Contains(feature.Id)) continue;
            try
            {
                if (feature.OnGetData(ref messageType, ref reader, playerNumber)) return true;
            }
            catch (Exception e) { Fail(feature, e); }
        }
        return false;
    }

    public static void CollectCategories(List<CheatCategory> into)
    {
        into.Clear();
        foreach (var feature in Features)
        {
            if (Failed.Contains(feature.Id) || into.Contains(feature.Category)) continue;
            into.Add(feature.Category);
        }
        into.Sort((a, b) => ((int)a).CompareTo((int)b));
    }

    public static void CollectSubPages(CheatCategory category, List<string> into)
    {
        into.Clear();
        foreach (var feature in Features)
        {
            if (feature.Category != category || Failed.Contains(feature.Id)) continue;
            if (feature.SubPage is null) continue;   // 常驻后台功能（无卡片）不占子页
            if (into.Contains(feature.SubPage)) continue;
            into.Add(feature.SubPage);
        }
    }

    /// <summary>改动只标记，600ms 内的连续拖动合并成一次写盘。</summary>
    public static void MarkDirty()
    {
        _dirty = true;
        _dirtyAt = Environment.TickCount64;
    }

    public static void SaveNow()
    {
        _dirty = false;
        try
        {
            Config?.SaveChanges(Config, null, true, false);
        }
        catch (Exception e)
        {
            CheatMenuMod.Log?.Warn("[cheat] 配置保存失败：" + e.Message);
        }
    }

    public static void Flush(bool force = false)
    {
        if (!_dirty) return;
        if (!force && Environment.TickCount64 - _dirtyAt < 600) return;
        SaveNow();
    }

    private static void Fail(CheatFeature feature, Exception e)
    {
        if (!Failed.Add(feature.Id)) return;
        RegistryVersion++;
        CheatMenuMod.Log?.Error($"[cheat] {feature.Id} 出错，已停用：{e}");
    }
}