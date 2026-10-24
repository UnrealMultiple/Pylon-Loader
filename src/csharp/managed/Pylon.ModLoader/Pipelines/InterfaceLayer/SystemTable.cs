using Pylon.ModLoader.Content;
using Pylon.ModLoader.Pipeline;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 系统时机的表：界面层与两个绘制时机的挂载门（ModSystem 覆写位集决定挂不挂/跑不跑），
/// 分派直接走 <see cref="SystemLoader"/>。位集在 Mod 装载时算好，分派期只做位查询。
/// </summary>
internal static class SystemTable
{
    internal static bool AnyModifyInterfaceLayers => SystemLoader.With(HookId.ModifyInterfaceLayers).Length > 0;

    internal static bool AnyPostDrawTiles => SystemLoader.With(HookId.PostDrawTiles).Length > 0;

    internal static bool AnyPostDrawInterface => SystemLoader.With(HookId.PostDrawInterface).Length > 0;
}
