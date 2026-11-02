// 唯一的一页：一个输入框。
using Pylon.Mod;
using Pylon.ModLoader.UI;

namespace FrameMod
{
    public sealed class FrameModTab : IModImGuiTab
    {
        public string Title => "FrameMod";

        private string _input = "点一下输入框";

        public void Draw()
        {
            TerrariaUI.SeparatorText("输入框");
            TerrariaUI.TextInput("名字", ref _input);
            TerrariaUI.DimText("当前内容：" + _input);
        }
    }
}
