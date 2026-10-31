using Il2CppMicrosoft.Xna.Framework;

namespace Pylon.ModLoader.UI;

public static partial class ModConfigMenu
{
    private enum Where { None, Pause, Menu }

    private sealed class Scales
    {
        public float Button = 1f;
        public float Title = 1f;
        public float Discard = 1f;
        public float Defaults = 1f;
        public float Apply = 1f;
    }

    private sealed class RowState
    {
        public float Scale = 1f;
        public float Scale2 = 1f;
        public float Value;
        public GUISlider.DragState? Drag;
    }

    private sealed class ColorState
    {
        public string? Hex;
        public float[] Hsl = [0f, 0f, 1f];
    }

    private sealed record Row(string Kind, ModConfigEntry Entry, ModConfigOption O, string Key, float Height);

    private sealed class TabItem
    {
        public required TransactionButton_Layout Layout;
        public float Scale = 1f;
    }

    private sealed class TabShape
    {
        public float IconPx;
        public required TransactionButton_Layout Start;
        public required TransactionButton_Layout Middle;
        public required TransactionButton_Layout End;
        public float ToMiddle;
        public float MiddleStep;
        public float ToEnd;
    }

    private sealed class PartsData
    {
        public required String_Layout Empty;
        public required String_Layout Header;
        public required String_Layout Label;
        public required String_Layout Cycle;
        public required Panel_Layout Divider;
        public required Panel_Layout ChoiceOn;
        public required Panel_Layout ChoiceOff;
        public required Panel_Layout Swatch;
        public required Panel_Layout Greyout;
        public required Panel_Layout PopBacking;
        public required TransactionButton_Layout Option;
    }

    private sealed class PopupState
    {
        public required Row Row;
        public Rectangle Rect;
        public bool Down;
        public bool DownInside;
        public bool Armed;
        public required float[] Scales;
    }

    private sealed class SegmentsData
    {
        public float Y;
        public float Each;
        public float Height;
        public required Func<float, Vector2> AnchorPos;
        public required Func<int, float> XOf;
    }
}
