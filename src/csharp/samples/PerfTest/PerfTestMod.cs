using Pylon.Mod;
using Pylon.ModLoader.UI;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.ImGuiHost;

namespace PerfTest;

// Deliberately NOT Mod: a benchmark must not install content/world persistence pipelines.
public sealed class PerfTestMod : IPylonMod
{
    private BenchRunner? _runner;
    internal BenchRunner Runner => _runner!;

    public void Load(IModContext context)
    {
        string data = Path.Combine(Path.GetDirectoryName(context.Directory)!, "_data", context.Id);
        _runner = new BenchRunner(context.Log, data);
        context.RegisterImGuiTab(new PerfTab(this));
        On.Terraria.Main.Update += Update;
        context.Log("[perf] 0.4.0 ready. Open PerfTest tab. Nothing runs automatically; no content/world hooks.");
    }

    private void Update(On.Terraria.Main.orig_Update orig, Terraria.Main self, GameTime time)
    {
        orig(self, time);
        _runner?.Tick(); // Always Unity/game thread, including main menu. No world mutation.
    }
}

internal sealed class PerfTab(PerfTestMod mod) : IModImGuiTab
{
    public string Title => "PerfTest";
    public void Draw()
    {
        ModernUI.SectionTitle("Protected direct-call benchmark");
        ModernUI.DimText("Bunny mode: 20k continuous calls/round; may briefly stall.");
        if (ModernUI.Button("Bunny 同口径 / Run typed")) mod.Runner.RequestBunny();
        if (ModernUI.Button("原始路径对照 / Run raw")) mod.Runner.RequestRun();
        if (ModernUI.Button("异常恢复 / Safety")) mod.Runner.RequestSafety();
        if (ModernUI.Button("取消 / Cancel")) mod.Runner.RequestCancel();
        if (ModernUI.Button("统计 ABI 覆盖 / Coverage")) mod.Runner.RequestCoverage();
        ModernUI.DimText(Short(mod.Runner.Status));
        ModernUI.DimText("Full results: Android/data/dev.pylon.loader/files/PerfTest/");
        ModernUI.DimText("B/op: CoreCLR only; excludes IL2CPP boxing.");
        // Hard UI limits: outlined text emits many vertices; never render a complete report.
        foreach (string line in mod.Runner.Results.TakeLast(6)) ModernUI.DimText(Short(line));
    }
    private static string Short(string text) => text.Length <= 120 ? text : text[..117] + "...";
}
