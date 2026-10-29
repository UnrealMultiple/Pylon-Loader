using System;
using System.Reflection;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Personalities;

namespace Pylon.ModLoader
{
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

        // biome 传 ModBiome 类的那只入口没翻：要 BiomeLoader/ModBiome，Pylon 侧还没有。
        /// <summary>它（NpcType）对某原版群系的好感（'Forest'、'Desert'…）；链式。</summary>
        public NPCHappiness SetBiomeAffection(string biome, AffectionLevel level)
        {
            // 名字末尾没有 'Biome' 就补上，再按名字建游戏群系。
            var name = biome.EndsWith("Biome") ? biome : biome + "Biome";
            AShoppingBiome shopping;
            switch (name)
            {
                case "ForestBiome": shopping = new ForestBiome(); break;
                case "DesertBiome": shopping = new DesertBiome(); break;
                case "SnowBiome": shopping = new SnowBiome(); break;
                case "JungleBiome": shopping = new JungleBiome(); break;
                case "OceanBiome": shopping = new OceanBiome(); break;
                case "UndergroundBiome": shopping = new UndergroundBiome(); break;
                case "HallowBiome": shopping = new HallowBiome(); break;
                case "MushroomBiome": shopping = new MushroomBiome(); break;
                case "DungeonBiome": shopping = new DungeonBiome(); break;
                case "CorruptionBiome": shopping = new CorruptionBiome(); break;
                case "CrimsonBiome": shopping = new CrimsonBiome(); break;
                default: throw new ArgumentException("SetBiomeAffection: " + biome + " nao e um bioma do jogo");
            }

            // 偏好装进清单，注册的是清单。
            var preference = new BiomePreferenceListTrait.BiomePreference(level, shopping);
            var list = new BiomePreferenceListTrait();
            list.Add(preference);

            Register(NpcType, list);
            return this;
        }

        // 真写入（挂进 PersonalityDatabase）是管线的事：IShopPersonalityTrait 从 C# 静态类型过不去（两个 trait 都 implements 它），等 Harmony 按指针接（docs/19 §6）。
        /// <summary>把偏好写进游戏的人物偏好库（当前为管线占位，不落库）。</summary>
        private static void Register(int npcType, object trait) { }
    }
}