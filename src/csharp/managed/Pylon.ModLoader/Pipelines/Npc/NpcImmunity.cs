using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>NPCID.Sets 的负面效果免疫表：按类型/负面效果号汇总作者写入的值，SetDefaults 时落进 npc.buffImmune。</summary>
internal static class NpcImmunity
{
    private const int WhipTagA = 310;
    private const int WhipTagB = 362;
    private const int BuffShimmer = 353;
    private const int BuffOnFire = 24;
    private const int BuffOnFire3 = 323;
    private const int BuffFrostburn = 44;
    private const int BuffFrostburn2 = 324;
    private const int BuffPoisoned = 20;
    private const int BuffVenom = 70;

    private static bool[] _whips = [];
    private static bool[] _regular = [];
    private static bool?[][] _specific = [];
    private static bool[] _tags = [];
    private static List<int>[] _parents = [];
    private static int _filled;

    internal static ref bool[] Whips
    {
        get
        {
            Resize();
            return ref _whips;
        }
    }

    internal static ref bool[] Regular
    {
        get
        {
            Resize();
            return ref _regular;
        }
    }

    internal static ref bool?[][] Specific
    {
        get
        {
            Resize();
            return ref _specific;
        }
    }

    internal static ref bool[] TagBuffs
    {
        get
        {
            Resize();
            return ref _tags;
        }
    }

    internal static ref List<int>[] ImmunityParents
    {
        get
        {
            Resize();
            return ref _parents;
        }
    }

    private static void Resize()
    {
        var npcCount = ModContent.NextType(ContentKind.Npc);
        var buffCount = ModContent.NextType(ContentKind.Buff);
        var old = _specific.Length;
        if (old == npcCount && (old == 0 || _specific[0].Length == buffCount)) return;

        var whips = new bool[npcCount];
        var regular = new bool[npcCount];
        var specific = new bool?[npcCount][];
        Array.Copy(_whips, whips, Math.Min(old, npcCount));
        Array.Copy(_regular, regular, Math.Min(old, npcCount));

        var tags = new bool[buffCount];
        var parents = new List<int>[buffCount];
        Array.Copy(_tags, tags, Math.Min(_tags.Length, buffCount));
        Array.Copy(_parents, parents, Math.Min(_parents.Length, buffCount));
        for (var i = 0; i < buffCount; i++) parents[i] ??= [];
        if (_tags.Length == 0)
        {
            tags[WhipTagA] = true;
            tags[WhipTagB] = true;
            parents[BuffOnFire3].Add(BuffOnFire);
            parents[BuffOnFire].Add(BuffOnFire3);
            parents[BuffFrostburn2].Add(BuffFrostburn);
            parents[BuffFrostburn].Add(BuffFrostburn2);
            parents[BuffPoisoned].Add(BuffVenom);
        }

        for (var type = 0; type < npcCount; type++)
        {
            bool?[] row = type < old ? _specific[type] : new bool?[buffCount];
            Array.Resize(ref row, buffCount);
            specific[type] = row;
            if (type < _filled) continue;
            Fill(type, whips, regular, row, buffCount);
        }

        _whips = whips;
        _regular = regular;
        _specific = specific;
        _tags = tags;
        _parents = parents;
        _filled = npcCount;
    }

    private static void Fill(int type, bool[] whips, bool[] regular, bool?[] row, int buffCount)
    {
        if (type >= VanillaCounts.Npc) return;
        try
        {
            var table = NPCID.Sets.DebuffImmunitySets;
            if (table is null || table.Pointer == 0 || !table.ContainsKey(type)) return;

            var data = table[type];
            if (data is null || data.Pointer == 0) return;

            whips[type] = data.ImmuneToWhips;
            regular[type] = data.ImmuneToAllBuffsThatAreNotWhips;

            var specific = data.SpecificallyImmuneTo;
            if (specific is null) return;
            for (var i = 0; i < specific.Length; i++)
            {
                var buff = specific[i];
                if ((uint)buff < (uint)buffCount) row[buff] = true;
            }
        }
        catch (Exception e)
        {
            Safe.Once("npc.immunity.data." + type, $"[npc] 免疫数据读取失败（type={type}）：{Safe.Describe(e)}", true);
        }

        try
        {
            var shimmer = NPCID.Sets.ShimmerImmunity;
            if (shimmer is not null && type < shimmer.Length && (uint)BuffShimmer < (uint)buffCount)
                row[BuffShimmer] = shimmer[type];
        }
        catch (Exception e)
        {
            Safe.Once("npc.immunity.shimmer", "[npc] ShimmerImmunity 读取失败：" + Safe.Describe(e), true);
        }
    }

    internal static void Apply(NPC npc)
    {
        if (npc is null) return;
        Resize();

        var type = npc.type;
        if ((uint)type >= (uint)_specific.Length) return;

        var count = _specific.Length == 0 ? 0 : _specific[0].Length;
        if (count <= 0) return;

        if (npc.buffImmune is null || npc.buffImmune.Length < count)
        {
            var grown = new bool[count];
            if (npc.buffImmune is not null)
                Array.Copy(npc.buffImmune, grown, Math.Min(npc.buffImmune.Length, count));
            npc.buffImmune = grown;
        }

        var immunity = npc.buffImmune;
        var regular = _regular[type];
        var tags = _whips[type];
        var specific = _specific[type];
        var tagBuffs = _tags;

        for (var buff = 0; buff < count; buff++)
            immunity[buff] = regular && !tagBuffs[buff] || tags && tagBuffs[buff];

        var broad = regular || tags;
        for (var buff = 0; buff < count; buff++)
            if (specific[buff] == !broad) immunity[buff] = !broad;

        var parents = _parents;
        for (var buff = 0; buff < count; buff++)
        {
            var list = parents[buff];
            if (list.Count == 0) continue;

            var inherited = broad;
            foreach (var parent in list)
            {
                var value = (uint)parent < (uint)count && immunity[parent];
                if (broad) inherited &= !value;
                else inherited |= value;
            }

            if (inherited) immunity[buff] = !broad;
        }

        for (var buff = 0; buff < count; buff++)
            if (specific[buff] == broad) immunity[buff] = broad;
    }
}
