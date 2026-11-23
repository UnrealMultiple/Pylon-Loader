using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Audio;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Terraria;
using Terraria.Localization;
using NullableColor = Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Graphics.Color>;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 多人托管界面（GUIMultiplayerHost，游戏自己的 UI）上的联机开关：借用原版 Crossplay 行的布局克隆出
/// 「模组同步 / 允许材质包 / 允许作弊」三行，加在密码行下方。开关直接写 NetPolicy（.pylon/net.json），
/// 语义随欢迎包广播给客户端（NetHandshake）：模组同步关 = 客户端模组内容可以不同；
/// 允许材质包关 = 客户端纹理必须与本机一致；允许作弊控制作弊规则位。
/// 机制：行内控件（开关钮、ON/OFF 药丸、标签）都锚定到"当前按钮"，克隆出的按钮每行只改 Location，
/// 其余原样复用；悬停行时把说明写进原版 description 栏；三行超出可视区时整屏可拖动滚动、
/// 原版各行随滚动位移并逐行裁剪，退出或开服时复位。
/// </summary>
internal static class NetHostMenu
{
    private sealed class Row
    {
        public required string Key;
        public required string Description;
        public required Func<bool> Get;
        public required Action<bool> Set;
        public SettingsOverlayToggle_Layout Layout = null!;
        public float Scale = 1f;
    }

    private const float DragPx = 8f;
    private const float Friction = 0.92f;
    private const int Tick = 12;

    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static List<Row> _rows = [];
    private static float _scroll, _momentum;
    private static float? _pressY, _lastY;
    private static bool _dragged, _pressIn;
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;

        On.GUIMultiplayerHost.Draw += Draw;
        On.GUIMultiplayerHost.DrawMaxPlayers += (orig, self) =>
            GameRowSafe(() => orig(self), () => Host()?.MaxPlayers?.Title);
        On.GUIMultiplayerHost.DrawOpenInviteOnly += (orig, self) =>
            GameRowSafe(() => orig(self), () => Host()?.InviteOpenToggle?.ToggleButton);
        On.GUIMultiplayerHost.DrawCrossplay += (orig, self) =>
            GameRowSafe(() => orig(self), () => Host()?.Crossplay?.ToggleButton);
        On.GUIMultiplayerHost.DrawPassword += DrawPassword;
        On.GUIMultiplayerHost.Back += (orig, self) => { ResetScroll(); orig(self); };
        On.GUIMultiplayerHost.HostServer += (orig, self) => { ResetScroll(); orig(self); };
    }

    private static MultiplayerHost_Layout? Host()
    {
        try { return MultiplayerHost_Layout.Instance; }
        catch (Exception) { return null; }
    }

    private static List<Row> Rows()
    {
        if (_rows.Count > 0) return _rows;
        var template = Host()?.Crossplay;
        if (template is null) throw new InvalidOperationException("MultiplayerHost_Layout 还没就绪");

        Row Make(string key, string description, Func<bool> get, Action<bool> set)
        {
            var layout = NativeClone.Of(template);
            layout.ToggleButton = NativeClone.Of(template.ToggleButton!);
            layout.Option1Label = NativeClone.Of(template.Option1Label!);
            layout.Option2Label = NativeClone.Of(template.Option2Label!);
            return new Row { Key = key, Description = description, Get = get, Set = set, Layout = layout };
        }

        _rows.Add(Make("mods", "开启后，加入玩家的模组内容必须与本服一致，不一致会被拒绝进入。",
            () => NetPolicy.SyncMods, v => NetPolicy.SyncMods = v));
        _rows.Add(Make("textures", "开启后，加入玩家可以使用自己的材质 Mod；关闭则要求与本服纹理一致。",
            () => NetPolicy.AllowTextures, v => NetPolicy.AllowTextures = v));
        _rows.Add(Make("cheats", "关闭后，客户端的作弊类 Mod 功能会向本服让步。",
            () => NetPolicy.HostCheats, v => NetPolicy.HostCheats = v));
        return _rows;
    }

    private static string Caption(Row row) => row.Key switch
    {
        "mods" => "模组同步：",
        "textures" => "允许材质包：",
        _ => "允许作弊：",
    };

    private static float Step(MultiplayerHost_Layout l) => l.Password.Location.Y - l.Crossplay.ToggleButton.Location.Y;

    private static Rectangle Rect(float x, float y, float width, float height) =>
        new((int)MathF.Round(x), (int)MathF.Round(y), (int)MathF.Round(width), (int)MathF.Round(height));

    private static Rectangle Area()
    {
        var l = Host()!;
        Rectangle back = GUIPanel.Region(l.Backing);
        Rectangle top = GUIPanel.Region(l.MenuDivider2);
        Rectangle bottom = GUIPanel.Region(l.MenuDivider);
        var y = top.Y + top.Height;
        return Rect(back.X, y, back.Width, bottom.Y - y);
    }

    private static void Draw(On.GUIMultiplayerHost.orig_Draw orig, GUIMultiplayerHost self)
    {
        Wrap("net.host.scroll", Scroll);
        orig(self);
    }

    private static void Scroll()
    {
        var area = Area();
        GUIInputRegionManager.Instance.RegisterInputRegion(area);
        var max = Rows().Count * Step(Host()!);
        var inside = Main.mouseX >= area.X && Main.mouseX <= area.X + area.Width &&
                     Main.mouseY >= area.Y && Main.mouseY <= area.Y + area.Height;
        if (Main.mouseLeft)
        {
            if (_pressY == null)
            {
                _pressY = Main.mouseY;
                _lastY = Main.mouseY;
                _dragged = false;
                _pressIn = inside;
                _momentum = 0;
            }
            else if (_pressIn && Math.Abs(Main.mouseY - _pressY.Value) > DragPx)
            {
                _dragged = true;
            }

            if (_dragged)
            {
                var delta = _lastY!.Value - Main.mouseY;
                _scroll += delta;
                _momentum = delta;
                XNAUIInputLayer.Instance.CaptureUICrusorDrag(1);
            }

            _lastY = Main.mouseY;
        }
        else
        {
            if (_dragged) XNAUIInputLayer.Instance.CaptureUICrusorDrag(1);
            _pressY = null;
            _lastY = null;
            _dragged = false;
            if (Math.Abs(_momentum) > 0.5f)
            {
                _scroll += _momentum;
                _momentum *= Friction;
            }
            else
            {
                _momentum = 0;
            }
        }

        if (_scroll < 0 || _scroll > max)
        {
            _scroll = Math.Clamp(_scroll, 0, max);
            _momentum = 0;
        }
    }

    private static void Clipped(TransactionButton_Layout layout, float y, Action draw)
    {
        var area = Area();
        var x = layout.Location.X;
        var savedY = layout.Location.Y;
        var top = LayoutCalculator.GetAnchoredPosition(layout.AnchorControl, layout.Anchor, new Vector2(x, y)).Y;
        var height = layout.overloadedSize.Y;
        if (top + height <= area.Y || top >= area.Y + area.Height) return;
        var cut = top < area.Y || top + height > area.Y + area.Height;
        if (cut && _pressY != null && !_pressIn) return;
        layout.Location = new Vector2(x, y);
        SpriteBatchItem.EnableClipping(area, area, Main.spriteBatch, true);
        try { draw(); }
        finally
        {
            SpriteBatchItem.DisabledClipping();
            layout.Location = new Vector2(x, savedY);
        }
    }

    private static void GameRow(Action vanilla, TransactionButton_Layout? layout)
    {
        if (layout is null || _scroll == 0)
        {
            vanilla();
            return;
        }

        var drawn = false;
        try
        {
            Clipped(layout, layout.Location.Y - _scroll, () =>
            {
                drawn = true;
                vanilla();
            });
            drawn = true;
        }
        catch (Exception e)
        {
            Safe.Report("net.host.row", e);
            if (!drawn) vanilla();
        }
    }

    private static void GameRowSafe(Action vanilla, Func<TransactionButton_Layout?> layout)
    {
        try
        {
            GameRow(vanilla, layout());
        }
        catch (Exception e)
        {
            Safe.Report("net.host.row", e);
            vanilla();
        }
    }

    private static void DrawPassword(On.GUIMultiplayerHost.orig_DrawPassword orig, GUIMultiplayerHost self)
    {
        GameRowSafe(() => orig(self), () => Host()?.Password);
        Wrap("net.host.rows", () => DrawRows(self));
    }

    private static void DrawRows(GUIMultiplayerHost self)
    {
        var l = Host();
        if (l is null) return;
        var step = Step(l);
        var baseX = l.Password.Location.X;
        var baseY = l.Password.Location.Y;
        var list = Rows();
        for (var i = 0; i < list.Count; i++)
        {
            var row = list[i];
            var button = row.Layout.ToggleButton;
            button.Location = new Vector2(baseX, baseY);
            var index = i;
            Clipped(button, baseY + step * (index + 1) - _scroll, () => DrawToggle(self, row));
        }
    }

    private static bool Focus()
    {
        var gi = GUIInstance.Active;
        var pad = gi?.GUIVirtualInputController;
        return pad != null && pad.ControllerActive;
    }

    private static void DrawToggle(GUIMultiplayerHost self, Row row)
    {
        var t = row.Layout;
        var template = Host()?.Crossplay;
        var state = GUITransactionButton.Draw(t.ToggleButton, (Item)null!, Caption(row), false, ref row.Scale,
            false, Focus(), false, false);
        if (state == GUITransactionButton.InputState.Clicked && _pressIn && !_dragged)
        {
            row.Set(!row.Get());
            SoundEngine.PlaySound(Tick);
        }

        if (state is GUITransactionButton.InputState.Clicked or GUITransactionButton.InputState.Over
            or GUITransactionButton.InputState.Down)
        {
            self.description = row.Description;
        }

        var on = row.Get();
        if (on)
        {
            GUIPanel.Draw(t.Option2Disabled, false, NoColor(), NoColor(), NoColor());
            GUIPanel.Draw(t.Option1Enabled, false, NoColor(), NoColor(), NoColor());
        }
        else
        {
            GUIPanel.Draw(t.Option1Disabled, false, NoColor(), NoColor(), NoColor());
            GUIPanel.Draw(t.Option2Enabled, false, NoColor(), NoColor(), NoColor());
        }

        if (template is not null)
        {
            Label(t.Option1Label, template.Option1Label!, t.Option1Enabled!.Size.X,
                Locale("Mobile.ToggleSettingOn"), on ? t.Option1Label.Color : t.Option1DisabledLabelColour);
            Label(t.Option2Label, template.Option2Label!, t.Option2Enabled!.Size.X,
                Locale("Mobile.ToggleSettingOff"), on ? t.Option2DisabledLabelColour : t.Option2Label.Color);
        }
    }

    private static void Label(String_Layout label, String_Layout template, float width, string text, Color color)
    {
        var font = TryFont(template);
        var measured = font != null ? font.MeasureString(text).X : 0f;
        var room = Math.Max(8f, width - 8f);
        label.Scale = measured > 0 ? Math.Min(template.Scale, room / measured) : template.Scale;
        GUIString.Draw(label, text, color, false);
    }

    private static SpriteFont? TryFont(String_Layout? layout)
    {
        if (layout == null) return null;
        try { return layout.GetFont(); }
        catch (Exception) { return null; }
    }

    private static string Locale(string key) => Language.GetTextValue(key);

    private static NullableColor NoColor() => new();

    private static void Wrap(string label, Action action, Action? fallback = null)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            if (Reported.Add(label)) Safe.Report(label, e);
            fallback?.Invoke();
        }
    }

    private static void ResetScroll()
    {
        _scroll = 0;
        _momentum = 0;
    }
}
