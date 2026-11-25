using System.Collections.Generic;
using Pylon.ModLoader.Content;

namespace CheatMenu;

/// <summary>
/// 晚阶段装配点：所有依赖游戏状态的初始化（hook、界面层）都在这里发生。
/// 早阶段 <see cref="CheatMenuMod.Load"/> 只注册 UI，不碰游戏对象。
/// </summary>
public sealed class CheatRuntime : ModSystem
{
    public override void PostSetupContent()
    {
        Features.CheatFeatureHost.Install();
    }

    public override void PostUpdateEverything()
    {
        Features.CheatFeatureHost.Tick();
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        foreach (var feature in Features.CheatFeatureHost.All)
        {
            if (Features.CheatFeatureHost.IsFailed(feature.Id)) continue;
            feature.AddInterfaceLayers(layers);
        }
    }

    public override bool HijackGetData(ref byte messageType, ref System.IO.BinaryReader reader, int playerNumber)
    {
        return Features.CheatFeatureHost.OnGetData(ref messageType, ref reader, playerNumber);
    }

    public override void OnModUnload()
    {
        Features.CheatFeatureHost.Flush(force: true);
    }
}