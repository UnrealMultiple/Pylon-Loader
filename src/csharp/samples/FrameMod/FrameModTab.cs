// 唯一的一页：一个输入框。
using Pylon.Mod;
using Pylon.ModLoader.UI;
using Pylon.ModLoader;
using Pylon.ModLoader.ImGuiHost;

namespace FrameMod
{
    public sealed class FrameModTab : IModImGuiTab
    {
        public string Title => "FrameMod";

        private string _input = "点一下输入框";

        public void Draw()
        {
            ModernUI.SectionTitle("输入框");
            ModernUI.TextInput("名字", ref _input);
            ModernUI.DimText("当前内容：" + _input);
        }
    }
}
