namespace Pylon.Mod;

/// <summary>Mod 自定义的 ImGui 菜单页。</summary>
public interface IModImGuiTab
{
    /// <summary>侧边栏标题。</summary>
    string Title { get; }

    /// <summary>每帧在菜单内容区调用。</summary>
    void Draw();
}
