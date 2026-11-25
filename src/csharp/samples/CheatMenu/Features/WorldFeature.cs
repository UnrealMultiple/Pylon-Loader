using System;
using System.IO;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.Map;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 世界功能。1:1 对应原版 <c>WorldTool</c>（子页「常用」）：
/// 点亮地图（多人先请求区块再全量 Update(255)）/ 世界传送（TriggerPing → Teleport）/
/// 时间设置（滑条 + 锁定）/ 世界事件（17 种一键生成与停止）。
/// 区块计数走 DecompressTileBlock_Inner hook，换世界由 ModSystem.ClearWorld 重置。
/// </summary>
public sealed class WorldFeature : CheatFeature
{
    private static readonly string[] InvasionNames =
    [
        "哥布林入侵", "雪人入侵", "海盗入侵", "火星人", "南瓜月", "霜月", "陨石",
        "满月", "血月", "日食", "沙尘暴", "雨天", "流星雨", "大风天",
        "史莱姆雨", "硬币雨", "灯笼夜",
    ];

    // 原版 Invasion 枚举：goblin=1 起，与下拉框序号 +1 对齐。
    private const int InvasionBase = 1;

    private static int _invasionIndex;

    // ==================== 点亮地图（区块计数） ====================
    private static bool[] _mapLoad;
    private static int _loadedSections;
    private static int _totalSections, _sectionsX, _sectionsY;
    private static bool _mapReady;
    private static bool _mapLoading;
    private static double _mapStartTime, _lastProgressToast;

    public override string Id => "world.world";
    public override string Title => "世界";
    public override CheatCategory Category => CheatCategory.World;
    public override string SubPage => "常用";

    public override void Install()
    {
        On.Terraria.NetMessage.DecompressTileBlock_Inner += DecompressTileBlock;
        On.Terraria.Main.TriggerPing += TriggerPing;
    }

    /// <summary>换世界（单机读档 / 多人收到世界数据）时重置区块记录，状态按新世界重建。</summary>
    public void OnWorldChange()
    {
        _mapReady = false;
        _mapLoading = false;
    }

    private static void DecompressTileBlock(On.Terraria.NetMessage.orig_DecompressTileBlock_Inner orig,
        Il2CppSystem.IO.BinaryReader reader, int xStart, int yStart, int width, int height)
    {
        if (xStart % 200 == 0 && yStart % 150 == 0 && width == 200 && height == 150)
            OnSectionReceived(xStart / 200, yStart / 150);   // 持续记录收到区块（含换世界重置）
        orig(reader, xStart, yStart, width, height);
    }

    private static void TriggerPing(On.Terraria.Main.orig_TriggerPing orig, Vector2 pos)
    {
        // 原版 MapTeleport：全屏地图上点 ping 直接传送过去（pos 是图格坐标，Teleport 前乘 16）。
        var player = Game.LocalPlayer;
        if (CheatFeatureHost.Config is { MapTeleport: true } && player is not null)
            player.Teleport(new Vector2(pos.X * 16f, pos.Y * 16f), 1, 0);
        orig(pos);
    }

    // ==================== 点亮地图 ====================

    private static void InitSections()
    {
        _sectionsX = Main.maxTilesX / 200;
        _sectionsY = Main.maxTilesY / 150;
        _totalSections = _sectionsX * _sectionsY;
        if (_sectionsX <= 0 || _sectionsY <= 0) return;
        _mapLoad = new bool[_totalSections];
        _loadedSections = 0;
        _mapReady = true;
    }

    private static void OnSectionReceived(int secX, int secY)
    {
        if (!_mapReady) InitSections();
        if (!_mapReady) return;
        var index = secX * _sectionsY + secY;
        if (index >= 0 && index < _totalSections && !_mapLoad[index])
        {
            _mapLoad[index] = true;
            _loadedSections++;
        }
    }

    private static void LightMap()
    {
        if (Main.netMode == 1)
        {
            // 保留 hook 已记录的区块计数（不重置），只发起请求所有区块
            _mapLoading = true;
            _mapStartTime = Environment.TickCount64 / 1000.0;
            _lastProgressToast = 0.0;
            RequestTiles();
        }
        else
        {
            LightUpMapNow();
        }
    }

    /// <summary>原版 Main::RequestTiles：对每个区块发 159 号包请求服务器下发。</summary>
    private static void RequestTiles()
    {
        var maxX = Main.maxSectionsX;
        var maxY = Main.maxSectionsY;
        for (var x = 0; x < maxX; x++)
        {
            for (var y = 0; y < maxY; y++)
            {
                NetMessage.SendData(159, -1, -1, null, x, y, 0f, 0f, 0, 0, 0);
            }
        }
    }

    /// <summary>原版 Main::LightUpMap：全量把 WorldMap 每格点亮到 255（不依赖区块是否已加载）。</summary>
    private static void LightUpMapNow()
    {
        var maxX = Main.maxTilesX;
        var maxY = Main.maxTilesY;
        var map = Main.Map;
        if (map is null) return;
        for (var x = 0; x < maxX; x++)
        {
            for (var y = 0; y < maxY; y++)
            {
                map.Update(x, y, 255);
            }
        }
        Main.refreshMap = true;
    }

    private static void CheckMapLoad()
    {
        if (!_mapLoading) return;
        if (Main.gameMenu)
        {
            _mapLoading = false;      // 退出服务器 / 回到菜单：停止点亮
            return;
        }
        if (!_mapReady) return;       // 尚未收到任何区块，继续等待

        var now = Environment.TickCount64 / 1000.0;
        if (now - _mapStartTime >= 60.0)
        {
            ModernUI.Toast("区块加载超时，点亮已加载区域");
            LightUpMapNow();
            _mapLoading = false;
            return;
        }
        if (_loadedSections >= _totalSections)
        {
            ModernUI.Toast("区块加载完成，正在点亮地图.....");
            LightUpMapNow();
            _mapLoading = false;
            return;
        }
        if (now - _lastProgressToast >= 1.0)
        {
            ModernUI.Toast($"正在从服务器加载区块: {_loadedSections}/{_totalSections}");
            _lastProgressToast = now;
        }
    }

    // ==================== 时间 ====================

    private static void SetTimeInHours(float hours)
    {
        hours -= 4.5f;                    // Terraria 时间从凌晨 4:30 开始
        while (hours < 0.0f) hours += 24.0f;
        hours %= 24.0f;
        if (hours < 15.0f)                // 白天
        {
            Main.dayTime = true;
            Main.time = hours * 3600.0;
        }
        else                              // 夜晚
        {
            Main.dayTime = false;
            Main.time = (hours - 15.0f) * 3600.0;
        }
    }

    /// <summary>读取当前游戏时间（小时数，0~24）。</summary>
    private static float GetTimeInHours()
    {
        var gameTime = Main.time;
        var isDay = Main.dayTime;
        var converted = (float)(gameTime / 60.0 / 60.0 + (isDay ? 4.5 : 19.5));
        while (converted < 0.0f) converted += 24.0f;
        converted %= 24.0f;
        return converted;
    }

    /// <summary>锁定时间：每帧把时间拨回锁定值。</summary>
    private static void ApplyTimeLock()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is not { LockTime: true }) return;
        Main.time = cfg.LockedTime;
        Main.dayTime = cfg.LockedDayTime;
    }

    // ==================== 世界事件 ====================

    private static void StartInvasion(int type)
    {
        switch ((InvasionType)type)
        {
            case InvasionType.Meteor:
                WorldGen.spawnMeteor = true;
                WorldGen.dropMeteor(false);
                break;
            case InvasionType.FullMoon:
                Main.dayTime = false; Main.moonPhase = 0; Main.time = 0;
                break;
            case InvasionType.BloodMoon:
                Main.dayTime = false; Main.bloodMoon = true; Main.time = 0;
                break;
            case InvasionType.Eclipse:
                Main.dayTime = true; Main.eclipse = true; Main.time = 0;
                break;
            case InvasionType.Sandstorm:
                Terraria.GameContent.Events.Sandstorm.StartSandstorm();
                break;
            case InvasionType.Rain:
                Main.StartRain(false, null, false);
                break;
            case InvasionType.MeteorShower:
                WorldGen.StartMeteorShower();
                break;
            case InvasionType.PumpkinMoon:
                Main.dayTime = false; Main.pumpkinMoon = true; Main.time = 0;
                Main.bloodMoon = false; NPC.waveKills = 0; NPC.waveNumber = 1;
                break;
            case InvasionType.FrostMoon:
                Main.dayTime = false; Main.snowMoon = true; Main.time = 0;
                Main.bloodMoon = false; NPC.waveKills = 0; NPC.waveNumber = 1;
                break;
            case InvasionType.Wind:
                Main.windSpeedCurrent = 1f; Main.windSpeedTarget = 1f;
                break;
            case InvasionType.SlimeRain:
                Main.StartSlimeRain(true);
                break;
            case InvasionType.CoinRain:
                Main.StartRain(false, null, true);
                break;
            case InvasionType.LanternNight:
                if (!Terraria.GameContent.Events.LanternNight.LanternsUp)
                    Terraria.GameContent.Events.LanternNight.ToggleManualLanterns();
                break;
            default:                  // 哥布林/雪人/海盗/火星人/南瓜月/霜月走原版入侵
                Main.StartInvasion(type);
                Main.invasionSize = 100;
                Main.invasionSizeStart = 100;
                break;
        }
    }

    private static void StopInvasion()
    {
        Main.bloodMoon = false;
        Main.eclipse = false;
        Terraria.GameContent.Events.Sandstorm.StopSandstorm();
        Main.StopRain(true);
        WorldGen.meteorShowerCount = 0;
        Main.pumpkinMoon = false;
        Main.snowMoon = false;
        Main.windSpeedCurrent = 0f;
        Main.windSpeedTarget = 0f;
        Main.StopSlimeRain(true);
        Main.invasionSize = 0;
        Main.invasionSizeStart = 0;
        if (Terraria.GameContent.Events.LanternNight.LanternsUp)
            Terraria.GameContent.Events.LanternNight.ToggleManualLanterns();
    }

    /// <summary>原版 Invasion 枚举的 C# 侧对应（值与原版一致，goblin=1 起）。</summary>
    private enum InvasionType
    {
        Goblin = 1, Snowman, Pirate, Martian, PumpkinMoon, FrostMoon, Meteor,
        FullMoon, BloodMoon, Eclipse, Sandstorm, Rain, MeteorShower, Wind,
        SlimeRain, CoinRain, LanternNight,
    }

    public override void Tick()
    {
        ApplyTimeLock();
        CheckMapLoad();
    }

    // ==================== UI（三个分区，文案与原版一致） ====================

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("世界功能", "WORLD");

        // ---- 地图与传送 ----
        if (ModernUI.PrimaryButton("点亮地图"))
        {
            LightMap();
        }
        RowToggle("世界传送", "##maptp", cfg.MapTeleport, v => cfg.MapTeleport = v);

        // ---- 时间设置 ----
        ModernUI.SectionTitle("时间设置");
        var hours = GetTimeInHours();
        var fmt = $"{(int)hours:D2}:{(int)((hours - (int)hours) * 60):D2}";
        ModernUI.BeginRow();
        ModernUI.RowLabel("当前时间", $"{fmt} ({(Main.dayTime ? "白天" : "夜晚")})");
        ModernUI.EndRow();
        var target = hours;
        if (ModernUI.RowSlider("##time", "时间", "0-24", ref target, 0f, 24f, "0.0"))
        {
            SetTimeInHours(target);
            if (cfg.LockTime)          // 锁定中拖动滑条时同步更新锁定值
            {
                cfg.LockedTime = Main.time;
                cfg.LockedDayTime = Main.dayTime;
                CheatFeatureHost.MarkDirty();
            }
        }
        RowToggle("锁定当前时间", "##locktime", cfg.LockTime, v =>
        {
            cfg.LockTime = v;
            if (v)                     // 开启时捕获当前时间
            {
                cfg.LockedTime = Main.time;
                cfg.LockedDayTime = Main.dayTime;
            }
        });

        // ---- 世界事件 ----
        ModernUI.SectionTitle("世界事件");
        ModernUI.BeginRow();
        ModernUI.RowLabel("事件类型");
        if (ModernUI.ComboField("##invasion", InvasionNames, ref _invasionIndex))
            _invasionIndex = Math.Clamp(_invasionIndex, 0, InvasionNames.Length - 1);
        ModernUI.EndRow();

        ModernUI.BeginRow();
        if (ModernUI.GhostButton("生成事件"))
        {
            StartInvasion(_invasionIndex + InvasionBase);
        }
        if (ModernUI.GhostButton("停止事件"))
        {
            StopInvasion();
        }
        ModernUI.EndRow();

        ModernUI.EndCard();
    }

    private static void RowToggle(string label, string id, bool value, Action<bool> set)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        if (ModernUI.Toggle(id, value))
        {
            set(!value);
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();
    }
}
