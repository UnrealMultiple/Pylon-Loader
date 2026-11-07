using Pylon.ModLoader;

namespace PipelineTest;

public sealed class PipelineTestMod : Mod
{
    public override void Load()
    {
        PT.Log2 = Logger;
        Logger.Info("[PT] mod loading");
    }

    public override void PostSetupContent()
    {
        var boot = 0;
        PT.Boot(1, 6);
        Logger.Info("[PT] ready: 开始用例");

        Tests.ItemTests.Run();

        PT.End();
    }
}
