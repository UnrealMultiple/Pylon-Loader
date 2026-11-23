using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Tables;

namespace Pylon.ModLoader.Pipelines;
/// <summary>尘土表：类型号 = 原版表长（运行期锚点）+ 注册序，与原版尘土共用 Main.dust 池；起点只能运行期读，见 <see cref="DustTables.Vanilla"/>。</summary>
internal static class DustTable
{
    private static readonly List<ModDust> Dusts = [];

    /// <summary>原版表长（自定义类型号的起点）。</summary>
    internal static int Base => DustTables.Vanilla;

    internal static int Count => Dusts.Count;

    internal static IReadOnlyList<ModDust> All => Dusts;

    /// <summary>扩表要的目标长度。</summary>
    internal static int Total => Base + Dusts.Count;

    internal static ModDust? Of(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)Dusts.Count ? Dusts[index] : null;
    }

    /// <summary>发号；幂等，每次 Mod 注册后跑一次（锚点没捕获时不发号）。</summary>
    internal static void Assign()
    {
        if (Base <= 0) return;

        foreach (var dust in ModContent.GetContent<ModDust>())
        {
            if (dust.Registered) continue;

            dust.Type = Base + Dusts.Count;
            dust.Registered = true;
            dust.Hooks = HookScan.Of(dust.GetType(), typeof(ModDust));
            Dusts.Add(dust);
        }
    }
}

/// <summary>尘土编号相关的原生表：Dust 的两张 alpha 表与 ChildSafety.SafeDust（长度都跟着尘土类型数走，原版多处读它们不带越界检查，先扩表再让 mod 号进原版）。</summary>
internal static class DustTables
{
    private static readonly Type[] Types =
    [
        typeof(Terraria.Dust),
        typeof(Terraria.GameContent.ChildSafety),
    ];

    internal static readonly TableSet Set = new("尘土", () => Vanilla, Types, fillWithMode: true);

    private static int _vanilla;
    private static bool _mismatched;

    /// <summary>原版尘土表长：SafeDust 为主、StaticAlpha 为备；读到 0 不缓存，两者都读到却不等按停用处理。</summary>
    internal static int Vanilla
    {
        get
        {
            if (_mismatched) return 0;
            if (_vanilla > 0) return _vanilla;

            try { IL2CPP.il2cpp_runtime_class_init(Il2CppClassPointerStore<Terraria.Dust>.NativeClassPtr); } catch { }
            try { IL2CPP.il2cpp_runtime_class_init(Il2CppClassPointerStore<Terraria.GameContent.ChildSafety>.NativeClassPtr); } catch { }

            var safe = Length(Terraria.GameContent.ChildSafety.SafeDust);
            var alpha = Length(Terraria.Dust.StaticAlpha);
            if (safe <= 0 && alpha <= 0) return 0;
            if (safe > 0 && alpha > 0 && safe != alpha)
            {
                _mismatched = true;
                ContentErrors.Line($"[dust] 表长不一致：SafeDust={safe}，StaticAlpha={alpha}；尘土池路径停用");
                return 0;
            }

            _vanilla = safe > 0 ? safe : alpha;
            return _vanilla;
        }
    }

    private static int Length(Il2CppStructArray<bool>? array)
    {
        try { return array?.Length ?? 0; }
        catch { return 0; }
    }
}
