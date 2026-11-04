using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// 菜单主题管线：把作者给的 logo / 太阳 / 月亮贴图换进 <c>TextureAssets</c>，离开主题时换回游戏原值。
/// logo 绘制回调与切换按钮本版不接，见 docs/pipeline/content-menu.md。
/// </summary>
internal sealed class MenuPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static MenuPipeline Instance { get; } = new();

    public string Name => "menu";

    private static IModLog Log => LoaderHost.Current.Log;

    private static ModMenu? _current;

    // 游戏原值：第一次选中主题时备份一次，之后一直用这份。
    private static Asset<Texture2D>? _logo;
    private static Asset<Texture2D>? _sun;
    private static Asset<Texture2D>[]? _moon;
    private static bool _backed;

    public bool Install()
    {
        On.Terraria.Main.Update += Update;
        On.GUILogo.Draw += DrawLogo;
        On.Terraria.Main.DrawMenu += DrawThemeButton;
        On.Terraria.Main.UpdateBGVisibility_BackLayer += UpdateBackLayer;

        Log.Info("[menu] 已挂（主题贴图 / 每帧 / 菜单音乐 / logo 绘制回调）；切换按钮本版不接");
        return true;
    }

    public void Refresh() => MenuTable.Rebuild();

    /// <summary>安装期一次：静态默认值 + 选中默认主题。</summary>
    internal void Build()
    {
        MenuTable.Rebuild();

        foreach (var menu in MenuTable.All) menu.SetStaticDefaults();

        if (MenuTable.Count > 0) Log.Info($"[menu] 安装：{MenuTable.Count} 个主题");

        // 上次选的主题优先；没有存档（或那个主题这次不可用）就回默认：最后注册且可用的那个。
        Select(LoadSaved() ?? MenuTable.Default());
    }

    // ===================== 菜单背景 =====================

    /// <summary>主题接管之前的背景号（-1 = 没接管过）；离开菜单或主题没声明背景时换回它。</summary>
    private static int _savedBgStyle = -1;

    /// <summary>
    /// 主题声明的表层背景风格：在游戏更新背景可见度的入口改 <c>Main.bgStyle</c>——这个时机比菜单逻辑晚、
    /// 比绘制早，原版自己的背景切换也走这里（有覆盖目标时原版会忽略 bgStyle，所以我们只在没有别的来源时写）。
    /// </summary>
    private static void UpdateBackLayer(On.Terraria.Main.orig_UpdateBGVisibility_BackLayer orig, Main self,
                                        Il2CppSystem.Nullable<int> targetBiomeOverride,
                                        Il2CppSystem.Nullable<float> transitionAmountOverride)
    {
        if (Main.gameMenu)
        {
            var slot = MenuBackgroundSlot();
            if (slot >= 0)
            {
                if (_savedBgStyle < 0) _savedBgStyle = Main.bgStyle;
                Main.bgStyle = slot;
            }
            else if (_savedBgStyle >= 0)
            {
                Main.bgStyle = _savedBgStyle;
                _savedBgStyle = -1;
            }
        }
        else if (_savedBgStyle >= 0)
        {
            Main.bgStyle = _savedBgStyle;
            _savedBgStyle = -1;
        }

        orig(self, targetBiomeOverride, transitionAmountOverride);
    }

    /// <summary>当前主题声明的背景槽；没声明 / 没注册 / 抛异常都是 -1。</summary>
    private static int MenuBackgroundSlot()
    {
        if (_current is not { } menu) return -1;

        ModSurfaceBackgroundStyle? style;
        try
        {
            style = menu.MenuBackgroundStyle;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.MenuBackgroundStyle 异常：{Safe.Describe(e)}");
            return -1;
        }

        if (style is null || !style.Registered) return -1;
        return style.Slot;
    }

    // ===================== 切换按钮 =====================

    /// <summary>按钮文字的位置（右下角），与 logo/太阳那套绘制互不干扰。</summary>
    private static readonly Vector2 ButtonAnchor = new(0.5f, 1f);

    private static bool _pressedLastFrame;

    /// <summary>
    /// 标题画面的"菜单主题"按钮：一行文字 + 点击切到下一个可用主题。
    /// 只在 <c>menuMode == 0</c> 且至少有一个可用主题时出现；点完落盘（<see cref="SelectAndSave"/>）。
    /// </summary>
    private static void DrawThemeButton(On.Terraria.Main.orig_DrawMenu orig, Main self, Il2CppMicrosoft.Xna.Framework.GameTime gameTime)
    {
        orig(self, gameTime);

        if (Main.gameMenu == false || Main.menuMode != 0) return;
        if (MenuTable.Count == 0) return;

        var next = NextAvailable();
        if (next is null && _current is null) return;

        var text = "菜单主题: " + (_current?.DisplayName ?? (_current?.Name ?? "原版")) + (next is null ? "" : "  [点击切换]");

        try
        {
            var font = FontAssets.MouseText.Value;
            var size = font.MeasureString(text) * 0.8f;
            var screen = new Vector2(Main.screenWidth * 0.5f, Main.screenHeight - 24f);
            var origin = new Vector2(size.X * 0.5f, 0f);

            Utils.DrawBorderString(Main.spriteBatch, text, screen - new Vector2(0f, 0f), Color.White, 0.8f, 0.5f, 0f);

            var down = Main.mouseLeft;
            var box = new Il2CppMicrosoft.Xna.Framework.Rectangle((int)(screen.X - origin.X - 6f), (int)(screen.Y - 4f),
                (int)size.X + 12, (int)size.Y + 8);

            if (down && !_pressedLastFrame && box.Contains(Main.mouseX, Main.mouseY) && next is not null)
            {
                Instance.SelectAndSave(next);
            }

            _pressedLastFrame = down;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[menu] 主题按钮绘制失败：" + Safe.Describe(e));
        }
    }

    /// <summary>下一个可用主题（在注册序上循环）；一个都没有时返回 null。</summary>
    private static ModMenu? NextAvailable()
    {
        var count = MenuTable.Count;
        if (count == 0) return null;

        var start = 0;
        if (_current is not null)
        {
            for (var index = 0; index < count; index++)
            {
                if (ReferenceEquals(MenuTable.At(index), _current))
                {
                    start = index + 1;
                    break;
                }
            }
        }

        for (var step = 0; step < count; step++)
        {
            var menu = MenuTable.At((start + step) % count);
            if (MenuTable.Available(menu)) return menu;
        }

        return null;
    }

    // ===================== 持久化 =====================

    /// <summary>选择落盘的位置：和触屏布局一样放在存档根目录，独立小文件。</summary>
    private static string FilePath => Path.Combine(Main.SavePath ?? "", "PylonLoader.menu.json");

    private static void Save(ModMenu? menu)
    {
        try
        {
            var path = FilePath;
            if (string.IsNullOrEmpty(path)) return;

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var key = menu is null ? "" : menu.Mod.Name + "/" + menu.Name;

            var root = new TagCompound();
            root.Set("theme", key);
            TagJson.Write(path, root);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[menu] 主题选择落盘失败：" + Safe.Describe(e));
        }
    }

    /// <summary>读回落盘的选择；键对不上（主题这次没装）或文件坏了都当没有。</summary>
    private static ModMenu? LoadSaved()
    {
        try
        {
            var path = FilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            var key = TagJson.Read(path)?.GetString("theme");
            if (string.IsNullOrEmpty(key)) return null;

            foreach (var menu in MenuTable.All)
            {
                if (string.Equals(menu.Mod.Name + "/" + menu.Name, key, StringComparison.Ordinal)) return menu;
            }

            Log.Warn("[menu] 存档里的主题 " + key + " 这次没有装载，回默认");
            return null;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[menu] 主题选择读档失败：" + Safe.Describe(e));
            return null;
        }
    }

    // ===================== 选择 =====================

    /// <summary>作者/界面切换主题：切完落盘（下次启动还是它）。</summary>
    internal void SelectAndSave(ModMenu? menu)
    {
        Select(menu);
        Save(menu);
    }

    /// <summary>换主题：先让旧的失效，再让新的生效；null = 回到游戏原值。</summary>
    internal void Select(ModMenu? menu)
    {
        if (ReferenceEquals(menu, _current)) return;

        var previous = _current;
        _current = null;

        if (previous is not null)
        {
            try
            {
                previous.IsSelected = false;
                previous.OnDeselected();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[menu] {previous.Name}.OnDeselected 异常：{Safe.Describe(e)}");
            }
        }

        Restore();

        if (menu is null || !MenuTable.Available(menu)) return;

        Backup();
        Apply(menu);
        _current = menu;

        try
        {
            menu.IsSelected = true;
            menu.OnSelected();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.OnSelected 异常：{Safe.Describe(e)}");
        }
    }

    private static void Backup()
    {
        if (_backed) return;

        _logo = TextureAssets.Logo;
        _sun = TextureAssets.Sun;
        _moon = TextureAssets.Moon;
        _backed = true;
    }

    /// <summary>把原值写回；备份还没做就什么都不做。</summary>
    private static void Restore()
    {
        if (!_backed) return;

        if (_logo is not null) TextureAssets.Logo = _logo;
        if (_sun is not null) TextureAssets.Sun = _sun;
        if (_moon is not null) TextureAssets.Moon = _moon;
    }

    private static void Apply(ModMenu menu)
    {
        var logo = ReadAsset(menu, () => menu.Logo, nameof(ModMenu.Logo));
        if (logo is not null) TextureAssets.Logo = logo;

        var sun = ReadAsset(menu, () => menu.SunTexture, nameof(ModMenu.SunTexture));
        if (sun is not null) TextureAssets.Sun = sun;

        var moon = ReadAsset(menu, () => menu.MoonTexture, nameof(ModMenu.MoonTexture));
        if (moon is null) return;

        var phases = TextureAssets.Moon;
        if (phases is null || phases.Length == 0) return;

        for (var index = 0; index < phases.Length; index++) phases[index] = moon;
    }

    private static Asset<Texture2D>? ReadAsset(ModMenu menu, Func<Asset<Texture2D>> read, string what)
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.{what} 异常：{Safe.Describe(e)}");
            return null;
        }
    }

    // ===================== logo =====================

    /// <summary>
    /// logo 的前后回调。几何参数由 loader 估算（屏幕水平中心、垂直五分之一处，旋转 0、缩放 1、白色）：
    /// 原版 <c>GUILogo.Draw()</c> 不带参数，真实位置只在它的绘制调用里，本版不去反推；
    /// 作者要精确对齐就在回调里改这些 ref，或者干脆自己画。
    /// </summary>
    private static void DrawLogo(On.GUILogo.orig_Draw orig, GUILogo self)
    {
        if (_current is not { } menu)
        {
            orig(self);
            return;
        }

        var batch = Main.spriteBatch;
        if (batch is null)
        {
            orig(self);
            return;
        }

        var center = new Il2CppMicrosoft.Xna.Framework.Vector2(Main.screenWidth * 0.5f, Main.screenHeight * 0.2f);
        var rotation = 0f;
        var scale = 1f;
        var color = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255, 255);
        var draw = true;

        try
        {
            draw = menu.PreDrawLogo(batch, ref center, ref rotation, ref scale, ref color);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.PreDrawLogo 异常：{Safe.Describe(e)}");
        }

        if (draw) orig(self);

        try
        {
            menu.PostDrawLogo(batch, ref center, ref rotation, ref scale, ref color);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.PostDrawLogo 异常：{Safe.Describe(e)}");
        }
    }

    // ===================== 每帧 =====================

    private void Update(On.Terraria.Main.orig_Update orig, Main self, Il2CppMicrosoft.Xna.Framework.GameTime gameTime)
    {
        orig(self, gameTime);

        if (MenuTable.Count == 0) return;

        // 选中的主题不可用了（例如按日期开放的写法）：换回默认。
        if (_current is not null && !MenuTable.Available(_current)) Select(MenuTable.Default());
        else if (_current is null) Select(MenuTable.Default());

        if (_current is not { } menu) return;

        var title = !Main.gameMenu || Main.menuMode == 0;

        try
        {
            menu.Update(title);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.Update 异常：{Safe.Describe(e)}");
        }

        if (!title) return;

        int music;
        try
        {
            music = menu.Music;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.Music 异常：{Safe.Describe(e)}");
            return;
        }

        if (music >= 0) Main.newMusic = music;
    }
}
