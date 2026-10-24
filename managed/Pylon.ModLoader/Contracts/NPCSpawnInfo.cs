using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>刷怪信息：刷怪点、目标玩家，加上 NPC.Spawner 的全部字段（可读可写，EditSpawnInfo 改它）。</summary>
    public class NPCSpawnInfo
    {
        /// <summary>游戏的 NPC.Spawner（EditSpawnInfo 改它，游戏就用改后的值）。</summary>
        public NPC.Spawner Spawner { get; }

        /// <summary>刷怪点的 x 格。</summary>
        public int SpawnTileX { get; }

        /// <summary>刷怪点的 y 格。</summary>
        public int SpawnTileY { get; }

        /// <summary>横向离目标玩家够远（游戏判定的 SafeRangeX）。</summary>
        public bool SafeRangeX { get; }

        /// <summary>目标玩家（多人下正确的那个，不是 LocalPlayer）。</summary>
        public Player Player { get; }

        /// <summary>这一列的地面 y 格（-1 = 还没选点）。</summary>
        public int GroundTileY { get; }

        /// <summary>地面那块 tile 的类型（-1 = 没有活跃 tile）。</summary>
        public int SpawnTileType { get; }

        /// <summary>刷怪点那格的墙类型。</summary>
        public int SpawnWallType { get; }

        /// <summary>tileX/tileY 是刷怪点（EditSpawnFlags 时 -1，游戏还没选点）；target 是目标玩家序号。</summary>
        public NPCSpawnInfo(NPC.Spawner spawner, int tileX, int tileY, int target, bool xRange = false)
        {
            Spawner = spawner;
            SpawnTileX = tileX;
            SpawnTileY = tileY;
            SafeRangeX = xRange;
            Player = Main.player[target >= 0 && target < 255 ? target : Main.myPlayer];
            GroundTileY = -1;
            SpawnTileType = -1;
            SpawnWallType = -1;
            if (tileX < 0 || tileY < 0) return;

            NPC.Spawner.FindGroundTile(tileX, tileY, out var groundTileY);
            GroundTileY = groundTileY;
            // 活跃 tile 才取类型，否则 -1。
            var ground = Main.tile.Safe(tileX, GroundTileY);
            SpawnTileType = ground.active() ? ground.type : -1;
            SpawnWallType = NPC.Spawner.GetSpawnWallType(tileX, tileY);
        }

        /// <summary>玩家在天空高度。</summary>
        public bool Sky => Player.ZoneSkyHeight;

        /// <summary>玩家在地表高度。</summary>
        public bool Surface => Player.ZoneOverworldHeight;

        /// <summary>玩家在泥土层高度。</summary>
        public bool Underground => Player.ZoneDirtLayerHeight;

        /// <summary>玩家在岩石层高度。</summary>
        public bool Cavern => Player.ZoneRockLayerHeight;

        /// <summary>玩家在地狱高度。</summary>
        public bool Underworld => Player.ZoneUnderworldHeight;

        /// <summary>地表或天空。</summary>
        public bool AboveSurface => Surface || Sky;

        /// <summary>既不在天上也不在地狱（= 地下）。</summary>
        public bool BelowSurface => !AboveSurface && !Underworld;

        /// <summary>白天。</summary>
        public bool Day => Main.dayTime;

        /// <summary>夜晚。</summary>
        public bool Night => !Main.dayTime;

        /// <summary>在下雨。</summary>
        public bool Rain => Main.raining;

        /// <summary>史莱姆雨。</summary>
        public bool SlimeRain => Main.slimeRain;

        /// <summary>血月。</summary>
        public bool BloodMoon => Main.bloodMoon;

        /// <summary>日食。</summary>
        public bool SolarEclipse => Main.eclipse;

        /// <summary>南瓜月。</summary>
        public bool PumpkinMoon => Main.pumpkinMoon;

        /// <summary>霜月。</summary>
        public bool FrostMoon => Main.snowMoon;

        /// <summary>史莱姆雨、日食、南瓜月、霜月任一。</summary>
        public bool AnyEvent => SlimeRain || SolarEclipse || PumpkinMoon || FrostMoon;

        /// <summary>困难模式。</summary>
        public bool HardMode => Main.hardMode;

        /// <summary>专家模式。</summary>
        public bool Expert => Main.expertMode;

        /// <summary>大师模式。</summary>
        public bool Master => Main.masterMode;

        /// <summary>地表腐化。</summary>
        public bool Corruption => Player.ZoneCorrupt && AboveSurface;

        /// <summary>地下腐化（泥土层或岩石层）。</summary>
        public bool UndergroundCorruption => Player.ZoneCorrupt && (Underground || Cavern);

        /// <summary>地表猩红。</summary>
        public bool Crimson => Player.ZoneCrimson && AboveSurface;

        /// <summary>地下猩红（泥土层或岩石层）。</summary>
        public bool UndergroundCrimson => Player.ZoneCrimson && (Underground || Cavern);

        /// <summary>地表神圣。</summary>
        public bool Hallow => Player.ZoneHallow && AboveSurface;

        /// <summary>地下神圣（泥土层或岩石层）。</summary>
        public bool UndergroundHallow => Player.ZoneHallow && (Underground || Cavern);

        /// <summary>地表雪地。</summary>
        public bool Snow => Player.ZoneSnow && AboveSurface;

        /// <summary>地下雪地（泥土层或岩石层）。</summary>
        public bool Ice => Player.ZoneSnow && (Underground || Cavern);

        /// <summary>地表丛林。</summary>
        public bool Jungle => Player.ZoneJungle && AboveSurface;

        /// <summary>地下丛林（泥土层或岩石层）。</summary>
        public bool UndergroundJungle => Player.ZoneJungle && (Underground || Cavern);

        /// <summary>地表蘑菇地。</summary>
        public bool SurfaceMushroom => Player.ZoneGlowshroom && Surface;

        /// <summary>地下蘑菇地（岩石层）。</summary>
        public bool Mushroom => Player.ZoneGlowshroom && Cavern;

        /// <summary>海洋。</summary>
        public bool Ocean => Player.ZoneBeach;

        /// <summary>陨石坑。</summary>
        public bool Meteor => Player.ZoneMeteor;

        /// <summary>沙漠。</summary>
        public bool Desert => Player.ZoneDesert;

        /// <summary>地下沙漠。</summary>
        public bool DesertCave => Player.ZoneUndergroundDesert;

        /// <summary>大理石洞。</summary>
        public bool Marble => Player.ZoneMarble;

        /// <summary>花岗岩洞。</summary>
        public bool Granite => Player.ZoneGranite;

        /// <summary>墓地。</summary>
        public bool Graveyard => Player.ZoneGraveyard;

        /// <summary>地牢。</summary>
        public bool Dungeon => Player.ZoneDungeon;

        /// <summary>丛林神庙。</summary>
        public bool Lihzahrd => Player.ZoneLihzhardTemple;

        /// <summary>入侵进行中。</summary>
        public bool Invasion => Main.invasionType > 0;

        /// <summary>四神柱（日耀/星旋/星云/星尘）任一附近。</summary>
        public bool AnyTower => Player.ZoneTowerSolar || Player.ZoneTowerVortex || Player.ZoneTowerNebula || Player.ZoneTowerStardust;

        /// <summary>普通刷怪情形：没有入侵、没有事件、没有柱子。</summary>
        public bool CommonEnemy => !Invasion && !AnyEvent && !AnyTower;

        /// <summary>场上活跃玩家数（Spawner 直通）。</summary>
        public int numberOfActivePlayers { get => Spawner.numberOfActivePlayers; set => Spawner.numberOfActivePlayers = value; }

        /// <summary>入侵 boss 数量到顶了（Spawner 直通）。</summary>
        public bool reachedInvasionBossCap { get => Spawner.reachedInvasionBossCap; set => Spawner.reachedInvasionBossCap = value; }

        /// <summary>目标玩家的 x 格（Spawner 直通）。</summary>
        public int pX { get => Spawner.pX; set => Spawner.pX = value; }

        /// <summary>目标玩家的 y 格（Spawner 直通）。</summary>
        public int pY { get => Spawner.pY; set => Spawner.pY = value; }

        /// <summary>目标玩家的运气值（Spawner 直通）。</summary>
        public float luck { get => Spawner.luck; set => Spawner.luck = value; }

        /// <summary>白天（Spawner 直通）。</summary>
        public bool dayTime { get => Spawner.dayTime; set => Spawner.dayTime = value; }

        /// <summary>在下雨（Spawner 直通）。</summary>
        public bool raining { get => Spawner.raining; set => Spawner.raining = value; }

        /// <summary>城镇 NPC 数量（Spawner 直通）。</summary>
        public int townNPCs { get => Spawner.townNPCs; set => Spawner.townNPCs = value; }

        /// <summary>高空刷怪位（Spawner 直通）。</summary>
        public bool skyMob { get => Spawner.skyMob; set => Spawner.skyMob = value; }

        /// <summary>禁止蠕虫（Spawner 直通）。</summary>
        public bool noWorms { get => Spawner.noWorms; set => Spawner.noWorms = value; }

        /// <summary>禁止地面蠕虫（Spawner 直通）。</summary>
        public bool noGroundWorms { get => Spawner.noGroundWorms; set => Spawner.noGroundWorms = value; }

        /// <summary>入侵者（Spawner 直通）。</summary>
        public bool invaders { get => Spawner.invaders; set => Spawner.invaders = value; }

        /// <summary>刷友好怪（Spawner 直通）。</summary>
        public bool spawnFriendly { get => Spawner.spawnFriendly; set => Spawner.spawnFriendly = value; }

        /// <summary>忽略安全墙（Spawner 直通）。</summary>
        public bool ignoreSafeWalls { get => Spawner.ignoreSafeWalls; set => Spawner.ignoreSafeWalls = value; }

        /// <summary>目标格是水（Spawner 直通）。</summary>
        public bool waterTile { get => Spawner.waterTile; set => Spawner.waterTile = value; }

        /// <summary>附近有花岗岩（Spawner 直通）。</summary>
        public bool nearGranite { get => Spawner.nearGranite; set => Spawner.nearGranite = value; }

        /// <summary>附近有大理石（Spawner 直通）。</summary>
        public bool nearMarble { get => Spawner.nearMarble; set => Spawner.nearMarble = value; }

        /// <summary>蜘蛛刷怪位（Spawner 直通）。</summary>
        public bool spawnSpider { get => Spawner.spawnSpider; set => Spawner.spawnSpider = value; }

        /// <summary>地表刷怪（Spawner 直通）。</summary>
        public bool surfaceSpawn { get => Spawner.surfaceSpawn; set => Spawner.surfaceSpawn = value; }

        /// <summary>地下沙漠刷怪位（Spawner 直通）。</summary>
        public bool spawnUndergroundDesert { get => Spawner.spawnUndergroundDesert; set => Spawner.spawnUndergroundDesert = value; }

        /// <summary>困难模式地牢（Spawner 直通）。</summary>
        public bool hardDungeon { get => Spawner.hardDungeon; set => Spawner.hardDungeon = value; }

        /// <summary>比岩石层更深（Spawner 直通）。</summary>
        public bool deeperThanRockLayer { get => Spawner.deeperThanRockLayer; set => Spawner.deeperThanRockLayer = value; }

        /// <summary>在地下（Spawner 直通）。</summary>
        public bool underGround { get => Spawner.underGround; set => Spawner.underGround = value; }

        /// <summary>在海洋（Spawner 直通）。</summary>
        public bool isOcean { get => Spawner.isOcean; set => Spawner.isOcean = value; }

        /// <summary>在海滩（Spawner 直通）。</summary>
        public bool isBeach { get => Spawner.isBeach; set => Spawner.isBeach = value; }

        /// <summary>顺着风向刷怪（Spawner 直通）。</summary>
        public bool isSpawningInWindDirection { get => Spawner.isSpawningInWindDirection; set => Spawner.isSpawningInWindDirection = value; }

        /// <summary>玩家背后是天空（Spawner 直通）。</summary>
        public bool skyBehindPlayer { get => Spawner.skyBehindPlayer; set => Spawner.skyBehindPlayer = value; }

        /// <summary>在活树里（Spawner 直通）。</summary>
        public bool livingTree { get => Spawner.livingTree; set => Spawner.livingTree = value; }

        /// <summary>双地牢刷怪规则（Spawner 直通）。</summary>
        public bool dualDungeonsSpawnRules { get => Spawner.dualDungeonsSpawnRules; set => Spawner.dualDungeonsSpawnRules = value; }

        /// <summary>在双子地牢里（Spawner 直通）。</summary>
        public bool inDualDungeon { get => Spawner.inDualDungeon; set => Spawner.inDualDungeon = value; }

        /// <summary>越界进了另一座地牢（Spawner 直通）。</summary>
        public bool tresspassingDualDungeon { get => Spawner.tresspassingDualDungeon; set => Spawner.tresspassingDualDungeon = value; }

        /// <summary>在 Remix 出生区（Spawner 直通）。</summary>
        public bool inRemixStartingArea { get => Spawner.inRemixStartingArea; set => Spawner.inRemixStartingArea = value; }

        /// <summary>对 Tim 有敌意（Spawner 直通）。</summary>
        public bool offensiveToTim { get => Spawner.offensiveToTim; set => Spawner.offensiveToTim = value; }

        /// <summary>玩家还是初始血量（Spawner 直通）。</summary>
        public bool playerHasStartingHealth { get => Spawner.playerHasStartingHealth; set => Spawner.playerHasStartingHealth = value; }

        /// <summary>腐化环境（Spawner 直通）。</summary>
        public bool ZoneCorrupt { get => Spawner.ZoneCorrupt; set => Spawner.ZoneCorrupt = value; }

        /// <summary>猩红环境（Spawner 直通）。</summary>
        public bool ZoneCrimson { get => Spawner.ZoneCrimson; set => Spawner.ZoneCrimson = value; }

        /// <summary>神圣环境（Spawner 直通）。</summary>
        public bool ZoneHallow { get => Spawner.ZoneHallow; set => Spawner.ZoneHallow = value; }

        /// <summary>丛林环境（Spawner 直通）。</summary>
        public bool ZoneJungle { get => Spawner.ZoneJungle; set => Spawner.ZoneJungle = value; }

        /// <summary>雪地环境（Spawner 直通）。</summary>
        public bool ZoneSnow { get => Spawner.ZoneSnow; set => Spawner.ZoneSnow = value; }

        /// <summary>发光蘑菇环境（Spawner 直通）。</summary>
        public bool ZoneGlowshroom { get => Spawner.ZoneGlowshroom; set => Spawner.ZoneGlowshroom = value; }

        /// <summary>陨石环境（Spawner 直通）。</summary>
        public bool ZoneMeteor { get => Spawner.ZoneMeteor; set => Spawner.ZoneMeteor = value; }

        /// <summary>墓地环境（Spawner 直通）。</summary>
        public bool ZoneGraveyard { get => Spawner.ZoneGraveyard; set => Spawner.ZoneGraveyard = value; }

        /// <summary>地牢环境（Spawner 直通）。</summary>
        public bool ZoneDungeon { get => Spawner.ZoneDungeon; set => Spawner.ZoneDungeon = value; }

        /// <summary>丛林神庙环境（Spawner 直通）。</summary>
        public bool ZoneLihzhardTemple { get => Spawner.ZoneLihzhardTemple; set => Spawner.ZoneLihzhardTemple = value; }

        /// <summary>花岗岩环境（Spawner 直通）。</summary>
        public bool ZoneGranite { get => Spawner.ZoneGranite; set => Spawner.ZoneGranite = value; }

        /// <summary>大理石环境（Spawner 直通）。</summary>
        public bool ZoneMarble { get => Spawner.ZoneMarble; set => Spawner.ZoneMarble = value; }

        /// <summary>沙尘暴环境（Spawner 直通）。</summary>
        public bool ZoneSandstorm { get => Spawner.ZoneSandstorm; set => Spawner.ZoneSandstorm = value; }

        /// <summary>日耀柱环境（Spawner 直通）。</summary>
        public bool ZoneTowerSolar { get => Spawner.ZoneTowerSolar; set => Spawner.ZoneTowerSolar = value; }

        /// <summary>星旋柱环境（Spawner 直通）。</summary>
        public bool ZoneTowerVortex { get => Spawner.ZoneTowerVortex; set => Spawner.ZoneTowerVortex = value; }

        /// <summary>星云柱环境（Spawner 直通）。</summary>
        public bool ZoneTowerNebula { get => Spawner.ZoneTowerNebula; set => Spawner.ZoneTowerNebula = value; }

        /// <summary>星尘柱环境（Spawner 直通）。</summary>
        public bool ZoneTowerStardust { get => Spawner.ZoneTowerStardust; set => Spawner.ZoneTowerStardust = value; }

        /// <summary>撒旦军队环境（Spawner 直通）。</summary>
        public bool ZoneOldOneArmy { get => Spawner.ZoneOldOneArmy; set => Spawner.ZoneOldOneArmy = value; }

        /// <summary>水蜡烛环境（Spawner 直通）。</summary>
        public bool ZoneWaterCandle { get => Spawner.ZoneWaterCandle; set => Spawner.ZoneWaterCandle = value; }

        /// <summary>和平蜡烛环境（Spawner 直通）。</summary>
        public bool ZonePeaceCandle { get => Spawner.ZonePeaceCandle; set => Spawner.ZonePeaceCandle = value; }

        /// <summary>暗影蜡烛环境（Spawner 直通）。</summary>
        public bool ZoneShadowCandle { get => Spawner.ZoneShadowCandle; set => Spawner.ZoneShadowCandle = value; }

        /// <summary>默认目标玩家序号（Spawner 直通）。</summary>
        public int defaultTarget { get => Spawner.defaultTarget; set => Spawner.defaultTarget = value; }
    }
}
