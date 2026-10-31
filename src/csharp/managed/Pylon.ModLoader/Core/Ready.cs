using System.Diagnostics;

namespace Pylon.ModLoader;

/// <summary>内容就绪的任务队列：注册期排队，内容就绪时按阶段依次执行，单个任务失败不影响其他任务。</summary>
internal static class Ready
{
    private static readonly Dictionary<ReadyPhase, List<Action>> Tasks = new()
    {
        [ReadyPhase.Resize] = [],
        [ReadyPhase.Setup] = [],
        [ReadyPhase.Groups] = [],
        [ReadyPhase.Mods] = [],
        [ReadyPhase.Content] = [],
        [ReadyPhase.Recipes] = [],
        [ReadyPhase.PostRecipes] = [],
        [ReadyPhase.Finish] = [],
    };

    private static bool _fired;
    private static bool _running;

    internal static IModLog Log { get; set; } = NullLog.Instance;

    /// <summary>排一个就绪任务；内容已经就绪时立即执行（晚注册的 Loader 也要生效）。</summary>
    internal static void Add(Action task, ReadyPhase phase = ReadyPhase.Content)
    {
        if (task is null) return;

        if (!_fired)
        {
            Tasks[phase].Add(task);
            return;
        }

        // 正在跑阶段流：排到该阶段末尾，避免在遍历中改集合；否则立即执行。
        if (_running) Tasks[phase].Add(task);
        else Safe.Run($"Ready.{phase}（晚注册）", task);
    }

    /// <summary>内容就绪：按阶段顺序执行全部任务（幂等）。</summary>
    internal static void Fire()
    {
        if (_fired) return;
        _fired = true;
        _running = true;

        var total = Stopwatch.StartNew();
        try
        {
            foreach (var phase in Enum.GetValues<ReadyPhase>()) RunPhase(phase);
            Log.Info($"[ready] 阶段总耗时 {total.ElapsedMilliseconds}ms");
        }
        finally
        {
            _running = false;
        }
    }

    private static void RunPhase(ReadyPhase phase)
    {
        var watch = Stopwatch.StartNew();
        var tasks = Tasks[phase];
        for (var i = 0; i < tasks.Count; i++) Safe.Run($"Ready.{phase}", tasks[i]);
        tasks.Clear();

        if (watch.ElapsedMilliseconds >= 50) Log.Info($"[ready] {phase} 阶段耗时 {watch.ElapsedMilliseconds}ms");
    }
}
