using System.Runtime.CompilerServices;
using Terraria.GameContent.Generation;
using Terraria.IO;
using Terraria.WorldBuilding;

namespace Pylon.ModLoader;

/// <summary>创建能插入原生生成任务列表的托管步骤;不要直接派生生成代理类实现虚方法。</summary>
public static class WorldGenPass
{
    private sealed record Entry(Action<GenerationProgress, GameConfiguration> Apply);
    private static readonly object Gate = new();
    private static readonly ConditionalWeakTable<GenPass, Entry> Callbacks = new();
    private static readonly Dictionary<nint, WeakReference<GenPass>> Entries = [];

    /// <summary>返回真实游戏步骤,可插入、重排或禁用;跨生成复用时须由作者保存返回对象,仅原生容器不能保活托管闭包。</summary>
    public static GenPass Create(string name, Action<GenerationProgress, GameConfiguration> apply, double weight = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(apply);
        if (!double.IsFinite(weight) || weight < 0) throw new ArgumentOutOfRangeException(nameof(weight));
        ModWorldGenHooks.Install();
        var native = new PassLegacy(name, null!, weight);
        lock (Gate)
        {
            foreach (var key in Entries.Where(static pair => !pair.Value.TryGetTarget(out _))
                         .Select(static pair => pair.Key).ToArray()) Entries.Remove(key);
            Callbacks.Add(native, new Entry(apply));
            Entries[native.Pointer] = new WeakReference<GenPass>(native);
        }
        return native;
    }

    internal static bool IsManaged(GenPass pass)
    {
        lock (Gate)
            return Entries.TryGetValue(pass.Pointer, out var weak) && weak.TryGetTarget(out _);
    }

    internal static GenPass Retain(GenPass pass)
    {
        lock (Gate)
            return Entries.TryGetValue(pass.Pointer, out var entry) && entry.TryGetTarget(out var original)
                ? original : pass;
    }

    internal static bool TryApply(GenPass pass, GenerationProgress progress, GameConfiguration configuration)
    {
        Entry? entry = null;
        GenPass? retained = null;
        lock (Gate)
        {
            if (Entries.TryGetValue(pass.Pointer, out var weak) && weak.TryGetTarget(out retained))
                Callbacks.TryGetValue(retained, out entry);
        }
        if (entry is null) return false;
        try { entry.Apply(progress, configuration); }
        catch (Exception e) { ContentErrors.Report($"[worldgen] {pass.Name} 异常:{Safe.Describe(e)}"); }
        finally { GC.KeepAlive(retained); }
        return true;
    }
}
