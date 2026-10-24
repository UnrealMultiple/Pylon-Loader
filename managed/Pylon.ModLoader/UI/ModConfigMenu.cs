using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using NullableColor = Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Graphics.Color>;
using NullableRect = Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Rectangle>;
using NullableMatrix = Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Matrix>;
using Terraria;
using Terraria.Localization;
using Pylon.ModLoader.Textures;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;
using SpriteFont = Il2CppMicrosoft.Xna.Framework.Graphics.SpriteFont;

namespace Pylon.ModLoader.UI
{
    public static partial class ModConfigMenu
    {
        private static bool _open;
        private static Where _where = Where.None;                   
        private static GUIOpenUIButtonSetting _menuEntry;           
        private static TransactionButton_Layout _button;            
        private static PartsData _parts;                            
        private static Scales _scales;                              
        private static readonly Dictionary<string, RowState> RowStates = [];
        private static readonly Dictionary<string, ColorState> ColorStates = [];
        private static string _selected;
        private static readonly Dictionary<string, TabItem> Tabs = new(StringComparer.Ordinal);
        private static TabShape _tabShape;
        private static readonly Dictionary<string, GameTexture2D> Icons = new(StringComparer.Ordinal);
        private static float _scroll;                              
        private static float _momentum;                            
        private static int? _lastY;                                
        private static string _slider;                             
        private static bool _pressInList;                          
        private static bool _guard;                                
        private static int _guardIdle;                             
        private static int? _pressY;                               
        private static bool _dragged;                                
        private static PopupState _popup;                            
        private static float _listTop;                               
        private static float _listBottom;                            
        private static float _tabScroll;                             
        private static float _tabMomentum;                           
        private static int? _tabPressX;                              
        private static int? _tabLastX;                               
        private static bool _tabDragged;                             
        private static bool _tabPressIn;                             

        private const float GAP = 4f;
        private const float HEADER_EXTRA = 8f;
        private const float SWATCH_INSET = 4f;
        private const float CYCLE_X = 24f;
        private const float POPUP_MARGIN = 8f;
        private const float DRAG_PX = 8f;
        private const float FRICTION = 0.92f;
        private const int MIDDLE = 16;                   
        private const int ALIGN_LEFT = 1, ALIGN_CENTER = 2, ALIGN_RIGHT = 4;
        private const float HEADER_PAD = 12f;
        private const float ICON_SCALE = 1.1f;
        private const int MENU_OPEN = 10, MENU_CLOSE = 11, TICK = 12;
        private const string BuiltinIconId = "pylon/icon";


        private static Harmony _harmony;
        private static ModLogger _logger;
        private static bool _forMainMenu;
        private static bool _pending;
        private static bool _installed;
        private static bool _insidePauseDraw;           
        private static string _backCulture;             
        private static string _backText;
        private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
        public static GUIOpenUIButtonSetting MenuEntry => _menuEntry;

        public static void Install(Harmony harmony, ModLogger logger, bool forMainMenu)
        {
            if (_installed || _pending) return;
            _harmony = harmony;
            _logger = logger;
            _forMainMenu = forMainMenu;
            _pending = true;
        }

        /// <summary>ModUI.Update 每帧调用。</summary>
        public static void TryInstallPending()
        {
            if (_installed || !_pending) return;
            if (!UiReady()) return;
            _pending = false;
            try
            {
                Hook();
                _installed = true;
                _logger?.Info("Mod 配置屏已挂（暂停菜单 + 主菜单设置一行）");
            }
            catch (Exception e)
            {
                _logger?.Error("Mod 配置屏挂钩失败：" + ModUI.Flatten(e));
            }
        }

        private static bool UiReady()
        {
            try { return GUIInstance.Active != null; }
            catch { return false; }
        }

        private static void Hook()
        {
            _scales = new Scales();

            Patch("PauseSetup", typeof(GUISettingsPauseMenu), "SetupOffsets", 1, false, null, false, true);
            var pauseDraw = Find(typeof(GUISettingsPauseMenu), "Draw", 0, false, null);
            if (pauseDraw != null)
            {
                try
                {
                    _harmony.Patch(pauseDraw,
                        prefix: new HarmonyMethod(typeof(ModConfigMenu).GetMethod(nameof(PauseDrawPrefix), StaticNonPublic)),
                        postfix: new HarmonyMethod(typeof(ModConfigMenu).GetMethod(nameof(PauseDrawPostfix), StaticNonPublic)));
                    _logger?.Info("Mod 配置屏已挂：GUISettingsPauseMenu.Draw");
                }
                catch (Exception e)
                {
                    _logger?.Error("挂 GUISettingsPauseMenu.Draw 失败：" + ModUI.Flatten(e));
                }
            }
            Patch("PanelDraw", typeof(GUIPanel), "Draw", 5, true, null, false, true);
            Patch("ClearWorld", typeof(WorldGen), "clearWorld", 0, true, null, false, true);

            if (_forMainMenu)
            {
                Patch("OverlaySetCategory", typeof(GUISettingsOverlay), "SetCategory", 1, false, null, false, true);
                Patch("OverlayDraw", typeof(GUISettingsOverlay), "Draw", 0, false, null, true, false);
                Patch("OverlayClose", typeof(GUISettingsOverlay), "Close", 0, false, null, true, false);
                Patch("OpenGetOption", typeof(GUIOpenUIButtonSetting), "GetOption", 0, false, null, false, true);
                Patch("OpenGetButtonCaption", typeof(GUIOpenUIButtonSetting), "GetButtonCaption", 0, false, null, false, true);
                Patch("OpenOpenUI", typeof(GUIOpenUIButtonSetting), "OpenUI", 0, false, null, true, false);
            }
        }

        private static readonly BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>按类型 + 名字 + 参数个数找方法。</summary>
        private static MethodInfo Find(Type type, string name, int paramCount, bool isStatic, Type firstParam)
        {
            foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Static | BindingFlags.Instance))
            {
                if (candidate.Name != name) continue;
                if (candidate.IsStatic != isStatic) continue;
                var parameters = candidate.GetParameters();
                if (parameters.Length != paramCount) continue;
                if (firstParam != null && parameters.Length > 0 && !firstParam.IsAssignableFrom(parameters[0].ParameterType)) continue;
                return candidate;
            }
            return null;
        }

        /// <summary>ModUI 同款：找目标方法、打补丁、失败记日志。</summary>
        private static void Patch(string label, Type type, string name, int paramCount, bool isStatic, Type firstParam, bool prefix, bool postfix)
        {
            var target = Find(type, name, paramCount, isStatic, firstParam);
            if (target == null)
            {
                _logger?.Error($"找不到 {type.Name}.{name}（{paramCount} 参）");
                return;
            }
            try
            {
                var prefixMethod = prefix
                    ? new HarmonyMethod(typeof(ModConfigMenu).GetMethod(label + "Prefix", StaticNonPublic) ??
                                        typeof(ModConfigMenu).GetMethod(label, StaticNonPublic))
                    : null;
                var postfixMethod = postfix
                    ? new HarmonyMethod(typeof(ModConfigMenu).GetMethod(label + "Postfix", StaticNonPublic) ??
                                        typeof(ModConfigMenu).GetMethod(label, StaticNonPublic))
                    : null;
                _harmony.Patch(target, prefix: prefixMethod, postfix: postfixMethod);
                _logger?.Info($"Mod 配置屏已挂：{type.Name}.{target.Name}");
            }
            catch (Exception e)
            {
                _logger?.Error($"挂 {type.Name}.{name} 失败：{ModUI.Flatten(e)}");
            }
        }

        private static void PauseSetupPostfix(GUISettingsPauseMenu __instance, bool __0)
        {
            if (!__0) return;
            Safe("ModConfigMenu.SetupOffsets", () => Layout(__instance));
        }

        private static bool PauseDrawPrefix(GUISettingsPauseMenu __instance)
        {
            _insidePauseDraw = true;
            if (_where != Where.Pause) return true;
            if (!Safe("ModConfigMenu.Draw", DrawScreen)) Close(false);
            return false;
        }

        private static void PauseDrawPostfix(GUISettingsPauseMenu __instance)
        {
            _insidePauseDraw = false;
        }

        /// <summary>暂停菜单画 Backing 时补我们的按钮。</summary>
        private static void PanelDrawPostfix(Panel_Layout __0)
        {
            if (!_insidePauseDraw) return;
            var L = SettingsPauseMenu_Layout.Instance;
            if (_open || L == null || !SameNative(__0, L.Backing)) return;
            Safe("ModConfigMenu.Button", () => DrawButton(L));
        }

        private static void ClearWorldPostfix()
        {
            Close(false);
        }

        private static void OverlaySetCategoryPostfix(GUISettingsOverlay __instance, SettingsOverlayOptionCategories_Layout.Category __0)
        {
            Safe("ModConfigMenu.SetCategory", () => AddMenuEntry(__instance));
        }

        private static bool OverlayDrawPrefix(GUISettingsOverlay __instance)
        {
            if (_where != Where.Menu) return true;
            if (!Safe("ModConfigMenu.Draw", DrawScreen)) Close(false);
            return false;
        }

        private static bool OverlayClosePrefix(GUISettingsOverlay __instance)
        {
            if (_where == Where.Menu) Close(false);
            return true;
        }

        private static void OpenGetOptionPostfix(GUIOpenUIButtonSetting __instance, ref string __result)
        {
            if (SameNative(__instance, _menuEntry)) __result = Text("row");
        }

        private static void OpenGetButtonCaptionPostfix(GUIOpenUIButtonSetting __instance, ref string __result)
        {
            if (SameNative(__instance, _menuEntry)) __result = Text("open");
        }

        private static bool OpenOpenUIPrefix(GUIOpenUIButtonSetting __instance)
        {
            if (!SameNative(__instance, _menuEntry)) return true;
            XNAUIInputLayer.Instance.CaptureUICrusorDrag(-1);
            Open(Where.Menu);
            return false;
        }

        private static void AddMenuEntry(GUISettingsOverlay overlay)
        {
            if (!Main.gameMenu || overlay.Selected != SettingsOverlayOptionCategories_Layout.Category.General) return;
            var list = overlay._categorySettings;
            if (list == null) return;
            _menuEntry ??= new GUIOpenUIButtonSetting(GUISettingID.None)
                {
                    _category = SettingsOverlayOptionCategories_Layout.Category.General
                };
            if (!list.Contains(_menuEntry)) list.Insert(0, _menuEntry);
        }

        private static void Open(Where where)
        {
            SoundEngine.PlaySound(MENU_OPEN);
            _open = true;
            _where = where;
            ModConfigLoader.Begin();
            ResetScroll();
            Guard();
        }

        private static List<ModConfigEntry> Entries(Mod mod)
        {
            bool inWorld = _where != Where.Menu;
            var result = new List<ModConfigEntry>();
            foreach (var e in ModConfigLoader.Of(mod))
            {
                if (!inWorld || e.VisibleInWorld) result.Add(e);
            }
            return result;
        }

        private static string Text(string key)
        {
            return key switch
            {
                "button" => "Mod 设置",
                "row" => "Mod 设置:",
                "open" => "打开",
                "empty" => "没有 Mod 提供选项。",
                _ => key,
            };
        }

        private static bool IsPt()
        {
            var culture = ModLocalization.ActiveCultureName ?? "";
            return culture.StartsWith("pt", StringComparison.OrdinalIgnoreCase);
        }

        private static string Locale(string key) => Language.GetTextValue(key);

        private static string BackText()
        {
            string culture = ModLocalization.ActiveCultureName ?? "";
            if (_backCulture == culture && _backText != null) return _backText;

            string text = null;
            foreach (var key in new[] { "UI.Back", "Mobile.Back", "LegacyMenu.15" })
            {
                string value = null;
                Safe("ModConfigMenu: " + key, () => value = Locale(key));
                if (!string.IsNullOrEmpty(value) && value != key)
                {
                    text = value;
                    break;
                }
            }
            text ??= (culture.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "Voltar" : "Back");
            _backCulture = culture;
            _backText = text;
            return text;
        }

        private static float Step(SettingsPauseMenu_Layout L)
        {
            return L.Close.overloadedSize.Y + L.ButtonSpacing;
        }

        private static TransactionButton_Layout ButtonLayout(SettingsPauseMenu_Layout L)
        {
            if (_button != null) return _button;
            var ours = Clone(L.Settings);
            Safe("ModConfigMenu: ícone", () =>
            {
                var icon = new Texture_Layout
                {
                    TextureId = BuiltinIconId
                };
                icon._lastTextureId = icon.TextureId;
                icon._texture = BuiltinIcon();
                ours.IconTexture = icon;
                ours.ForceIconScale = true;
                ours.ForcedIconScale = ICON_SCALE;
            });
            _button = ours;
            return ours;
        }




        /// <summary>暂停菜单布局让出一行，并把我们的按钮加进控制器列表。</summary>
        private static void Layout(GUISettingsPauseMenu self)
        {
            var L = SettingsPauseMenu_Layout.Instance;
            if (L == null) return;
            float step = Step(L);
            void Move(TransactionButton_Layout b, float dy)
            {
                var p = b.Location;
                b.Location = new Vector2(p.X, p.Y + dy);
            }

            var backing = L.Backing;
            backing.Size = new Vector2(backing.Size.X, backing.Size.Y + step);
            var above = new List<TransactionButton_Layout> { L.Close, L.Controls, L.Settings };
            if (self.includeMPInviteActive) above.Add(L.Invite);
            var below = new List<TransactionButton_Layout> { L.Achievements, L.Bestiary, L.Home };
            if (self.creativeModeActive) below.Add(L.JourneySettings);
            foreach (var b in above) Move(b, -step / 2);
            foreach (var b in below) Move(b, step / 2);

            var ours = ButtonLayout(L);
            var settings = L.Settings.Location;
            ours.Location = new Vector2(settings.X, settings.Y + step);

            var list = self._controllerList;
            if (list == null) return;
            var items = list._items;
            var all = new List<TransactionButton_Layout>();
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++) all.Add(items[i]);
            }

            foreach (var x in all)
            {
                if (SameNative(x, ours)) return;
            }
            all.Add(ours);
            // 用 T[] 构造一步建好原生数组（手动逐元素赋值会段错误，实测构造内部拷贝没问题）
            var array = new Il2CppReferenceArray<TransactionButton_Layout>([.. all]);
            list._items = array;
            list.Activate();
        }


        /// <summary>暂停菜单画 Backing 后补画我们的按钮。</summary>
        private static void DrawButton(SettingsPauseMenu_Layout L)
        {
            if (_open) return;
            var ours = ButtonLayout(L);
            ours.overloadedSize = L.Settings.overloadedSize;
            ours.Location = new Vector2(L.Settings.Location.X, ours.Location.Y);
            var state = GUITransactionButton.Draw(ours, (Item)null, Text("button"), false,
                ref _scales.Button, false, Focus(), false, false);
            if (state != GUITransactionButton.InputState.Clicked) return;

            XNAUIInputLayer.Instance.CaptureUICrusorDrag(-1);
            Open(Where.Pause);
        }

        private static bool Focus()
        {
            var gi = GUIInstance.Active;
            var pad = gi?.GUIVirtualInputController;
            return pad != null && pad.ControllerActive;
        }

        private static void Guard()
        {
            _guard = true;
            _guardIdle = 0;
        }


        /// <summary>关屏：默认把草稿丢掉（Descartar / 安卓返回 / 出世界）。</summary>
        private static void Close(bool sound = true, bool apply = false)
        {
            if (!_open) return;
            _open = false;
            _where = Where.None;
            _popup = null;
            ReleaseSliders();
            if (apply) ModConfigLoader.Apply();
            else ModConfigLoader.Discard();
            if (sound) SoundEngine.PlaySound(MENU_CLOSE);
        }

        private static bool Modal() => _popup != null;

        private static bool Blocked() => _guard || _dragged || Modal();

        private static bool RowBlocked() => Blocked() || !_pressInList;

        private static void ResetScroll()
        {
            _scroll = 0;
            _momentum = 0;
        }

        private static void ReleaseSliders()
        {
            _slider = null;
            foreach (var st in RowStates.Values)
            {
                st.Drag?.wasDragging = false;
            }
        }


        private static ModConfig View(ModConfigEntry entry) => ModConfigLoader.View(entry);

        private static bool SameNative(Il2CppObjectBase a, Il2CppObjectBase b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            return a.Pointer == b.Pointer;
        }

        private static NullableColor NoColor() => new();
        private static NullableRect NoRect() => new();
        private static NullableMatrix NoMatrix() => new();

        private static int IndexOf(string[] choices, string value)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (string.Equals(choices[i], value, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static SpriteFont TryFont(String_Layout layout)
        {
            if (layout == null) return null;
            try { return layout.GetFont(); }
            catch { return null; }
        }

        private static float TextureHeightSafe(GameTexture2D texture)
        {
            try
            {
                float height = texture.Height;
                if (height > 0) return height;
            }
            catch { }
            try
            {
                PylonTexture.Size(texture, out _, out var height);
                return height > 0 ? height : 32f;
            }
            catch { return 32f; }
        }

        /// <summary>每个标签只报一次，不重复刷屏。</summary>
        private static bool Safe(string label, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                if (Reported.Add(label))
                {
                    _logger?.Error("error in " + label + ": " + ModUI.Flatten(e));
                }
                return false;
            }
        }

        /// <summary>原生 Object 浅拷贝：属性逐个拷会漏掉未暴露的原生字段（画时读空字段段错误），直接调原生 MemberwiseClone。</summary>
        private static T Clone<T>(T source) where T : Il2CppObjectBase, new()
        {
            if (source is Il2CppObjectBase)
            {
                try
                {
                    var result = ((Il2CppSystem.Object)(object)source).MemberwiseClone();
                    if (result is T typed) return typed;
                    var ctor = typeof(T).GetConstructor(new[] { typeof(IntPtr) });
                    if (ctor != null) return (T)ctor.Invoke(new object[] { ((Il2CppObjectBase)result).Pointer });
                }
                catch (Exception e)
                {
                    _logger?.Error("原生 MemberwiseClone 失败（退回属性拷贝）：" + typeof(T).Name + " " + ModUI.Flatten(e));
                }
            }
            return CopyFallback(source);
        }

        /// <summary>原生 MemberwiseClone 不可用时的兜底。</summary>
        private static T CopyFallback<T>(T source) where T : Il2CppObjectBase, new()
        {
            var clone = new T();
            var copy = typeof(T).GetMethod("Copy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, [typeof(T)], null);
            if (copy != null)
            {
                try { copy.Invoke(clone, parameters: [source]); return clone; }
                catch (Exception e)
                {
                    throw new InvalidOperationException($"原生 Copy 失败：{typeof(T).Name}（{e.Message}）", e);
                }
            }
            CopyProperties(source, clone);
            return clone;
        }

        private static readonly Dictionary<Type, PropertyInfo[]> CloneCache = [];

        private static void CopyProperties<T>(T from, T to)
        {
            var type = typeof(T);
            if (!CloneCache.TryGetValue(type, out var properties))
            {
                properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                CloneCache[type] = properties;
            }
            foreach (var property in properties)
            {
                if (!property.CanRead || !property.CanWrite) continue;
                if (property.GetIndexParameters().Length > 0) continue;
                try
                {
                    property.SetValue(to, property.GetValue(from));
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException(
                        $"MemberwiseClone 等价拷贝失败：{type.Name}.{property.Name}（{e.Message}）", e);
                }
            }
        }
    }
}