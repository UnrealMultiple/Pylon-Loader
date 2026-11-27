using System.ComponentModel;
using System.Reflection;
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

    /// <summary>
    /// 颜色字段是 <c>0xAARRGGBB</c> 的 <see cref="uint"/>。旧版本框架把没有 <c>[Range]</c>
    /// 的整数配置一律按 0..100 钳制，颜色因此被存成 <c>100</c>（alpha=0，整片透明）——
    /// ESP / HUD / 弹道预测的绘制就是这样「完全不可见」的。
    /// 颜色 alpha=0 没有合法用途（等于不画），读盘后统一回落到该字段的 <c>[DefaultValue]</c>。
    /// 框架侧已修正 uint 的默认上限（ConfigOption），这里只负责修复旧存档。
    /// </summary>
    public override void OnLoaded()
    {
        FixLegacyColors();
        FixLegacyHudIcons();
    }

    /// <summary>
    /// 图标 ID 的旧错误默认值（29/109/1558，或上次被错误钳成 100 的值）→ 按 ItemID 实值改正。
    /// 只认这几个确切旧值，用户自己调过的其它值一律不动。
    /// </summary>
    private void FixLegacyHudIcons()
    {
        if (HudHealthIconId is 29 or 100) HudHealthIconId = 58;
        if (HudManaIconId is 109 or 100) HudManaIconId = 184;
        if (HudDefenseIconId is 1558 or 100) HudDefenseIconId = 156;
    }

    /// <summary>颜色字段旧存档修复（见类注释）。</summary>
    private void FixLegacyColors()
    {
        var fields = typeof(CheatConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.FieldType != typeof(uint)) continue;
            if (field.GetValue(this) is not uint value || value >> 24 != 0) continue;
            if (field.GetCustomAttribute<DefaultValueAttribute>()?.Value is uint fallback && fallback >> 24 != 0)
                field.SetValue(this, fallback);
        }
    }

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

    // ==================== CameraScaleTool ====================

    [Header("CameraScale")]
    [DefaultValue(false)]
    public bool CameraScaleEnabled = false;

    [DefaultValue(1f)]
    [Range(0.1f, 20f)]
    [Increment(0.01f)]
    public float ZoomScale = 1f;

    /// <summary>get_MaxPixelScale 的替代返回值（原版默认 7.5）。</summary>
    [DefaultValue(7.5f)]
    [Range(1f, 20f)]
    [Increment(0.1f)]
    public float MaxPixelScale = 7.5f;

    [DefaultValue(true)]
    public bool SyncViewSize = true;

    // ==================== SyncChestSizeTool ====================

    [Header("SyncChestSize")]
    [DefaultValue(false)]
    public bool ChestExploit = false;

    // ==================== NPCDamageDebugTool ====================

    [Header("NPCDamageDebug")]
    [DefaultValue(false)]
    public bool NpcImmortal = false;

    // ==================== GemStaffForm2Tool ====================

    /// <summary>原版默认开启（常驻后台），按「智能交互」键在形态 1/2 间切换。</summary>
    [DefaultValue(true)]
    public bool GemStaffForm2Enabled = true;

    // ==================== NPCSpawnTool ====================

    /// <summary>点击图标生成的数量（原版 m_spawnCount，1~200）。</summary>
    [DefaultValue(1)]
    [Range(1, 200)]
    [Increment(1)]
    public int NpcSpawnCount = 1;

    // ==================== NPCSpawnSettingsTool ====================

    [DefaultValue(false)]
    public bool ModifySpawn = false;

    [DefaultValue(600)]
    [Range(1, 1000)]
    [Increment(1)]
    public int DefaultSpawnRate = 600;

    [DefaultValue(5)]
    [Range(0, 200)]
    [Increment(1)]
    public int DefaultMaxSpawns = 5;

    // ==================== NPCTool（屠杀NPC） ====================

    [DefaultValue(false)]
    public bool AutoButcherNpc = false;

    // ==================== PlayerButcherTool ====================

    [DefaultValue(1000)]
    [Range(1, 32767)]
    [Increment(1)]
    public int ButcherDamage = 1000;

    [DefaultValue(false)]
    public bool AutoButcherPlayer = false;

    // ==================== ItemBrowserTool / ModItemsTool ====================

    /// <summary>点击图标生成时的堆叠/数量（1~9999）。</summary>
    [DefaultValue(1)]
    [Range(1, 9999)]
    [Increment(1)]
    public int ItemGiveStack = 1;

    // ==================== WorldEditBrushTool ====================

    /// <summary>触摸拖动按住持续刷（原版 WorldTapBrush）。</summary>
    [DefaultValue(false)]
    public bool WorldTapBrush = false;

    [DefaultValue(16)]
    [Range(16, 800)]
    [Increment(8)]
    public int BrushDiameter = 16;

    [DefaultValue(true)]
    public bool DrawDetailedPreview = true;

    [DefaultValue(true)]
    public bool SquareFrame = true;

    [DefaultValue(false)]
    public bool TeleportToTilesFarAway = false;

    [DefaultValue(false)]
    public bool RevealMap = false;

    /// <summary>0=无 1=破坏 2=放置 3=替换</summary>
    [DefaultValue(0)]
    [Range(0, 3)]
    [Increment(1)]
    public int BrushTileAction = 0;

    [DefaultValue(0)]
    [Range(0, 3)]
    [Increment(1)]
    public int BrushWallAction = 0;

    /// <summary>0=无 1=移除 2=水 3=岩浆 4=蜂蜜 5=微光</summary>
    [DefaultValue(0)]
    [Range(0, 5)]
    [Increment(1)]
    public int BrushLiquidAction = 0;

    // ==================== WorldEditCopyPasteTool ====================

    [DefaultValue(false)]
    public bool CopyPasteEnabled = false;

    /// <summary>0=直写 (SendTileRect) 1=发包 (TileManipulation)</summary>
    [DefaultValue(0)]
    [Range(0, 1)]
    [Increment(1)]
    public int PastePlaceMode = 0;

    [DefaultValue(true)]
    public bool PasteClearAfter = true;

    [DefaultValue(600)]
    [Range(60, 1800)]
    [Increment(60)]
    public int PasteTilesPerSecond = 600;

    // ==================== ESPTool ====================

    [DefaultValue(false)]
    public bool EspEnabled = false;

    [DefaultValue(true)]
    public bool EspPlayerBoxes = true;

    [DefaultValue(true)]
    public bool EspNpcBoxes = true;

    [DefaultValue(false)]
    public bool EspProjectileBoxes = false;

    [DefaultValue(false)]
    public bool EspPlayerTracers = false;

    [DefaultValue(false)]
    public bool EspNpcTracers = false;

    [DefaultValue(true)]
    public bool EspDrawTownNpcs = true;

    /// <summary>
    /// 颜色取 ESPTool.hpp 的 <c>ImVec4</c> 默认值（rgba→0xAARRGGBB）：
    /// 本机绿 (0,1,0,1) / 其他红 (1,.31,.31,1) / NPC 橙 (1,.71,0,1) /
    /// 弹幕紫 (.78,0,1,1) / 玩家连线白 (1,1,1,.78) / NPC 连线橙黄 (1,.78,.31,.63)。
    /// </summary>
    [DefaultValue(0xFF00FF00u)]
    public uint EspLocalPlayerColor = 0xFF00FF00u;

    [DefaultValue(0xFFFF4F4Fu)]
    public uint EspOtherPlayerColor = 0xFFFF4F4Fu;

    [DefaultValue(0xFFFFB500u)]
    public uint EspNpcColor = 0xFFFFB500u;

    [DefaultValue(0xFFC700FFu)]
    public uint EspProjectileColor = 0xFFC700FFu;

    [DefaultValue(0xC7FFFFFFu)]
    public uint EspPlayerTracerColor = 0xC7FFFFFFu;

    [DefaultValue(0xA1FFC74Fu)]
    public uint EspNpcTracerColor = 0xA1FFC74Fu;

    // ==================== PlayerHudTool ====================

    [DefaultValue(false)]
    public bool HudShowSelf = false;

    [DefaultValue(false)]
    public bool HudShowOthers = false;

    [DefaultValue(true)]
    public bool HudShowWeapon = true;

    [DefaultValue(true)]
    public bool HudShowHealth = true;

    [DefaultValue(true)]
    public bool HudShowMana = true;

    [DefaultValue(true)]
    public bool HudShowDefense = true;

    [DefaultValue(1f)]
    [Range(0.4f, 3f)]
    [Increment(0.05f)]
    public float HudScale = 1f;

    [DefaultValue(80f)]
    [Range(0f, 300f)]
    [Increment(5f)]
    public float HudHeadOffset = 80f;

    /// <summary>
    /// 图标物品 id：原始 C++ 用的是 <c>ItemID::Heart / ItemID::Star / ItemID::CobaltShield</c>，
    /// 按本机 dump（refs/dump.cs，ItemID 类）实值为 58 / 184 / 156。
    /// 旧移植版误写成 29（生命水晶）/ 109（魔力水晶）/ 1558（另一个物品）——
    /// 这就是「魔力/防御贴图不对」的原因。
    /// </summary>
    [DefaultValue(58)]
    public int HudHealthIconId = 58;

    [DefaultValue(184)]
    public int HudManaIconId = 184;

    [DefaultValue(156)]
    public int HudDefenseIconId = 156;

    [DefaultValue(0xC8000000u)]
    public uint HudBackgroundColor = 0xC8000000u;

    [DefaultValue(0xFFFFFFFFu)]
    public uint HudTextColor = 0xFFFFFFFFu;

    [DefaultValue(0xFFFFFFFFu)]
    public uint HudBorderColor = 0xFFFFFFFFu;

    [DefaultValue(0x40FFFFFFu)]
    public uint HudDividerColor = 0x40FFFFFFu;

    // ==================== ProjectilePredictionTool ====================

    [DefaultValue(false)]
    public bool PredEnabled = false;

    [DefaultValue(true)]
    public bool PredDrawFriendly = true;

    [DefaultValue(true)]
    public bool PredDrawHostile = true;

    [DefaultValue(1000)]
    [Range(100, 5000)]
    [Increment(50)]
    public int PredMaxStepCount = 1000;

    [DefaultValue(0x5050FF50u)]
    public uint PredFriendlyColor = 0x5050FF50u;

    [DefaultValue(0x50FF5050u)]
    public uint PredHostileColor = 0x50FF5050u;

    // ==================== ProjectileTool（附加弹幕） ====================

    [DefaultValue(false)]
    public bool ApplyProjectile = false;

    /// <summary>手持武器 → 弹幕列表 的 JSON 序列化（Dictionary&lt;int, List&lt;Bullet&gt;&gt;）。</summary>
    [DefaultValue("")]
    public string ProjectileConfigJson = "";
}