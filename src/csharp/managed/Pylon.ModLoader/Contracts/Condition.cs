using System.Text.RegularExpressions;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>一条配方条件：描述（文本键）+ 判定函数，像 tModLoader 的 Condition；recipe.AddCondition 用。</summary>
public class Condition
{
    /// <summary>描述键 + 判定函数；predicate 不能是 null。</summary>
    public Condition(string description, Func<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        DescriptionKey = description;
        Predicate = predicate;
    }

    /// <summary>同上，描述用 LocalizedText 的 Key。</summary>
    public Condition(LocalizedText description, Func<bool> predicate)
        : this(description?.Key!, predicate)
    {
    }

    /// <summary>描述键（构造时定死）。</summary>
    public string DescriptionKey { get; }

    /// <summary>判定函数（构造时定死；IsMet 就是调它）。</summary>
    public Func<bool> Predicate { get; }

    /// <summary>当前语言的描述文字：键有本地化用本地化，否则键末段按驼峰拆开（再不行用键本身）。</summary>
    public string Description
    {
        get
        {
            var key = DescriptionKey;
            if (Language.Exists(key)) return Language.GetTextValue(key);

            var last = key[(key.LastIndexOf('.') + 1)..];
            return Regex.IsMatch(last, "^[A-Za-z]\\w*$") && key.Contains('.')
                ? Regex.Replace(last, "([a-z0-9])([A-Z])", "$1 $2")
                : key;
        }
    }

    /// <summary>当前是否满足（就是调 Predicate）。</summary>
    public bool IsMet() => Predicate();

    /// <summary>当字符串用：给描述文字。</summary>
    public override string ToString() => Description;

    /// <summary>本地玩家（现成的条件都用它做判定）。</summary>
    private static Player Me() => Main.player[Main.myPlayer];

    /// <summary>靠近水（身边有水或水槽）。</summary>
    public static readonly Condition NearWater = new("Conditions.NearWater", () => Me().adjWaterSource || Me().adjTile[TileID.Sinks]);

    /// <summary>靠近岩浆。</summary>
    public static readonly Condition NearLava = new("Conditions.NearLava", () => Me().adjLava);

    /// <summary>靠近蜂蜜。</summary>
    public static readonly Condition NearHoney = new("Conditions.NearHoney", () => Me().adjHoney);

    /// <summary>白天。</summary>
    public static readonly Condition TimeDay = new("Conditions.TimeDay", () => Main.dayTime);

    /// <summary>夜晚。</summary>
    public static readonly Condition TimeNight = new("Conditions.TimeNight", () => !Main.dayTime);

    /// <summary>在地牢。</summary>
    public static readonly Condition InDungeon = new("Conditions.InDungeon", () => Me().ZoneDungeon);

    /// <summary>在腐化之地。</summary>
    public static readonly Condition InCorrupt = new("Conditions.InCorrupt", () => Me().ZoneCorrupt);

    /// <summary>在神圣之地。</summary>
    public static readonly Condition InHallow = new("Conditions.InHallow", () => Me().ZoneHallow);

    /// <summary>在陨石坑。</summary>
    public static readonly Condition InMeteor = new("Conditions.InMeteor", () => Me().ZoneMeteor);

    /// <summary>在丛林。</summary>
    public static readonly Condition InJungle = new("Conditions.InJungle", () => Me().ZoneJungle);

    /// <summary>在雪原。</summary>
    public static readonly Condition InSnow = new("Conditions.InSnow", () => Me().ZoneSnow);

    /// <summary>在猩红之地。</summary>
    public static readonly Condition InCrimson = new("Conditions.InCrimson", () => Me().ZoneCrimson);

    /// <summary>在水蜡烛旁。</summary>
    public static readonly Condition InWaterCandle = new("Conditions.InWaterCandle", () => Me().ZoneWaterCandle);

    /// <summary>在和平蜡烛旁。</summary>
    public static readonly Condition InPeaceCandle = new("Conditions.InPeaceCandle", () => Me().ZonePeaceCandle);

    /// <summary>在沙漠。</summary>
    public static readonly Condition InDesert = new("Conditions.InDesert", () => Me().ZoneDesert);

    /// <summary>在发光蘑菇地。</summary>
    public static readonly Condition InGlowshroom = new("Conditions.InGlowshroom", () => Me().ZoneGlowshroom);

    /// <summary>在沙漠地下。</summary>
    public static readonly Condition InUndergroundDesert = new("Conditions.InUndergroundDesert", () => Me().ZoneUndergroundDesert);

    /// <summary>在天空层（ZoneSkyHeight）。</summary>
    public static readonly Condition InSkyHeight = new("Conditions.InSkyHeight", () => Me().ZoneSkyHeight);

    /// <summary>在地表层（ZoneOverworldHeight）。</summary>
    public static readonly Condition InOverworldHeight = new("Conditions.InOverworldHeight", () => Me().ZoneOverworldHeight);

    /// <summary>在泥土层（ZoneDirtLayerHeight）。</summary>
    public static readonly Condition InDirtLayerHeight = new("Conditions.InDirtLayerHeight", () => Me().ZoneDirtLayerHeight);

    /// <summary>在岩石层（ZoneRockLayerHeight）。</summary>
    public static readonly Condition InRockLayerHeight = new("Conditions.InRockLayerHeight", () => Me().ZoneRockLayerHeight);

    /// <summary>在地下世界层（ZoneUnderworldHeight）。</summary>
    public static readonly Condition InUnderworldHeight = new("Conditions.InUnderworldHeight", () => Me().ZoneUnderworldHeight);

    /// <summary>在海滩。</summary>
    public static readonly Condition InBeach = new("Conditions.InBeach", () => Me().ZoneBeach);

    /// <summary>在下雨。</summary>
    public static readonly Condition InRain = new("Conditions.InRain", () => Me().ZoneRain);

    /// <summary>在沙尘暴里。</summary>
    public static readonly Condition InSandstorm = new("Conditions.InSandstorm", () => Me().ZoneSandstorm);

    /// <summary>在撒旦军团事件里。</summary>
    public static readonly Condition InOldOneArmy = new("Conditions.InOldOneArmy", () => Me().ZoneOldOneArmy);

    /// <summary>在花岗岩洞。</summary>
    public static readonly Condition InGranite = new("Conditions.InGranite", () => Me().ZoneGranite);

    /// <summary>在大理石洞。</summary>
    public static readonly Condition InMarble = new("Conditions.InMarble", () => Me().ZoneMarble);

    /// <summary>在蜂巢。</summary>
    public static readonly Condition InHive = new("Conditions.InHive", () => Me().ZoneHive);

    /// <summary>在宝石洞。</summary>
    public static readonly Condition InGemCave = new("Conditions.InGemCave", () => Me().ZoneGemCave);

    /// <summary>在神庙里（键是 InLihzardTemple，参考就这么拼）。</summary>
    public static readonly Condition InLihzhardTemple = new("Conditions.InLihzardTemple", () => Me().ZoneLihzhardTemple);

    /// <summary>在墓地。</summary>
    public static readonly Condition InGraveyard = new("Conditions.InGraveyard", () => Me().ZoneGraveyard);

    /// <summary>在微光（以太）里（手机端没有 NearShimmer，参考就用这个）。</summary>
    public static readonly Condition InAether = new("Conditions.InAether", () => Me().ZoneShimmer);

    /// <summary>在猩红或腐化之地。</summary>
    public static readonly Condition InEvilBiome = new("Conditions.InEvilBiome", () => Me().ZoneCrimson || Me().ZoneCorrupt);

    /// <summary>不在猩红也不在腐化之地。</summary>
    public static readonly Condition NotInEvilBiome = new("Conditions.NotInEvilBiome", () => !Me().ZoneCrimson && !Me().ZoneCorrupt);

    /// <summary>不在神圣之地。</summary>
    public static readonly Condition NotInHallow = new("Conditions.NotInHallow", () => !Me().ZoneHallow);

    /// <summary>不在墓地。</summary>
    public static readonly Condition NotInGraveyard = new("Conditions.NotInGraveyard", () => !Me().ZoneGraveyard);

    /// <summary>不在地下世界。</summary>
    public static readonly Condition NotInUnderworld = new("Conditions.NotInUnderworld", () => !Me().ZoneUnderworldHeight);

    /// <summary>经典模式。</summary>
    public static readonly Condition InClassicMode = new("Conditions.InClassicMode", () => !Main.expertMode);

    /// <summary>专家模式。</summary>
    public static readonly Condition InExpertMode = new("Conditions.InExpertMode", () => Main.expertMode);

    /// <summary>大师模式。</summary>
    public static readonly Condition InMasterMode = new("Conditions.InMasterMode", () => Main.masterMode);

    /// <summary>旅行模式。</summary>
    public static readonly Condition InJourneyMode = new("Conditions.InJourneyMode", () => Main.IsJourneyMode);

    /// <summary>困难模式。</summary>
    public static readonly Condition Hardmode = new("Conditions.InHardmode", () => Main.hardMode);

    /// <summary>困难模式之前。</summary>
    public static readonly Condition PreHardmode = new("Conditions.PreHardmode", () => !Main.hardMode);

    /// <summary>砸过暗影珠。</summary>
    public static readonly Condition SmashedShadowOrb = new("Conditions.SmashedShadowOrb", () => WorldGen.shadowOrbSmashed);

    /// <summary>猩红世界。</summary>
    public static readonly Condition CrimsonWorld = new("Conditions.WorldCrimson", () => WorldGen.crimson);

    /// <summary>腐化世界。</summary>
    public static readonly Condition CorruptWorld = new("Conditions.WorldCorrupt", () => !WorldGen.crimson);

    /// <summary>醉酒世界种子。</summary>
    public static readonly Condition DrunkWorld = new("Conditions.WorldDrunk", () => Main.drunkWorld);

    /// <summary>反转过世界种子。</summary>
    public static readonly Condition RemixWorld = new("Conditions.WorldRemix", () => Main.remixWorld);

    /// <summary>"不是蜜蜂"世界种子。</summary>
    public static readonly Condition NotTheBeesWorld = new("Conditions.WorldNotTheBees", () => Main.notTheBeesWorld);

    /// <summary>"值得一试"世界种子（For the Worthy）。</summary>
    public static readonly Condition ForTheWorthyWorld = new("Conditions.WorldForTheWorthy", () => Main.getGoodWorld);

    /// <summary>十周年世界种子。</summary>
    public static readonly Condition TenthAnniversaryWorld = new("Conditions.WorldAnniversary", () => Main.tenthAnniversaryWorld);

    /// <summary>饥荒联动世界种子。</summary>
    public static readonly Condition DontStarveWorld = new("Conditions.WorldDontStarve", () => Main.dontStarveWorld);

    /// <summary>无机关世界种子。</summary>
    public static readonly Condition NoTrapsWorld = new("Conditions.WorldNoTraps", () => Main.noTrapsWorld);

    /// <summary>天顶世界种子。</summary>
    public static readonly Condition ZenithWorld = new("Conditions.WorldZenith", () => Main.zenithWorld);

    /// <summary>太空（同 InSkyHeight）。</summary>
    public static readonly Condition InSpace = InSkyHeight;

    /// <summary>地狱（同 InUnderworldHeight）。</summary>
    public static readonly Condition InUnderworld = InUnderworldHeight;
}
