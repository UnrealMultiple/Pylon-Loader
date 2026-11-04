using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 键位管线：手机端"按键"就是触屏按钮，所以这里只做两件事——
/// 把注册转给触屏按钮，以及补上键盘端才有的"别在打字时触发"判定。
/// </summary>
internal sealed class KeybindPipeline : ILoaderModule
{
    public static KeybindPipeline Instance { get; } = new();

    private int _count;

    public string Name => "keybind";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        Log.Info($"[keybind] 已挂（按键即触屏按钮；手机端无物理键可绑）· 已注册 {_count}");
        return true;
    }

    internal ModTouchButton Register(Mod mod, string name, string defaultKey = "")
    {
        var button = TouchButtonLoader.RegisterButton(mod, name, null);
        _count++;
        Log.Info($"[keybind] {mod?.Name}/{name} → 触屏按钮 #{_count - 1}");
        return button;
    }

    /// <summary>
    /// Current 的"不在文本界面里"版本：聊天 / NPC 对话 / 牌子打开时不算按下。
    /// 作者要"按住按钮边走边打字"时仍该读 Current。
    /// </summary>
    internal bool RetroCurrent(ModTouchButton button)
    {
        if (button is null || !button.Current) return false;

        try
        {
            if (Main.drawingPlayerChat) return false;

            var player = Main.player[Main.myPlayer];
            if (player is null) return true;

            return player.talkNPC == -1 && player.sign == -1;
        }
        catch (Exception e)
        {
            Safe.Report("keybind.retro", e);
            return true;
        }
    }
}
