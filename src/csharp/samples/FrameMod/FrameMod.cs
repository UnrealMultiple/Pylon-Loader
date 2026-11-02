// FrameMod —— 最小示例 Mod：只挂一页 ImGui 界面。
// 这一页做两件事：画一张游戏自带的 Texture2D（走 GPU 抓取，不要求纹理可读），外加一个输入框。
using Pylon.ModLoader;

namespace FrameMod
{
    public sealed class FrameMod : Mod
    {
        internal static FrameMod Instance;
        internal static ModLogger Log;

        public override void Load()
        {
            Instance = this;
            Log = Logger;
            RegisterImGuiTab(new FrameModTab());
            Logger.Info("[frame] FrameMod 已加载：菜单里的“FrameMod”页");
        }
    }
}
