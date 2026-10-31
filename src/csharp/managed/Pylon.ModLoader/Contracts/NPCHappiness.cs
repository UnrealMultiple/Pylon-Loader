using Terraria.GameContent.Personalities;

namespace Pylon.ModLoader;

/// <summary>城镇 NPC 的幸福度偏好（SetStaticDefaults 里 new 出来）：Set 的偏好当场写进游戏的人物偏好库，算心情和物价用。</summary>
public class NPCHappiness
{
    /// <summary>这家 NPC 的类型：后面的 Set 都是写"它"对别人/群系的好感。</summary>
    public NPCHappiness(int npcType)
    {
        NpcType = npcType;
    }

    /// <summary>拥有这组偏好的 NPC 类型。</summary>
    public int NpcType { get; }

    /// <summary>它（NpcType）对另一个 NPC（npcType）的好感；链式。</summary>
    public NPCHappiness SetNPCAffection(int npcType, AffectionLevel level)
    {
        var trait = new NPCPreferenceTrait();
        trait.Level = level;
        trait.NpcId = npcType;

        Register(NpcType, trait);
        return this;
    }

    /// <summary>它（NpcType）对某原版群系的好感（'Forest'、'Desert'…）；链式。</summary>
    public NPCHappiness SetBiomeAffection(string biome, AffectionLevel level)
    {
        // 名字末尾没有 'Biome' 就补上，再按名字建游戏群系。
        var name = biome.EndsWith("Biome") ? biome : biome + "Biome";
        AShoppingBiome shopping = name switch
        {
            "ForestBiome" => new ForestBiome(),
            "DesertBiome" => new DesertBiome(),
            "SnowBiome" => new SnowBiome(),
            "JungleBiome" => new JungleBiome(),
            "OceanBiome" => new OceanBiome(),
            "UndergroundBiome" => new UndergroundBiome(),
            "HallowBiome" => new HallowBiome(),
            "MushroomBiome" => new MushroomBiome(),
            "DungeonBiome" => new DungeonBiome(),
            "CorruptionBiome" => new CorruptionBiome(),
            "CrimsonBiome" => new CrimsonBiome(),
            _ => throw new ArgumentException("SetBiomeAffection: " + biome + " nao e um bioma do jogo")
        };

        // 偏好装进清单，注册的是清单。
        var preference = new BiomePreferenceListTrait.BiomePreference(level, shopping);
        var list = new BiomePreferenceListTrait();
        list.Add(preference);

        Register(NpcType, list);
        return this;
    }

    // 真写入（挂进 PersonalityDatabase）是管线的事：IShopPersonalityTrait 从 C# 静态类型过不去
    // （两个 trait 都 implements 它），等 Harmony 按指针接。
    /// <summary>把偏好写进游戏的人物偏好库（当前为管线占位，不落库）。</summary>
    private static void Register(int npcType, object trait) { }
}
