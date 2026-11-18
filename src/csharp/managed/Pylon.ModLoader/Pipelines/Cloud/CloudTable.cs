using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>云侧表：编号 = 原版云表长 + 注册序。</summary>
internal static class CloudTable
{
    private static ModCloud?[] _clouds = new ModCloud?[8];
    private static readonly Dictionary<string, ModCloud> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModCloud?> ByName = new(StringComparer.Ordinal);
    private static int _next;

    internal static int Base
    {
        get
        {
            if (_vanilla > 0) return _vanilla;
            var length = TextureSlots.Length(TextureTable.Cloud);
            if (length > 0) _vanilla = length;
            return _vanilla;
        }
    }

    private static int _vanilla;

    internal static int Count => _next;

    internal static int Total => Base + _next;

    internal static ModCloud? Of(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)_clouds.Length ? _clouds[index] : null;
    }

    internal static int Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return 0;

        if (name.IndexOf('/') >= 0) return ByKey.TryGetValue(name, out var exact) ? exact.Type : 0;
        return ByName.TryGetValue(name, out var loose) && loose is not null ? loose.Type : 0;
    }

    internal static void Assign()
    {
        if (Base <= 0) return;

        foreach (var cloud in ModContent.GetContent<ModCloud>())
        {
            if (cloud.Registered) continue;

            if (_next == _clouds.Length) Array.Resize(ref _clouds, _clouds.Length * 2);

            cloud.Type = Base + _next;
            cloud.Registered = true;
            cloud.Hooks = HookScan.Of(cloud.GetType(), typeof(ModCloud));
            _clouds[_next++] = cloud;

            ByKey[cloud.Mod.Name + "/" + cloud.Name] = cloud;
            ByName[cloud.Name] = ByName.ContainsKey(cloud.Name) ? null : cloud;
        }
    }

    internal static int Roll(int vanillaCount, bool rare = false)
    {
        if (_next == 0) return 0;

        var total = 0f;
        for (var index = 0; index < _next; index++)
        {
            if (_clouds[index] is not { } candidate) continue;
            if (candidate.RareCloud != rare) continue;
            total += Chance(candidate);
        }

        if (total <= 0) return 0;

        var span = (int)total + (vanillaCount > 0 ? vanillaCount : 0);
        var roll = (float)Main.rand.Next(span);
        if (roll >= total) return 0;

        for (var index = 0; index < _next; index++)
        {
            var cloud = _clouds[index];
            if (cloud is null || cloud.RareCloud != rare) continue;

            var chance = Chance(cloud);
            if (chance <= 0) continue;

            if (roll < chance) return Base + index;
            roll -= chance;
        }

        return 0;
    }

    internal static bool IsRare(int type) => Of(type) is { RareCloud: true };

    private static float Chance(ModCloud cloud)
    {
        try
        {
            return Math.Max(cloud.SpawnChance(), 0f);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[cloud] {cloud.Name}.SpawnChance 异常：{Safe.Describe(e)}");
            return 0;
        }
    }
}
