using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 云侧表：编号 = 原版云表长 + 注册序。原版表长只能运行期读（没有编译期常量），
/// 由 <see cref="TypeTables.CloudVanilla"/> 在扩表之前统一捕获。
/// </summary>
internal static class CloudTable
{
    private static ModCloud?[] _clouds = new ModCloud?[8];
    private static readonly Dictionary<string, ModCloud> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModCloud?> ByName = new(StringComparer.Ordinal);
    private static int _next;

    /// <summary>原版云表长（自定义编号的起点）。</summary>
    internal static int Base => TypeTables.CloudVanilla;

    internal static int Count => _next;

    /// <summary>扩表要的目标长度。</summary>
    internal static int Total => Base + _next;

    internal static ModCloud? Of(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)_clouds.Length ? _clouds[index] : null;
    }

    /// <summary>按名字取编号：先 <c>Mod名/类名</c>，再裸类名（重名当没有）。</summary>
    internal static int Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return 0;

        if (name.IndexOf('/') >= 0) return ByKey.TryGetValue(name, out var exact) ? exact.Type : 0;
        return ByName.TryGetValue(name, out var loose) && loose is not null ? loose.Type : 0;
    }

    /// <summary>发号；幂等，每次 Mod 注册后跑一次（原版表还没建好时先不发号）。</summary>
    internal static void Assign()
    {
        if (Base <= 0) return;

        foreach (var cloud in ModContent.GetContent<ModCloud>())
        {
            if (cloud.Registered) continue;

            if (_next == _clouds.Length) Array.Resize(ref _clouds, _clouds.Length * 2);

            cloud.Type = Base + _next;
            cloud.Registered = true;
            _clouds[_next++] = cloud;

            ByKey[cloud.Mod.Name + "/" + cloud.Name] = cloud;
            ByName[cloud.Name] = ByName.ContainsKey(cloud.Name) ? null : cloud;
        }
    }

    /// <summary>
    /// 按权重抽一朵 mod 云：返回值是云类型号，0 = 这次保持原版。
    /// 区间是 [0, mod 权重之和 + 原版云数)：每朵原版云算权重 1。
    /// <paramref name="rare"/> 只抽稀有云（或只抽普通云），和 <c>ModCloud.RareCloud</c> 对应。
    /// </summary>
    internal static int Roll(int vanillaCount, bool rare = false)
    {
        if (_next == 0) return 0;

        var total = 0;
        for (var index = 0; index < _next; index++)
        {
            if (_clouds[index] is not { } candidate) continue;
            if (candidate.RareCloud != rare) continue;
            total += Chance(candidate);
        }

        if (total <= 0) return 0;

        var span = total + (vanillaCount > 0 ? vanillaCount : 0);
        var roll = Main.rand.Next(span);
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

    /// <summary>这个编号是不是稀有云（用于给生成出来的云补 <c>OnSpawn</c>）。</summary>
    internal static bool IsRare(int type) => Of(type) is { RareCloud: true };

    private static int Chance(ModCloud cloud)
    {
        try
        {
            return Math.Max(cloud.SpawnChance(), 0);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[cloud] {cloud.Name}.SpawnChance 异常：{Safe.Describe(e)}");
            return 0;
        }
    }
}
