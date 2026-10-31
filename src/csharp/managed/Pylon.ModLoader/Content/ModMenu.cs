using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace Pylon.ModLoader;

/// <summary>标题画面的主题：菜单里的 logo、太阳、月亮与背景。没有在设置里选过的主题时，生效的是最后加载的 mod 的主题。</summary>
public abstract class ModMenu : ModType
{
    /// <summary>logo 贴图（Mod.LoadAsset 的 Asset&lt;Texture2D&gt;；null = 用游戏的日/夜两个 logo）。</summary>
    public virtual Asset<Texture2D> Logo { get; set; } = null!;

    /// <summary>太阳贴图（null = 用游戏的）。</summary>
    public virtual Asset<Texture2D> SunTexture { get; set; } = null!;

    /// <summary>月亮贴图，单帧、无月相（null = 用游戏的）。</summary>
    public virtual Asset<Texture2D> MoonTexture { get; set; } = null!;

    /// <summary>菜单音乐槽（-1 = 用游戏的）；菜单音乐总是游戏的；在 SetStaticDefaults 里定一次，别用 get（每帧读）。</summary>
    public virtual int Music { get; set; } = -1;

    /// <summary>false 把这个主题拿出轮换（选中的话自动换成别的）；也可以写成只读 get，按日期开。</summary>
    public virtual bool IsAvailable { get; set; } = true;

    /// <summary>"菜单主题"按钮上的名字（null = 用 Mod 名，长了截断）。</summary>
    public virtual string DisplayName { get; set; } = null!;

    /// <summary>现在是生效的主题（只读；管线在切换主题时写）。</summary>
    public bool IsSelected { get; internal set; }

    /// <summary>只放共享数值（例：把 Music 的槽定下来），不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>这个主题开始生效时（开游戏时已经在菜单里、或点了"菜单主题"）。</summary>
    public virtual void OnSelected() { }

    /// <summary>这个主题停止生效时。</summary>
    public virtual void OnDeselected() { }

    /// <summary>菜单里每帧；isOnTitleScreen = 在标题画面（menuMode 0）。</summary>
    public virtual void Update(bool isOnTitleScreen) { }

    /// <summary>logo 画之前；后四个可写（游戏这帧的 logo 中心/旋转/缩放/颜色）；false = 不画 logo。</summary>
    public virtual bool PreDrawLogo(SpriteBatch spriteBatch, ref Vector2 logoDrawCenter, ref float logoRotation, ref float logoScale, ref Color drawColor) => true;

    /// <summary>logo 画完之后，参数是它画的时候的值。</summary>
    public virtual void PostDrawLogo(SpriteBatch spriteBatch, ref Vector2 logoDrawCenter, ref float logoRotation, ref float logoScale, ref Color drawColor) { }
}

// 未移植 MenuBackgroundStyle：类型是 ModSurfaceBackgroundStyle，这个版本的游戏里没有（dump.cs 查无此类型）。
// 未移植 register：Pylon 的注册由 ModType 发现自动做，主题切换与 SetStaticDefaults 的调用在管线里。
