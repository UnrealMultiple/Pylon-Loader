using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// 菜单主题管线：作者给的 logo / 太阳 / 月亮贴图在绘制入口每帧换入 <c>TextureAssets</c>、画完还原
/// （不持久占用游戏的贴图槽）；主题选择落盘见 <c>PylonLoader.menu.json</c>。
/// </summary>
internal sealed class MenuPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static MenuPipeline Instance { get; } = new();

    public string Name => "menu";

    private static IModLog Log => LoaderHost.Current.Log;

    private static ModMenu? _current;

    public bool Install()
    {
        On.Terraria.Main.Update += Update;
        On.GUILogo.Draw += DrawLogo;
        On.Terraria.Main.DrawMenu += DrawThemeButton;
        On.Terraria.Main.UpdateBGVisibility_BackLayer += UpdateBackLayer;

        // 之前，MenuTable 还是空的，按注册内容判断永远判不到——所以这里无条件挂载，
        // 由 hook 体内按 _current.OverridesSunMoon 决定是否换贴图（没重写就 orig 直通）。
        On.Terraria.Main.DrawSunAndMoon += DrawSunAndMoon;

        Log.Info("[menu] 已挂（主题贴图每帧换入 / 每帧 / 菜单音乐 / logo 绘制回调 / 日月）");
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

    /// <summary>
    /// 只在 menuMode==0 时保留每帧钩位（未来接 UI 用）；点击逻辑随按钮一起去掉。
    /// </summary>
    private static void DrawThemeButton(On.Terraria.Main.orig_DrawMenu orig, Main self, Il2CppMicrosoft.Xna.Framework.GameTime gameTime)
    {
        orig(self, gameTime);
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

        if (menu is null || !MenuTable.Available(menu)) return;

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

    /// <summary>
    /// 换入前的原值。移动版的 logo / 太阳都有多套槽（Logo..Logo6、Sun/Sun2/Sun3），
    /// 按 LogoA/LogoB 或背景选一套画——只换 <c>TextureAssets.Logo</c> 真机上不会变，所以全部备份/换入。
    /// </summary>
    private sealed class TextureBackup
    {
        public Asset<Texture2D>? Logo, Logo2, Logo3, Logo4, Logo5, Logo6;
        public Asset<Texture2D>? Sun, Sun2, Sun3;
        public Asset<Texture2D>[]? Moon;
    }

    /// <summary>作者贴图 → 游戏资产；作者没给/读失败都返回 null（读不到就别动游戏的槽）。</summary>
    private static Asset<Texture2D>? ReadAsset(ModMenu menu, Func<Asset<Texture2D>?> read, string what)
    {
        try
        {
            var asset = read();
            return asset is null || asset.Value is null ? null : asset;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.{what} 异常：{Safe.Describe(e)}");
            return null;
        }
    }

    /// <summary>把作者贴图换进 <c>TextureAssets</c>（全部变体槽），返回原值备份；画完必须 SwapOut 还原。</summary>
    private static TextureBackup SwapIn(ModMenu menu)
    {
        var backup = new TextureBackup
        {
            Logo = TextureAssets.Logo, Logo2 = TextureAssets.Logo2, Logo3 = TextureAssets.Logo3,
            Logo4 = TextureAssets.Logo4, Logo5 = TextureAssets.Logo5, Logo6 = TextureAssets.Logo6,
            Sun = TextureAssets.Sun, Sun2 = TextureAssets.Sun2, Sun3 = TextureAssets.Sun3,
        };

        var logo = ReadAsset(menu, () => menu.Logo, nameof(ModMenu.Logo));
        if (logo is not null)
        {
            TextureAssets.Logo = logo;
            TextureAssets.Logo2 = logo;
            TextureAssets.Logo3 = logo;
            TextureAssets.Logo4 = logo;
            TextureAssets.Logo5 = logo;
            TextureAssets.Logo6 = logo;
        }

        var sun = ReadAsset(menu, () => menu.SunTexture, nameof(ModMenu.SunTexture));
        if (sun is not null)
        {
            TextureAssets.Sun = sun;
            TextureAssets.Sun2 = sun;
            TextureAssets.Sun3 = sun;
        }

        var moon = ReadAsset(menu, () => menu.MoonTexture, nameof(ModMenu.MoonTexture));
        if (moon is not null)
        {
            var phases = TextureAssets.Moon;
            if (phases is not null && phases.Length > 0)
            {
                backup.Moon = new Asset<Texture2D>[phases.Length];
                for (var index = 0; index < phases.Length; index++)
                {
                    backup.Moon[index] = phases[index];
                    phases[index] = moon;
                }
            }
        }

        return backup;
    }

    /// <summary>按备份把游戏贴图槽还原（每帧画完调用）。</summary>
    private static void SwapOut(TextureBackup backup)
    {
        if (backup.Logo is not null) TextureAssets.Logo = backup.Logo;
        if (backup.Logo2 is not null) TextureAssets.Logo2 = backup.Logo2;
        if (backup.Logo3 is not null) TextureAssets.Logo3 = backup.Logo3;
        if (backup.Logo4 is not null) TextureAssets.Logo4 = backup.Logo4;
        if (backup.Logo5 is not null) TextureAssets.Logo5 = backup.Logo5;
        if (backup.Logo6 is not null) TextureAssets.Logo6 = backup.Logo6;

        if (backup.Sun is not null) TextureAssets.Sun = backup.Sun;
        if (backup.Sun2 is not null) TextureAssets.Sun2 = backup.Sun2;
        if (backup.Sun3 is not null) TextureAssets.Sun3 = backup.Sun3;

        if (backup.Moon is not null)
        {
            var phases = TextureAssets.Moon;
            if (phases is not null)
            {
                for (var index = 0; index < backup.Moon.Length && index < phases.Length; index++)
                {
                    phases[index] = backup.Moon[index];
                }
            }
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

        if (draw)
        {
            var backup = SwapIn(menu);
            try
            {
                orig(self);
            }
            finally
            {
                SwapOut(backup);
            }
        }

        try
        {
            menu.PostDrawLogo(batch, center, rotation, scale, color);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[menu] {menu.Name}.PostDrawLogo 异常：{Safe.Describe(e)}");
        }
    }


    /// <summary>太阳/月亮：只在作者重写了 getter 时才进这里；画前换入、画完还原。</summary>
    private static void DrawSunAndMoon(On.Terraria.Main.orig_DrawSunAndMoon orig, Main self,
        Main.SceneArea sceneArea, Il2CppMicrosoft.Xna.Framework.Graphics.Color moonColor,
        Il2CppMicrosoft.Xna.Framework.Graphics.Color sunColor, float tempMushroomInfluence)
    {
        if (_current is not { } menu || !menu.OverridesSunMoon || !Main.gameMenu)
        {
            orig(self, sceneArea, moonColor, sunColor, tempMushroomInfluence);
            return;
        }
        TextureBackup backup;
        try { backup = SwapIn(menu); }
        catch (Exception e)
        {
            ContentErrors.Report("[menu] 日月换入失败：" + Safe.Describe(e));
            orig(self, sceneArea, moonColor, sunColor, tempMushroomInfluence);
            return;
        }

        try
        {
            orig(self, sceneArea, moonColor, sunColor, tempMushroomInfluence);
        }
        finally
        {
            try { SwapOut(backup); }
            catch (Exception e) { ContentErrors.Report("[menu] 日月还原失败：" + Safe.Describe(e)); }
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