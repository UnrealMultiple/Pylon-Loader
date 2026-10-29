using System;
using System.Collections.Generic;
using System.IO;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Pylon.ModLoader.Textures;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;
using SpriteFont = Il2CppMicrosoft.Xna.Framework.Graphics.SpriteFont;

namespace Pylon.ModLoader.UI
{
    public static partial class ModConfigMenu
    {
        private static void DrawScreen()
        {
            var S = SettingsOverlay_Layout.Instance;
            if (_guard)
            {
                _guardIdle = Main.mouseLeft ? 0 : _guardIdle + 1;
                if (_guardIdle >= 2) _guard = false;
            }
            GUIPanel.Draw(S.Backing, false, NoColor(), NoColor(), NoColor());
            GUIPanel.Draw(S.MenuDivider, false, NoColor(), NoColor(), NoColor());
            GUIPanel.Draw(S.MenuDivider2, false, NoColor(), NoColor(), NoColor());

            var mods = new List<Mod>();
            foreach (var m in ModLoader.Mods)
            {
                var entries = Entries(m);
                foreach (var e in entries) { if (e.Options.Count > 0) { mods.Add(m); break; } }
            }
            Mod mod = null;
            foreach (var m in mods) { if (m.Name == _selected) { mod = m; break; } }
            if (mod == null && mods.Count > 0) mod = mods[0];
            if (mod != null) mod = DrawTabs(S, mods, mod);
            _selected = mod != null ? mod.Name : null;
            var title = mod != null ? mod.Name : Text("button");
            GUIStringButton.DrawButton(S.Title, title, ref _scales.Title, false, true);
            DrawList(S, mod);

            var action = DrawActions(S, mod);
            Safe("ModConfigMenu.Popup", () => DrawPopup(S));
            if (action != null && !Blocked())
            {
                XNAUIInputLayer.Instance.CaptureUICrusorDrag(-1);
                if (action == "defaults")
                {
                    ModConfigLoader.Defaults(Entries(mod));
                    SoundEngine.PlaySound(TICK);
                }
                else if (action == "apply")
                {
                    // 草稿从刚生效的值重新开始
                    ModConfigLoader.Apply();
                    ModConfigLoader.Begin();
                    SoundEngine.PlaySound(TICK);
                }
                else
                {
                    Close(true);
                    return;
                }
            }
            if (KeyboardInput.GetKeyUp(UnityEngine.KeyCode.Escape))
            {
                var gi = GUIInstance.Active;
                if (gi != null && gi.GUIKeyboardMappings != null) gi.GUIKeyboardMappings.DisableEscapeKeyUsage();
                if (_popup != null) _popup = null;
                else Close();
            }
        }

        private static string DrawActions(SettingsOverlay_Layout S, Mod mod)
        {
            var K = KeyboardMappings_Layout.Instance;
            var entries = mod != null ? Entries(mod) : new List<ModConfigEntry>();
            bool pending = ModConfigLoader.Pending();
            string clicked = null;
            if (ActionButton("discard", K.ResetSettings, pending ? Locale("Mobile.Discard") : BackText(), false)) clicked = "discard";
            if (ActionButton("defaults", K.Defaults, Locale("Mobile.Default"), entries.Count == 0 || ModConfigLoader.AtDefaults(entries))) clicked = "defaults";
            if (ActionButton("apply", K.Close, Locale("Mobile.ApplyChanges"), !pending)) clicked = "apply";
            return clicked;
        }

        private static bool ActionButton(string id, TransactionButton_Layout layout, string text, bool disabled)
        {
            var state = GUITransactionButton.Draw(layout, (Item)null, text, disabled,
                ref ScaleOf(id), false, Focus(), false, false);
            return state == GUITransactionButton.InputState.Clicked && !disabled;
        }

        private static ref float ScaleOf(string id)
        {
            if (id == "discard") return ref _scales.Discard;
            if (id == "defaults") return ref _scales.Defaults;
            if (id == "apply") return ref _scales.Apply;
            return ref _scales.Button;
        }

        private static Mod DrawTabs(SettingsOverlay_Layout S, List<Mod> mods, Mod selected)
        {
            var C = S.Categories;
            GUIPanel.Draw(C.Backing, false, NoColor(), NoColor(), NoColor());
            var shape = _tabShape ?? (_tabShape = MeasureTabShape(C));
            int n = mods.Count;
            if (n == 1)
            {
                var tab = Tab(shape.Middle, mods[0], "middle", shape.IconPx);
                tab.Layout.Location = new Vector2(shape.Start.Location.X, shape.Start.Location.Y);
                GUITransactionButton.Draw(tab.Layout, (Item)null, mods[0].Name, false, ref tab.Scale,
                    true, Focus(), false, false);
                return mods[0];
            }

            var xs = new (string kind, float x)[n];
            float x = shape.Start.Location.X;
            for (int i = 0; i < n; i++)
            {
                string kind = i == 0 ? "start" : i == n - 1 ? "end" : "middle";
                if (i > 0) x += kind == "end" ? shape.ToEnd : i == 1 ? shape.ToMiddle : shape.MiddleStep;
                xs[i] = (kind, x);
            }
            Vector2 At(TransactionButton_Layout layout, float lx) =>
                LayoutCalculator.GetAnchoredPosition(layout.AnchorControl, layout.Anchor, new Vector2(lx, layout.Location.Y));
            var start = shape.Start;
            var origin = At(start, start.Location.X);
            float perRaw = (At(start, start.Location.X + 100).X - origin.X) / 100f;
            float perUnit = perRaw != 0 ? perRaw : 1f;
            var bounds = ListBounds(S);
            float left = origin.X;
            float right = bounds.left + bounds.width;
            var last = xs[n - 1];
            float contentRight = origin.X + (last.x - start.Location.X) * perUnit + ShapeLayout(shape, last.kind).overloadedSize.X;
            float height = start.overloadedSize.Y;
            var area = Rect(left, origin.Y, right - left, height);
            float max = Math.Max(0, contentRight - right);
            TabDrag(area, max);

            Mod chosen = selected;
            bool clip = max > 0;
            // 只裁左右选中态和亮条会高出这排一行
            var band = Rect(left, origin.Y - height, right - left, height * 3);
            if (clip) SpriteBatchItem.EnableClipping(band, band, Main.spriteBatch, false);
            try
            {
                for (int i = 0; i < n; i++)
                {
                    var (kind, tx) = xs[i];
                    var tab = Tab(ShapeLayout(shape, kind), mods[i], kind, shape.IconPx);
                    tab.Layout.Location = new Vector2(tx - _tabScroll / perUnit, start.Location.Y);
                    bool on = ReferenceEquals(mods[i], selected);
                    var state = GUITransactionButton.Draw(tab.Layout, (Item)null, mods[i].Name, false, ref tab.Scale,
                        on, Focus(), false, false);
                    if (state == GUITransactionButton.InputState.Clicked && _tabPressIn && !_tabDragged
                        && !Blocked() && !on)
                    {
                        chosen = mods[i];
                        ResetScroll();
                        SoundEngine.PlaySound(TICK);
                    }
                }
            }
            finally
            {
                if (clip) SpriteBatchItem.DisabledClipping();
            }
            // 拖完抬手的那个触点不变成点 tab
            if (!Main.mouseLeft)
            {
                _tabDragged = false;
                _tabPressIn = false;
            }
            return chosen;
        }

        private static TransactionButton_Layout ShapeLayout(TabShape shape, string kind)
        {
            if (kind == "start") return shape.Start;
            if (kind == "end") return shape.End;
            return shape.Middle;
        }

        private static void TabDrag(Rectangle area, float max)
        {
            GUIInputRegionManager.Instance.RegisterInputRegion(area);
            bool inside = Main.mouseX >= area.X && Main.mouseX <= area.X + area.Width
                          && Main.mouseY >= area.Y && Main.mouseY <= area.Y + area.Height;
            if (Main.mouseLeft)
            {
                if (_tabPressX == null)
                {
                    _tabPressX = Main.mouseX;
                    _tabLastX = Main.mouseX;
                    _tabDragged = false;
                    _tabPressIn = inside && !_guard && !Modal();
                    _tabMomentum = 0;
                }
                else if (max > 0 && _tabPressIn && Math.Abs(Main.mouseX - _tabPressX.Value) > DRAG_PX)
                {
                    _tabDragged = true;
                }
                if (_tabDragged)
                {
                    float delta = _tabLastX.Value - Main.mouseX;
                    _tabScroll += delta;
                    _tabMomentum = delta;
                }
                _tabLastX = Main.mouseX;
            }
            else
            {
                _tabPressX = null;
                _tabLastX = null;
                if (Math.Abs(_tabMomentum) > 0.5f)
                {
                    _tabScroll += _tabMomentum;
                    _tabMomentum *= FRICTION;
                }
                else
                {
                    _tabMomentum = 0;
                }
            }
            if (_tabScroll < 0 || _tabScroll > max)
            {
                _tabScroll = Math.Max(0, Math.Min(_tabScroll, max));
                _tabMomentum = 0;
            }
        }

        private static TabShape MeasureTabShape(SettingsOverlayCategories_Layout C)
        {
            var general = C.General;
            var row = new List<TransactionButton_Layout>();
            foreach (var key in new[]
                     {
                         "General", "Cursor", "Video", "Language", "Interface", "Info", "Gameplay", "Sound",
                         "Multiplayer", "GraphicalQuality", "KeyboardMouse", "GameplayControls",
                     })
            {
                var layout = CategoryLayout(C, key);
                if (layout == null) continue;
                var p = layout.Location;
                if (Math.Abs(p.Y - general.Location.Y) >= 1 || p.X < general.Location.X - 1) continue;
                bool dup = false;
                foreach (var r in row) { if (Math.Abs(r.Location.X - p.X) < 1) { dup = true; break; } }
                if (dup) continue;
                row.Add(layout);
            }
            row.Sort((a, b) => a.Location.X.CompareTo(b.Location.X));
            float X(int i) => row[i].Location.X;
            float step = row.Count > 1 ? X(1) - X(0) : general.overloadedSize.X + 4;
            int last = 0;
            while (last + 1 < row.Count && Math.Abs(X(last + 1) - X(last) - step) < 4) last++;
            var group = row.GetRange(0, last + 1);
            float Gx(int i) => group[i].Location.X;
            int k = group.Count;

            var theirs = general.IconTexture != null ? general.IconTexture.Texture : null;
            float iconPx = (theirs != null ? TextureHeightSafe(theirs) : 32f)
                           * (general.ForceIconScale ? general.ForcedIconScale : 1f);

            return new TabShape
            {
                IconPx = iconPx,
                Start = group.Count > 0 ? group[0] : general,
                Middle = k > 2 ? group[1] : (group.Count > 0 ? group[0] : general),
                End = group.Count > 0 ? group[k - 1] : general,
                ToMiddle = k > 1 ? Gx(1) - Gx(0) : step,
                MiddleStep = k > 2 ? Gx(2) - Gx(1) : step,
                ToEnd = k > 1 ? Gx(k - 1) - Gx(k - 2) : step,
            };
        }

        private static TransactionButton_Layout CategoryLayout(SettingsOverlayCategories_Layout C, string key)
        {
            switch (key)
            {
                case "General": return C.General;
                case "Cursor": return C.Cursor;
                case "Video": return C.Video;
                case "Language": return C.Language;
                case "Interface": return C.Interface;
                case "Info": return C.Info;
                case "Gameplay": return C.Gameplay;
                case "Sound": return C.Sound;
                case "Multiplayer": return C.Multiplayer;
                case "GraphicalQuality": return C.GraphicalQuality;
                case "KeyboardMouse": return C.KeyboardMouse;
                case "GameplayControls": return C.GameplayControls;
                default: return null;
            }
        }

        private static TabItem Tab(TransactionButton_Layout template, Mod mod, string kind, float iconPx)
        {
            string key = mod.Name + ":" + kind;
            if (Tabs.TryGetValue(key, out var tab)) return tab;
            var layout = Clone(template);
            Safe("ModConfigMenu: ícone de " + mod.Name, () =>
            {
                var texture = ModIcon(mod);
                var icon = new Texture_Layout();
                icon.TextureId = "pylon/mod/" + mod.Name;
                icon._lastTextureId = icon.TextureId;
                icon._texture = texture;
                layout.IconTexture = icon;
                layout.ForceIconScale = true;
                layout.ForcedIconScale = iconPx / Math.Max(1, TextureHeightSafe(texture));
            });
            tab = new TabItem { Layout = layout, Scale = 1f };
            Tabs[key] = tab;
            return tab;
        }

        private static GameTexture2D ModIcon(Mod mod)
        {
            if (Icons.TryGetValue(mod.Name, out var texture)) return texture;
            GameTexture2D loaded;
            var file = !string.IsNullOrEmpty(mod.Directory) ? Path.Combine(mod.Directory, "icon.png") : null;
            if (file != null && File.Exists(file)) loaded = mod.LoadTexture("icon.png");
            else loaded = BuiltinIcon();
            Icons[mod.Name] = loaded;
            return loaded;
        }

        internal static GameTexture2D _builtinIcon;

        internal static GameTexture2D BuiltinIcon()
        {
            if (_builtinIcon != null) return _builtinIcon;
            var assembly = typeof(ModConfigMenu).Assembly;
            string name = null;
            foreach (var candidate in assembly.GetManifestResourceNames())
            {
                if (candidate.EndsWith(".loaderIcon.png", StringComparison.Ordinal)) { name = candidate; break; }
            }
            if (name == null)
            {
                foreach (var candidate in assembly.GetManifestResourceNames())
                {
                    if (candidate.EndsWith(".pylon.png", StringComparison.Ordinal)) { name = candidate; break; }
                }
            }
            if (name == null) throw new InvalidOperationException("框架内建图标缺失（Textures/loaderIcon.png 没嵌进来）");
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) throw new InvalidOperationException("框架内建图标读不出来");
            var data = new byte[stream.Length];
            int offset = 0;
            while (offset < data.Length)
            {
                int read = stream.Read(data, offset, data.Length - offset);
                if (read <= 0) break;
                offset += read;
            }
            _builtinIcon = PylonTexture.Decode(data, BuiltinIconId);
            return _builtinIcon;
        }

        private static PartsData Parts(SettingsOverlay_Layout S)
        {
            if (_parts != null) return _parts;
            var t = S.ToggleTemplate;
            var p = S.PulldownTemplate;
            var empty = ToScreen(Clone(t.Option1Label));
            empty.Alignment = LayoutCalculator.AnchorType.TopLeft;
            var header = ToScreen(Clone(p.PulldownTitle));
            header.Alignment = LayoutCalculator.AnchorType.CentreBoth;
            var option = Clone(p.Option1);
            option.AnchorControl = p.PulldownButton.AnchorControl;
            option.Anchor = p.PulldownButton.Anchor;
            _parts = new PartsData
            {
                Empty = empty,
                Header = header,
                Divider = ToScreen(Clone(S.MenuDivider)),
                ChoiceOn = Clone(t.Option1Enabled),
                ChoiceOff = Clone(t.Option1Disabled),
                Swatch = Clone(t.Option1Enabled),
                Cycle = Clone(S.SliderTemplate.Value),
                Label = Clone(t.Option1Label),
                Greyout = ToScreen(Clone(p.PulldownGreyout)),
                PopBacking = ToScreen(Clone(p.PulldownBacking)),
                Option = option,
            };
            return _parts;
        }

        private static String_Layout ToScreen(String_Layout x)
        {
            x.AnchorControl = ControlAnchor.ControlId.Screen;
            x.Anchor = LayoutCalculator.AnchorType.TopLeft;
            return x;
        }

        private static Panel_Layout ToScreen(Panel_Layout x)
        {
            x.AnchorControl = ControlAnchor.ControlId.Screen;
            x.Anchor = LayoutCalculator.AnchorType.TopLeft;
            return x;
        }

        private static List<Row> Rows(SettingsOverlay_Layout S, Mod mod)
        {
            var rows = new List<Row>();
            if (mod == null) return rows;
            float height = S.ToggleTemplate.ToggleButton.overloadedSize.Y + GAP;
            foreach (var entry in Entries(mod))
            {
                foreach (var o in entry.Options)
                {
                    string key = entry.Name + "." + o.Key;
                    if (o.Kind == OptionKind.Color)
                    {
                        foreach (var part in new[] { "color", "hue", "saturation", "lightness" })
                        {
                            rows.Add(new Row { Kind = part, Entry = entry, O = o, Key = key + ":" + part, Height = height });
                        }
                    }
                    else
                    {
                        rows.Add(new Row
                        {
                            Kind = o.Kind.ToString().ToLowerInvariant(),
                            Entry = entry,
                            O = o,
                            Key = key,
                            Height = o.Kind == OptionKind.Header ? height + HEADER_EXTRA : height,
                        });
                    }
                }
            }
            return rows;
        }

        private static void DrawList(SettingsOverlay_Layout S, Mod mod)
        {
            var P = Parts(S);
            var top = AtPanel(S.MenuDivider).Y + 8;
            var bottom = AtPanel(S.MenuDivider2).Y - 6;
            var bounds = ListBounds(S);
            float left = bounds.left, width = bounds.width;
            _listTop = top;
            _listBottom = bottom;
            var rows = Rows(S, mod);

            if (rows.Count == 0)
            {
                P.Empty.Location = new Vector2(left + 12, top + 10);
                GUIString.Draw(P.Empty, Text("empty"), P.Empty.Color, false);
                return;
            }

            float total = 0;
            foreach (var row in rows) total += row.Height;
            total -= GAP;
            var area = Rect(left, top, width, bottom - top);
            Scroll(area, total, left, top, width, bottom);

            var saved = ControlAnchor._gridItemRegion;
            SpriteBatchItem.EnableClipping(area, area, Main.spriteBatch, true);
            try
            {
                float y = top - _scroll;
                foreach (var row in rows)
                {
                    float h = row.Height - GAP;
                    if (y + h > top && y < bottom)
                    {
                        ControlAnchor._gridItemRegion = Rect(left, y, width, h);
                        Safe("ModConfig " + row.Key, () => DrawOption(S, P, row));
                    }
                    y += row.Height;
                }
            }
            finally
            {
                SpriteBatchItem.DisabledClipping();
                ControlAnchor._gridItemRegion = saved;
            }
        }

        private static (float left, float width) ListBounds(SettingsOverlay_Layout S)
        {
            var ach = Achievements_Layout.Instance;
            float width = ach != null ? ach.ItemBacking.Size.X : 0f;
            float left = LayoutCalculator.GetAnchoredPosition(S.Backing.AnchorControl, S.Backing.Anchor, new Vector2(0, 0)).X - width / 2;
            return (left, width);
        }

        private static Rectangle Rect(float x, float y, float width, float height)
        {
            return new Rectangle(
                (int)Math.Round(x, MidpointRounding.AwayFromZero),
                (int)Math.Round(y, MidpointRounding.AwayFromZero),
                (int)Math.Round(width, MidpointRounding.AwayFromZero),
                (int)Math.Round(height, MidpointRounding.AwayFromZero));
        }

        private static Vector2 AtPanel(Panel_Layout layout) =>
            LayoutCalculator.GetAnchoredPosition(layout.AnchorControl, layout.Anchor, layout.Location);

        private static void Scroll(Rectangle area, float total, float left, float top, float width, float bottom)
        {
            GUIInputRegionManager.Instance.RegisterInputRegion(area);
            float max = Math.Max(0, total - (bottom - top));
            bool inside = Main.mouseX >= left && Main.mouseX <= left + width && Main.mouseY >= top && Main.mouseY <= bottom;

            bool fresh = Main.mouseLeft && _pressY == null;
            if (Main.mouseLeft)
            {
                if (fresh)
                {
                    _pressY = Main.mouseY;
                    _lastY = Main.mouseY;
                    _dragged = false;
                    _pressInList = inside && !_guard && !Modal();
                    _momentum = 0;
                }
                else if (_slider == null && _pressInList && Math.Abs(Main.mouseY - _pressY.Value) > DRAG_PX)
                {
                    _dragged = true;
                }
                if (_dragged)
                {
                    float delta = _lastY.Value - Main.mouseY;
                    _scroll += delta;
                    _momentum = delta;
                }
                _lastY = Main.mouseY;
            }
            else
            {
                _pressY = null;
                _lastY = null;
                _slider = null;
                ModConfigLoader.Flush();
                if (Math.Abs(_momentum) > 0.5f)
                {
                    _scroll += _momentum;
                    _momentum *= FRICTION;
                }
                else
                {
                    _momentum = 0;
                }
            }
            if (_scroll < 0 || _scroll > max)
            {
                _scroll = Math.Max(0, Math.Min(_scroll, max));
                _momentum = 0;
            }
        }

        private static void Label(PartsData P, String_Layout template, float x, float width, string text, Color color, bool centered = false)
        {
            var label = P.Label;
            label.AnchorControl = template.AnchorControl;
            label.Anchor = template.Anchor;
            label.Alignment = centered
                ? (LayoutCalculator.AnchorType)(((int)template.Alignment & ~7) | 2)
                : template.Alignment;
            var font = TryFont(template);
            float measured = font != null ? font.MeasureString(text).X : 0f;
            float room = Math.Max(8f, width - 8f);
            label.Scale = measured > 0 ? Math.Min(template.Scale, room / measured) : template.Scale;
            label.Location = new Vector2(x, template.Location.Y);
            GUIString.Draw(label, text, color, false);
        }

        public static string Fit(TransactionButton_Layout layout, string text, float width)
        {
            var label = layout != null ? layout.Label : null;
            var font = TryFont(label);
            if (font == null) return text;
            float Measure(string s) => font.MeasureString(s).X * label.Scale;
            if (Measure(text) <= width) return text;
            string cut = text;
            while (cut.Length > 1 && Measure(cut + "...") > width) cut = cut.Substring(0, cut.Length - 1);
            return cut + "...";
        }

        private static RowState State(Row row)
        {
            if (!RowStates.TryGetValue(row.Key, out var st))
            {
                RowStates[row.Key] = st = new RowState();
            }
            return st;
        }

        private static SegmentsData Segments(SettingsOverlayToggle_Layout t, int n)
        {
            var a = t.Option1Enabled;
            var b = t.Option2Enabled;
            float y = a.Location.Y;
            float AnchorX(float x) => LayoutCalculator.GetAnchoredPosition(a.AnchorControl, a.Anchor, new Vector2(x, y)).X;
            float denominator = a.Location.X - b.Location.X;
            float dir = denominator != 0
                ? Math.Sign((AnchorX(a.Location.X) - AnchorX(b.Location.X)) / denominator)
                : 1f;
            if (dir == 0) dir = 1;
            float ua = a.Location.X * dir, ub = b.Location.X * dir;
            float right = Math.Max(ua + a.Size.X / 2, ub + b.Size.X / 2);
            float span = right - Math.Min(ua - a.Size.X / 2, ub - b.Size.X / 2);
            float left = right - span * Math.Max(1, n / 2f);
            float each = (right - left) / n;
            return new SegmentsData
            {
                Y = y,
                Each = each,
                Height = a.Size.Y,
                AnchorPos = lx => LayoutCalculator.GetAnchoredPosition(a.AnchorControl, a.Anchor, new Vector2(lx, y)),
                XOf = i => (left + each * (i + 0.5f)) * dir,
            };
        }
    }
}