using System.ComponentModel;
using Pylon.ModLoader.Content;

namespace CheatMenu;

/// <summary>
/// 全部作弊开关。字段名尽量沿用原版 <c>mod_config.json</c> 的 key（同名冲突时加工具前缀），
/// 默认值与原版工具头文件一致。原版每个工具一个配置段；Pylon 的 ModConfig 是扁平的，
/// 所以这里用前缀区分，行为不变。
/// </summary>
public sealed class CheatConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;

    // ==================== PlayerCheatsTool ====================

    [Header("PlayerCheats")]
    [DefaultValue(false)]
    public bool GodMode = false;

    [DefaultValue(false)]
    public bool InfiniteMinions = false;

    [DefaultValue(false)]
    public bool InfiniteReach = false;

    [DefaultValue(false)]
    public bool InfiniteMana = false;

    [DefaultValue(false)]
    public bool AutoAim = false;

    /// <summary>单位：格（1 格 = 16 像素），使用时 ×16。</summary>
    [DefaultValue(60f)]
    [Range(6f, 125f)]
    [Increment(1f)]
    public float AttackRange = 60f;

    [DefaultValue(true)]
    public bool RequireLineOfSight = true;

    [DefaultValue(true)]
    public bool FavorBosses = true;

    /// <summary>0=最近 1=最高最大生命 2=最低最大生命 3=最高生命 4=最低生命</summary>
    [DefaultValue(0)]
    [Range(0, 4)]
    [Increment(1)]
    public int TargetPriority = 0;

    /// <summary>原版 key 就是小写 <c>bestow</c>：聊天发「司命赐我ID」生成物品。</summary>
    [DefaultValue(false)]
    public bool Bestow = false;

    /// <summary>字段名沿用原版；UI 标签是「禁止被传送」，true = 拦下传送。</summary>
    [DefaultValue(false)]
    public bool AllowTeleport = false;

    // ==================== FullBrightTool ====================

    [Header("FullBright")]
    [DefaultValue(false)]
    public bool FullBrightEnabled = false;

    /// <summary>亮度系数：光照色 X/Y/Z 统一取该值，1 = 原版全亮白。存 0-1，UI 显示 ×100。</summary>
    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Increment(0.01f)]
    public float FullBrightLevel = 1f;

    // ==================== WorldTool ====================

    [Header("World")]
    [DefaultValue(false)]
    public bool MapTeleport = false;

    [DefaultValue(false)]
    public bool LockTime = false;

    [DefaultValue(0.0)]
    public double LockedTime = 0.0;

    [DefaultValue(false)]
    public bool LockedDayTime = false;

    // ==================== DiggingTool ====================

    [Header("Digging")]
    [DefaultValue(false)]
    public bool DiggingKillTileRect = false;

    [DefaultValue(false)]
    public bool DiggingKillWallRect = false;

    [DefaultValue(40)]
    [Range(1, 100)]
    [Increment(1)]
    public int DiggingRange = 40;

    // ==================== GhostTool ====================

    [Header("Ghost")]
    [DefaultValue(false)]
    public bool GhostEnabled = false;

    [DefaultValue(25)]
    [Range(1, 100)]
    [Increment(1)]
    public int GhostSpeed = 25;

    // ==================== RespawnTool ====================

    [Header("Respawn")]
    [DefaultValue(false)]
    public bool RespawnEnabled = false;

    [DefaultValue(false)]
    public bool RespawnIntercept = false;

    [DefaultValue(15)]
    [Range(1, 30)]
    [Increment(1)]
    public int RespawnSeconds = 15;

    // ==================== LoadoutEffectTool ====================

    [Header("Loadout")]
    [DefaultValue(false)]
    public bool LoadoutEffects = false;

    [DefaultValue(true)]
    public bool LoadoutVanity = true;

    [DefaultValue(true)]
    public bool Loadout0 = true;

    [DefaultValue(true)]
    public bool Loadout1 = true;

    [DefaultValue(true)]
    public bool Loadout2 = true;

    // ==================== FishTool ====================

    [Header("Fish")]
    [DefaultValue(false)]
    public bool AutoFish = false;

    [DefaultValue(false)]
    public bool MultiLine = false;

    [DefaultValue(3)]
    [Range(1, 10)]
    [Increment(1)]
    public int MultiLineCount = 3;

    [DefaultValue(false)]
    public bool AutoFishItem = false;

    [DefaultValue(false)]
    public bool AutoFishNpc = false;

    [DefaultValue(false)]
    public bool AutoFishCrates = false;

    [DefaultValue(false)]
    public bool AutoFishNormal = false;

    [DefaultValue(false)]
    public bool AutoFishCommon = false;

    [DefaultValue(false)]
    public bool AutoFishUncommon = false;

    [DefaultValue(false)]
    public bool AutoFishRare = false;

    [DefaultValue(false)]
    public bool AutoFishVeryRare = false;

    [DefaultValue(false)]
    public bool AutoFishLegendary = false;

    [DefaultValue(false)]
    public bool AutoFishQuestFish = false;

    [DefaultValue(false)]
    public bool HasSpecialPosition = false;

    // ==================== BuffBrowserTool ====================

    [Header("BuffBrowser")]
    [DefaultValue(60)]
    [Range(1, 3600)]
    [Increment(1)]
    public int BuffSeconds = 60;
}