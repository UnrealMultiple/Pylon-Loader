using System;
using Il2CppMicrosoft.Xna.Framework;

namespace Pylon.ModLoader.UI
{
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
            public float Value = 0f;
            public GUISlider.DragState Drag;
        }

        private sealed class ColorState
        {
            public string Hex;
            public float[] Hsl = { 0f, 0f, 1f };
        }

        private sealed class Row
        {
            public string Kind;
            public ModConfigEntry Entry;
            public ModConfigOption O;
            public string Key;
            public float Height;
        }

        private sealed class TabItem
        {
            public TransactionButton_Layout Layout;
            public float Scale = 1f;
        }

        private sealed class TabShape
        {
            public float IconPx;
            public TransactionButton_Layout Start;
            public TransactionButton_Layout Middle;
            public TransactionButton_Layout End;
            public float ToMiddle;
            public float MiddleStep;
            public float ToEnd;
        }

        private sealed class PartsData
        {
            public String_Layout Empty;
            public String_Layout Header;
            public String_Layout Label;
            public String_Layout Cycle;
            public Panel_Layout Divider;
            public Panel_Layout ChoiceOn;
            public Panel_Layout ChoiceOff;
            public Panel_Layout Swatch;
            public Panel_Layout Greyout;
            public Panel_Layout PopBacking;
            public TransactionButton_Layout Option;
        }

        private sealed class PopupState
        {
            public Row Row;
            public Rectangle Rect;
            public bool Down;
            public bool DownInside;
            public bool Armed;
            public float[] Scales;
        }

        private sealed class SegmentsData
        {
            public float Y;
            public float Each;
            public float Height;
            public Func<float, Vector2> AnchorPos;
            public Func<int, float> XOf;
        }
    }
}