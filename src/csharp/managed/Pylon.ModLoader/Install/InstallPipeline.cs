using System.Diagnostics;
using Pylon.ModLoader.Textures;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 安装管线的执行器：等游戏表就绪，然后按预算逐步执行安装步骤；完成后转入表维护模式。
/// 顺序由 <see cref="InstallPlan"/> 唯一决定，这里只管"什么时候、花多久"。
/// </summary>
internal sealed class InstallPipeline
{
    /// <summary>每帧给安装的时间预算；超了就让出，下一帧接着来。</summary>
    private const int FrameBudgetMs = 8;

    /// <summary>单步超过这个时长就单独记一条，提示该拆了。</summary>
    private const int SlowStepMs = 40;

    /// <summary>就绪判据的兜底帧数（约 5 分钟）；只防永远卡住，不提前强装。</summary>
    private const int ReadyTimeoutFrames = 60 * 60 * 5;

    /// <summary>表维护的间隔帧数。</summary>
    private const int MaintainEveryFrames = 60;

    private readonly LoaderHost _host;
    private IInstallStep[] _steps = [];
    private int _cursor;
    private int _frames;
    private int _maintainAge;
    private bool _armed;
    private bool _complete;

    public InstallPipeline(LoaderHost host) => _host = host;

    public bool IsComplete => _complete;

    private IModLog Log => _host.Log;

    /// <summary>武装：装一次每帧钩子（幂等）。</summary>
    public void Arm()
    {
        if (_armed) return;
        _armed = true;
        try
        {
            On.Terraria.Main.DoUpdate += Tick;

            // 扩表只在明确事件里做（SceneMetrics.Reset / LoadWorld），不每帧扫全表。
            On.Terraria.SceneMetrics.Reset += Reshape;
            On.Terraria.IO.WorldFile.LoadWorld += ReshapeWorld;
        }
        catch (Exception e)
        {
            Log.Warn("安装管线武装失败：" + Safe.Describe(e));
        }
    }

    private void Tick(On.Terraria.Main.orig_DoUpdate orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);

        try
        {
            if (_complete) return;

            if (_steps.Length == 0)
            {
                var ready = GameReadiness.LooksReady();
                if (!ready && ++_frames < ReadyTimeoutFrames) return;

                if (_steps.Length == 0) Build();
            }

            RunUntilBudget();
        }
        catch (Exception e)
        {
            Log.Warn("安装帧异常：" + Safe.Describe(e));
        }
    }

    private void Build()
    {
        _steps = InstallPlan.Build(_host);
        _cursor = 0;
        _frames = 0;

        if (_steps.Length == 0)
        {
            Complete();
            return;
        }

        Log.Info($"[install] 表已定型，开始安装（{_steps.Length} 步）");
    }

    /// <summary>按时间预算推进；单步失败只记日志并跳过。</summary>
    private void RunUntilBudget()
    {
        var watch = Stopwatch.StartNew();
        while (_cursor < _steps.Length)
        {
            var step = _steps[_cursor];
            var stepWatch = Stopwatch.StartNew();
            var done = true;

            try
            {
                done = step.Run();
            }
            catch (Exception e)
            {
                Safe.Report($"[install] {step.Name}", e);
            }

            if (stepWatch.ElapsedMilliseconds >= SlowStepMs)
                Log.Warn($"[install] {step.Name} 用了 {stepWatch.ElapsedMilliseconds}ms（该拆了）");

            if (!done) break;
            _cursor++;

            if (watch.ElapsedMilliseconds >= FrameBudgetMs) return;
        }

        if (_cursor >= _steps.Length) Complete();
    }

    private void Complete()
    {
        _complete = true;
        _host.MarkReady();
        Log.Info("[install] 内容安装完成，转入表维护");
    }

    /// <summary>事件驱动的表维护：场景指标重置后补扩。</summary>
    private void Reshape(On.Terraria.SceneMetrics.orig_Reset orig, Terraria.SceneMetrics self)
    {
        orig(self);
        Maintain();
    }

    /// <summary>读档后补扩：世界文件加载会重建一批内容表。</summary>
    private void ReshapeWorld(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
    {
        orig(canCheckFileState);
        Maintain();

        // 进世界时游戏会重建 Lang 缓存：名字/介绍必须重写一遍，否则物品「拿得到但没有名字没有介绍」。
        try
        {
            InstallSteps.ReapplyTexts();
        }
        catch (Exception e)
        {
            Log.Warn("本地化重写失败：" + Safe.Describe(e));
        }
    }

    /// <summary>表维护：游戏重建表后补扩、重写槽；方块与墙壁的实例表低频补齐。</summary>
    private void Maintain()
    {
        try
        {
            var grown = TypeTables.Maintenance(m => Log.Info(m), _ => InstallSteps.WriteTextures(_host));
            if (grown > 0) Log.Info($"[typetables] 维护：补扩 {grown} 张表");

            if (++_maintainAge < MaintainEveryFrames) return;
            _maintainAge = 0;
            TilePipeline.Instance.Maintain();
        }
        catch (Exception e)
        {
            Log.Warn("表维护异常：" + Safe.Describe(e));
        }
    }
}
