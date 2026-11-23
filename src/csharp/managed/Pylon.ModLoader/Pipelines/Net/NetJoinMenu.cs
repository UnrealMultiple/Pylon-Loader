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
/// 加入 / 添加服务器界面上的「发送指纹」开关行：借原版 RememberToggle（记住服务器）行的布局克隆，
/// 写 NetPolicy.SendFingerprint——关闭后客户端不向服务器上报指纹包，hello 报占位符，
/// 本端放弃内容 / 纹理自检（NetHandshake）。
/// 结构（反汇编 GUIMultiplayerJoin.Draw 0xE673DC 核对）：
/// DrawMainButtons（0xE67838）每帧现读 MultiplayerJoin_Layout 的 Join 0xA8 / Accept 0xB0 / Back 0xA0 / Cancel 0xB8，
/// 记住行只在 editingServerEntry == null 时画；DrawDescription（0xE67D50）的文字位置由 Description.Label
/// 自己的 Anchor/Location 决定（GUIString.Draw 0x152E2A4 读 +0x10/+0x14/+0x18），与按钮 Location 无关。
/// 因此：Draw 前缀把四个按钮与 Description.Label 各加一行间距，画完还原；发送指纹行落在记住行下方
/// （记住行隐藏时占它的空槽），行内点位 / 开关沿用记住行克隆（画法与 NetHostMenu 同一套）。
/// </summary>
internal static class NetJoinMenu
{
    private const int Tick = 12;
    private const float ButtonGap = 10f;

    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static readonly List<(TransactionButton_Layout Layout, Vector2 Saved)> ShiftedButtons = new();
    private static readonly List<(String_Layout Layout, Vector2 Saved)> ShiftedStrings = new();
    private static SettingsOverlayToggle_Layout? _row;
    private static float _rowScale = 1f;
    private static float _step, _rowDelta;
    private static bool _shifting;
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;

        On.GUIMultiplayerJoin.Draw += Draw;
    }

    private static void Draw(On.GUIMultiplayerJoin.orig_Draw orig, GUIMultiplayerJoin self)
    {
        if (!FormOpen(self))
        {
            orig(self);
            return;
        }

        var moved = false;
        if (!_shifting)
        {
            try
            {
                moved = MakeRoom(self);
            }
            catch (Exception e)
            {
                Restore();
                if (Reported.Add("net.join.room")) Safe.Report("net.join.room", e);
            }

            _shifting = moved;
        }

        try
        {
            orig(self);
        }
        finally
        {
            if (moved)
            {
                Restore();
                _shifting = false;
            }
        }

        Wrap("net.join.row", () => DrawRow(self));
    }

    private static bool FormOpen(GUIMultiplayerJoin self)
    {
        try
        {
            return self.editingNewEntry || self.editingServerEntry is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static MultiplayerJoin_Layout? Join()
    {
        try { return MultiplayerJoin_Layout.Instance; }
        catch (Exception) { return null; }
    }

    private static Vector2 At(TransactionButton_Layout layout) =>
        LayoutCalculator.GetAnchoredPosition(layout.AnchorControl, layout.Anchor, layout.Location);

    private static bool MakeRoom(GUIMultiplayerJoin self)
    {
        var l = Join();
        var remember = l?.RememberToggle?.ToggleButton;
        var password = l?.ServerPasswordButton;
        if (l is null || remember is null || password is null) return false;

        var step = At(remember).Y - At(password).Y;
        if (step <= 0) return false;

        _step = step;
        _rowDelta = self.editingServerEntry is null ? step : 0;

        Shift(l.Join, _step + ButtonGap);
        Shift(l.Accept, _step + ButtonGap);
        Shift(l.Back, _step + ButtonGap);
        Shift(l.Cancel, _step + ButtonGap);
        Shift(l.Description?.Label);
        return true;
    }

    private static void Shift(TransactionButton_Layout? layout, float? amount = null)
    {
        if (layout is null) return;
        var saved = layout.Location;
        layout.Location = new Vector2(saved.X, saved.Y + (amount ?? _step));
        ShiftedButtons.Add((layout, saved));
    }

    private static void Shift(String_Layout? layout)
    {
        if (layout is null) return;
        var saved = layout.Location;
        layout.Location = new Vector2(saved.X, saved.Y + _step);
        ShiftedStrings.Add((layout, saved));
    }

    private static void Restore()
    {
        foreach (var (layout, saved) in ShiftedButtons)
        {
            try { layout.Location = saved; }
            catch (Exception) { }
        }

        foreach (var (layout, saved) in ShiftedStrings)
        {
            try { layout.Location = saved; }
            catch (Exception) { }
        }

        ShiftedButtons.Clear();
        ShiftedStrings.Clear();
    }

    private static void DrawRow(GUIMultiplayerJoin? self)
    {
        var l = Join();
        var remember = l?.RememberToggle;
        if (l is null || remember?.ToggleButton is null) return;

        var row = RowLayout(remember);
        var rememberLoc = remember.ToggleButton.Location;
        row.ToggleButton.Location = new Vector2(rememberLoc.X, rememberLoc.Y + _rowDelta);
        var state = GUITransactionButton.Draw(row.ToggleButton, (Item)null!, "发送指纹：", false, ref _rowScale,
            false, Focus(), false, false);
        if (state == GUITransactionButton.InputState.Clicked)
        {
            NetPolicy.SendFingerprint = !NetPolicy.SendFingerprint;
            SoundEngine.PlaySound(Tick);
        }

        if (self is not null && state is GUITransactionButton.InputState.Clicked
            or GUITransactionButton.InputState.Over or GUITransactionButton.InputState.Down)
        {
            self.description = "开启后，客户端向服务器上报模组与纹理指纹用于一致性校验；关闭则不上报也不自检，" +
                               "任何 Pylon 服都能直接进入，内容不一致时编号可能错位。";
        }

        var on = NetPolicy.SendFingerprint;
        if (on)
        {
            GUIPanel.Draw(row.Option2Disabled, false, NoColor(), NoColor(), NoColor());
            GUIPanel.Draw(row.Option1Enabled, false, NoColor(), NoColor(), NoColor());
        }
        else
        {
            GUIPanel.Draw(row.Option1Disabled, false, NoColor(), NoColor(), NoColor());
            GUIPanel.Draw(row.Option2Enabled, false, NoColor(), NoColor(), NoColor());
        }

        Label(row.Option1Label, remember.Option1Label!, row.Option1Enabled!.Size.X,
            Locale("Mobile.ToggleSettingOn"), on ? row.Option1Label.Color : row.Option1DisabledLabelColour);
        Label(row.Option2Label, remember.Option2Label!, row.Option2Enabled!.Size.X,
            Locale("Mobile.ToggleSettingOff"), on ? row.Option2DisabledLabelColour : row.Option2Label.Color);
    }

    private static SettingsOverlayToggle_Layout RowLayout(SettingsOverlayToggle_Layout remember)
    {
        if (_row != null) return _row;
        var row = NativeClone.Of(remember);
        row.ToggleButton = NativeClone.Of(remember.ToggleButton!);
        row.Option1Label = NativeClone.Of(remember.Option1Label!);
        row.Option2Label = NativeClone.Of(remember.Option2Label!);
        _row = row;
        return row;
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

    private static bool Focus()
    {
        var gi = GUIInstance.Active;
        var pad = gi?.GUIVirtualInputController;
        return pad != null && pad.ControllerActive;
    }

    private static string Locale(string key) => Language.GetTextValue(key);

    private static NullableColor NoColor() => new();

    private static void Wrap(string label, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            if (Reported.Add(label)) Safe.Report(label, e);
        }
    }
}
