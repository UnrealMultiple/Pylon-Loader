using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

internal static class NpcHeadHooks
{
    private struct Heads
    {
        public int Town;
        public int Shimmer;
        public int Boss;
    }

    private static Heads[] _heads = [];
    private static readonly List<Asset<Texture2D>> TownAssets = [];
    private static readonly List<Asset<Texture2D>> BossAssets = [];
    private static readonly Dictionary<int, int> ExplicitTownSlots = [];
    private static readonly Dictionary<int, int> ExplicitBossSlots = [];
    private static int _townBase = -1;
    private static int _bossBase = -1;
    private static bool _profileInstalled;

    internal static void Install()
    {
        On.Terraria.NPC.TypeToDefaultHeadIndex += DefaultHead;
    }

    internal static void Build()
    {
        if (!_profileInstalled)
        {
            On.Terraria.GameContent.TownNPCProfiles.GetHeadIndexSafe += EntityHead;
            _profileInstalled = true;
        }
        if (_townBase < 0) _townBase = TextureAssets.NpcHead.Length;
        if (_bossBase < 0) _bossBase = TextureAssets.NpcHeadBoss.Length;
        var content = ModContent.GetContent<ModNpc>();
        for (var i = 0; i < content.Count; i++)
        {
            var mod = content[i];
            if (mod.Type >= _heads.Length)
            {
                var previous = _heads.Length;
                Array.Resize(ref _heads, mod.Type + 1);
                for (var j = previous; j < _heads.Length; j++)
                    _heads[j] = new Heads { Town = -1, Shimmer = -1, Boss = -1 };
            }
            for (var kind = 0; kind < 3; kind++)
            {
                try
                {
                    ref var heads = ref _heads[mod.Type];
                    if (kind == 0)
                    {
                        if (!ExplicitTownSlots.ContainsKey(mod.Type))
                            Load(mod, mod.HeadTexture, "_Head", TownAssets, _townBase, ref heads.Town);
                    }
                    else if (kind == 1) Load(mod, mod.ShimmerHeadTexture, "_Shimmer_Head", TownAssets, _townBase, ref heads.Shimmer);
                    else
                    {
                        if (!ExplicitBossSlots.ContainsKey(mod.Type))
                            Load(mod, mod.BossHeadTexture, "_Head_Boss", BossAssets, _bossBase, ref heads.Boss);
                    }
                }
                catch (Exception e) { Safe.Report($"[npc] {mod.Name}.HeadTexture[{kind}]", e); }
            }
        }
        Maintain();
    }

    internal static int AddTownHead(Mod mod, int npcType, string texture)
    {
        if (ModContent.GetModNPC(npcType) is null) return -1;
        if (_townBase < 0) _townBase = TextureAssets.NpcHead.Length;

        var slot = _townBase + TownAssets.Count;
        TownAssets.Add(mod.LoadAsset(texture));
        ExplicitTownSlots[npcType] = slot;
        return slot;
    }

    internal static int AddBossHead(Mod mod, string texture, int npcType)
    {
        if (_bossBase < 0) _bossBase = TextureAssets.NpcHeadBoss.Length;

        var slot = _bossBase + BossAssets.Count;
        BossAssets.Add(mod.LoadAsset(texture));
        if (npcType >= 0) ExplicitBossSlots[npcType] = slot;
        return slot;
    }

    private static void Load(ModNpc mod, string path, string suffix, List<Asset<Texture2D>> assets, int first, ref int slot)
    {
        if (slot >= 0) return;
        var explicitPath = !string.IsNullOrEmpty(path);
        try
        {
            if (!explicitPath) path = mod.TextureSibling(suffix);
            if (string.IsNullOrEmpty(path)) return;
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
            if (!explicitPath && !File.Exists(PylonTexture.Resolve(mod.Mod, path))) return;
            var asset = mod.Mod.LoadAsset(path);
            slot = first + assets.Count;
            assets.Add(asset);
        }
        catch (Exception e) { Safe.Report($"[npc] {mod.Name}{suffix}", e); }
    }

    internal static int TownSlot(int type, bool shimmer = false)
    {
        if (!shimmer && ExplicitTownSlots.TryGetValue(type, out var explicitSlot)) return explicitSlot;
        if ((uint)type >= (uint)_heads.Length) return -1;
        var heads = _heads[type];
        return shimmer && heads.Shimmer >= 0 ? heads.Shimmer : heads.Town;
    }

    internal static int BossSlot(int type) =>
        ExplicitBossSlots.TryGetValue(type, out var slot) ? slot :
        (uint)type < (uint)_heads.Length ? _heads[type].Boss : -1;

    private static int DefaultHead(On.Terraria.NPC.orig_TypeToDefaultHeadIndex orig, int type)
    {
        var slot = TownSlot(type);
        return slot >= 0 ? slot : orig(type);
    }

    private static int EntityHead(On.Terraria.GameContent.TownNPCProfiles.orig_GetHeadIndexSafe orig, NPC npc)
    {
        if (npc is null) return orig(npc!);
        var slot = TownSlot(npc.type, npc.IsShimmerVariant);
        return slot >= 0 ? slot : orig(npc);
    }

    internal static void Maintain()
    {
        if (_townBase < 0 || _bossBase < 0) return;
        TextureAssets.NpcHead = Apply(TextureAssets.NpcHead, TownAssets, _townBase);
        TextureAssets.NpcHeadBoss = Apply(TextureAssets.NpcHeadBoss, BossAssets, _bossBase);
        var indices = Terraria.ID.NPCID.Sets.BossHeadTextures;
        for (var i = 0; i < _heads.Length && i < indices.Length; i++)
            if (_heads[i].Boss >= 0) indices[i] = _heads[i].Boss;
        foreach (var pair in ExplicitBossSlots)
            if ((uint)pair.Key < (uint)indices.Length) indices[pair.Key] = pair.Value;
    }

    private static Il2CppReferenceArray<Asset<Texture2D>> Apply(
        Il2CppReferenceArray<Asset<Texture2D>> current, List<Asset<Texture2D>> assets, int first)
    {
        var total = first + assets.Count;
        if (current.Length < total)
        {
            var grown = new Il2CppReferenceArray<Asset<Texture2D>>((long)total);
            for (var i = 0; i < current.Length; i++) grown[i] = current[i];
            current = grown;
        }
        for (var i = 0; i < assets.Count; i++) current[first + i] = assets[i];
        return current;
    }
}
