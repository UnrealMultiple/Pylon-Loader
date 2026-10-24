// 层：框架 / 触屏按钮。
//
// 游戏按"动作号"索引的数组（样式、按钮动作、控制、输入模式）只装游戏自己的动作：
// 这里把它们长到能装下 mod 的动作，并每隔约一秒重新确认一次（游戏换布局/模式时会重建）。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;

using Pylon.ModLoader.UI;

namespace Pylon.ModLoader
{
    /// <summary>触屏按钮的游戏侧钩子。</summary>
    public static class TouchButtonHooks
    {
        /// <summary>安装完成且已套用保存的布局（晚注册的按钮靠它补进布局）。</summary>
        public static bool Applied { get; private set; }

        private static int _frame;
        private static readonly GroupCacheData GroupCache = new() { Frame = -999 };
        private static readonly Dictionary<IntPtr, GridBase> Grids = new();

        // 常量
        private const int Button = 1;          // 编辑器空间类型：按钮
        private const int Group = 2;           // 编辑器空间类型：小组（圆形的多动作）
        private const int GroupCentre = 9;     // 小组中心索引（松开 = 不选）
        private const int Disabled = 0;        // GUIQuickActionButton.InteractionState
        private const int PressedState = 1;
        private const int Normal = 2;

        // 安装状态（Pylon 版：GUIInstance.Active 就绪后由 ModUI 驱动）
        private static Harmony _harmony;
        private static ModLogger _logger;
        private static bool _pending;
        private static bool _installed;

        /// <summary>ModUI.Update 每帧调用：游戏 UI 就绪后挂钩子 + 套用保存的布局（一次）。</summary>
        public static void TryInstallPending()
        {
            if (_installed || !_pending) return;
            if (!UiReady()) return;
            _pending = false;
            try
            {
                Install();
                _installed = true;
                _logger?.Info("触屏按钮已挂（控制编辑器 + 输入 + 布局）");
                ApplySaved();
            }
            catch (Exception e)
            {
                _logger?.Error("触屏按钮挂钩失败：" + ModUI.Flatten(e));
            }
        }

        /// <summary>登记安装（ModUI.EnsureInitialized 时调）。</summary>
        public static void RequestInstall(Harmony harmony, ModLogger logger)
        {
            if (_installed || _pending) return;
            _harmony = harmony;
            _logger = logger;
            _pending = true;
        }

        private static bool UiReady()
        {
            try { return GUIInstance.Active != null; }
            catch { return false; }
        }

        private static void Hook() { }

        internal static void Install()
        {

            // 控制器更新：长数组 + 小组里松在一个 mod 动作上 = 按它
            Patch("ControllerUpdate", typeof(GUIVirtualInputController), "Update", 1, false, true);
            // 控制器画：保证数组
            Patch("ControllerDraw", typeof(GUIVirtualInputController), "Draw", 0, false, true);
            // 触点落在我们的按钮上
            Patch("ControllerTouchDown", typeof(GUIVirtualInputController), "HandleTouchCursorDown", 3, false, true);
            // 隐藏的按钮不显示
            Patch("ControllerVisible", typeof(GUIVirtualInputController), "DetermineButtonVisible", 3, false, true);
            // 快动作按钮画我们的图标
            Patch("QuickDraw", typeof(GUIQuickActionButton), "Draw", 6, true, true);
            // 快动作图标的取帧
            Patch("QuickIcon", typeof(GUIQuickActionButton), "GetIconFrame", 3, true, true);
            // 编辑器里的动作名
            Patch("EditName", typeof(GUIVirtualInputControllerEdit), "GetControlName", 1, false, true);
            // 编辑器里的动作图标
            Patch("EditIcon", typeof(GUIVirtualInputControllerEdit), "GetControlTextureFrame", 3, false, true);
            // 编辑器画：保证列表
            Patch("EditDraw", typeof(GUIVirtualInputControllerEdit), "Draw", 0, false, true);
            // 布局保存：mod 动作不进游戏文件
            Patch("StylesSave", typeof(VirtualControlsStyles_Layout), "SaveConfiguration", 1, false, true);
            // 布局加载：套回保存的位置
            Patch("StylesLoad", typeof(VirtualControlsStyles_Layout), "LoadConfiguration", 1, false, true);
            // 分享码不带 mod 动作
            Patch("StylesShare", typeof(VirtualControlsStyles_Layout), "GetShareCode", 1, true, true);
        }

        // ---- 补丁注册 ----

        private static MethodInfo Find(Type type, string name, int paramCount, bool isStatic)
        {
            foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Static | BindingFlags.Instance))
            {
                if (candidate.Name != name) continue;
                if (candidate.IsStatic != isStatic) continue;
                if (candidate.GetParameters().Length != paramCount) continue;
                return candidate;
            }
            return null;
        }

        private static void Patch(string label, Type type, string name, int paramCount, bool isStatic, bool unused)
        {
            var target = Find(type, name, paramCount, isStatic);
            if (target == null)
            {
                _logger?.Error($"找不到 {type.Name}.{name}（{paramCount} 参）");
                return;
            }
            try
            {
                var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                var prefix = typeof(TouchButtonHooks).GetMethod(label + "Prefix", flags);
                var postfix = typeof(TouchButtonHooks).GetMethod(label + "Postfix", flags);
                if (prefix == null && postfix == null)
                {
                    _logger?.Error($"触屏按钮缺补丁方法 {label}（Prefix/Postfix 都没有）");
                    return;
                }
                _harmony.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(postfix) : null);
                _logger?.Info($"触屏按钮已挂：{type.Name}.{name}");
            }
            catch (Exception e)
            {
                _logger?.Error($"挂 {type.Name}.{name} 失败：" + ModUI.Flatten(e));
            }
        }


        // ---- 控制器 ----

        private static void ControllerUpdatePostfix(GUIVirtualInputController __instance, float __0)
        {
            _frame++;
            Safe("TouchButtons: arrays", () =>
            {
                EnsureController(__instance);
                Ensure(false);
            });
            var groups = Safe(() => GroupSlots()) ?? new List<(string id, object groups)>();
            var before = new List<int>();
            if (groups.Count > 0)
            {
                foreach (var g in groups) before.Add(GroupIndex(__instance, g.id));
            }
            if (before.Count == 0) return;

            // 小组松在一个 mod 动作上：紧的索引回到 -1 就按一下
            for (int i = 0; i < groups.Count; i++)
            {
                var was = before[i];
                var g = groups[i];
                if (was < 0 || was == GroupCentre || was >= GroupActionsLength(g.groups)) continue;
                if (GroupIndex(__instance, g.id) != -1) continue;
                var entry = TouchButtonLoader.ByAction(GroupActionAt(g.groups, was));
                if (entry != null) TouchButtonLoader.Press(entry);
            }
        }

        private static void ControllerDrawPostfix(GUIVirtualInputController __instance)
        {
            Safe("TouchButtons: arrays", () => EnsureController(__instance));
        }

        private static bool ControllerTouchDownPrefix(GUIVirtualInputController __instance, int __0, Vector2 __1, object __2)
        {
            var at = Safe(() => SlotAt(__0));
            var entry = at != null && at.type == Button ? TouchButtonLoader.ByAction(SafeAction2(at.binding)) : null;
            if (entry == null) return true; // 原逻辑

            Safe("TouchButtons: toque", () =>
            {
                EnsureController(__instance);
                var control = TouchButtonLoader.ControlOf(entry);
                var first = !SafePressedAt(__instance, control);
                SetPressedAt(__instance, control, true);
                if (first) TouchButtonLoader.Press(entry);
            });
            // 让原逻辑也跑（触摸点绑到按钮上，长按拖动判定才有依据）；风险：原方法对未知动作号可能查表越界崩（0x9c），若崩就退回 return false。
            return true;
        }

        private static bool ControllerVisiblePrefix(GUIVirtualInputController __instance, int __0, int __1, object __2, ref bool __result)
        {
            var entry = TouchButtonLoader.ByAction(__1);
            if (entry != null && !TouchButtonLoader.Visible(entry))
            {
                __result = false;
                return false;
            }
            return true;
        }

        // ---- 快动作绘制 / 取帧 ----

        // 注意：mod 快动作绝不能带自己的动作号走原逻辑（游戏查图标表会越界崩）。
        // 前缀里没法换参，改为：没图标就不画（按钮只剩底衬，安全）。
        private static bool QuickDrawPrefix(int __0, object __1, bool __2, ref float __3, bool __4, bool __5)
        {
            var entry = TouchButtonLoader.ByQuick(__0);
            if (entry == null || __1 == null) return true;   // 游戏自己的快动作（或布局还没给）→ 原逻辑
            // 参数序：__4=ignoreChecks、__5=ignoreIcon；用错的话游玩时会把图标置 null（"绑定后看不见按钮"的根因）。
            var texture = !__5 ? TouchButtonLoader.Texture(entry) : null;   // ignoreIcon
            if (texture == null) return false;               // 忽略图标时不画也不走原逻辑
            var state = __2 ? PressedState : TouchButtonLoader.Enabled(entry) ? Normal : Disabled;
            var scale = __3;   // ref 参数不能进 lambda，先拷本地
            Safe("TouchButtons: desenhar", () =>
            {
                GUIQuickActionButton.Draw((QuickActionButton_Layout)__1, texture, (GUIQuickActionButton.InteractionState)state, ref scale);
            });
            __3 = scale;
            return false; // 用我们的画法，跳过原逻辑
        }

        // 注意：mod 动作**必须**在这里截住——原方法拿 mod 动作号查游戏图标表会越界崩（0x9c）。
        private static bool QuickIconPrefix(int __0, ref GameTexture2D __1, ref Rectangle __2)
        {
            var entry = TouchButtonLoader.ByQuick(__0);
            if (entry == null) return true;   // 游戏自己的快动作 → 原逻辑
            var texture = TouchButtonLoader.Texture(entry);
            __1 = texture;                    // 允许 null（没图标的 mod 按钮）
            __2 = texture != null ? new Rectangle(0, 0, texture.Width, texture.Height) : default;
            return false;
        }

        // ---- 编辑器 ----

        private static bool EditNamePrefix(int __0, ref string __result)
        {
            var entry = TouchButtonLoader.ByAction(__0);
            if (entry == null) return true;
            __result = TouchButtonLoader.Label(entry);
            return false;
        }

        // 注意：mod 动作**必须**在这里截住——原方法拿 mod 动作号查游戏图标表会越界崩（0x9c）。
        private static bool EditIconPrefix(int __0, ref GameTexture2D __1, ref Rectangle __2)
        {
            var entry = TouchButtonLoader.ByAction(__0);
            if (entry == null) return true;   // 游戏自己的动作 → 原逻辑
            var texture = TouchButtonLoader.Texture(entry);
            __1 = texture;                    // 允许 null（没图标的 mod 按钮）
            __2 = texture != null ? new Rectangle(0, 0, texture.Width, texture.Height) : default;
            return false;
        }

        private static void EditDrawPostfix(GUIVirtualInputControllerEdit __instance)
        {
            Safe("TouchButtons: editor", () => Ensure(false));
        }

        // ---- 样式：保存/加载/分享 ----

        private static void StylesSavePrefix(VirtualControlsStyles_Layout __instance, object __0)
        {
            Safe("TouchButtons: guardar o layout", () =>
            {
                if (TouchButtonLoader.Count > 0)
                {
                    TouchButtonLayouts.Capture(__instance);
                    TouchButtonLayouts.__BeginStrip(TouchButtonLayouts.Profiles(__instance));
                }
            });
        }

        private static void StylesSavePostfix(VirtualControlsStyles_Layout __instance, object __0)
        {
            TouchButtonLayouts.__EndStrip();
        }

        private static void StylesLoadPostfix(VirtualControlsStyles_Layout __instance, object __0)
        {
            Safe("TouchButtons: layout salvo", () =>
            {
                Ensure(true);
                TouchButtonLayouts.Apply(__instance);
            });
        }

        private static bool StylesSharePrefix(object __0)
        {
            if (TouchButtonLoader.Count > 0)
            {
                var profile = __0 as VirtualControlsProfile_Layout;
                if (profile != null) TouchButtonLayouts.__BeginStrip(new[] { profile });
            }
            return true;
        }

        private static void StylesSharePostfix(object __0)
        {
            TouchButtonLayouts.__EndStrip();
        }

        // ---- 数组 ----

        /// <summary>内容就绪后套用保存的布局。</summary>
        public static void ApplySaved()
        {
            Applied = true;
            Safe("TouchButtons: layout salvo", () =>
            {
                Ensure(true);
                TouchButtonLayouts.Apply(TouchButtonLoader.Styles());
            });
        }

        private static void Ensure(bool force)
        {
            if (TouchButtonLoader.Count == 0) return;
            if (!force && (_frame % 60) != 0) return;

            Safe("TouchButtons: estáticos", () => { Statics(); });
            Safe("TouchButtons: modo de entrada", () => { InputState(); });
            Safe("TouchButtons: lista do editor", () => { Options(); });
            Safe("TouchButtons: controle", () => { EnsureController(TouchButtonLoader.Controller()); });
        }

        private static void EnsureController(GUIVirtualInputController controller)
        {
            if (controller == null || TouchButtonLoader.Count == 0) return;
            var size = TouchButtonLoader.ControlOf(TouchButtonLoader.All[TouchButtonLoader.Count - 1]) + 1;
            var pressed = Safe(() => controller.Pressed);
            if (pressed != null && pressed.Length >= size) return;
            controller.Pressed = Sized(pressed, size, _ => false);
            controller.WasPressed = Sized(Safe(() => controller.WasPressed), size, _ => false);
            controller.Scale = Sized(Safe(() => controller.Scale), size, _ => 0f);
        }

                private static void Statics()
        {
            Put(VirtualControlsButtonConfiguration_Layout.ButtonStyles,
                _ => default, _ => default,
                v => VirtualControlsButtonConfiguration_Layout.ButtonStyles = v, "ButtonStyles");
            Put(GUIVirtualInputController.QuickActionControlFromLayoutControl,
                _ => (QuickActionButton_Layout.QuickActionControl)TouchButtonLoader.QuickUnboundValue(),
                e => (QuickActionButton_Layout.QuickActionControl)TouchButtonLoader.QuickOf(e),
                v => GUIVirtualInputController.QuickActionControlFromLayoutControl = v, "QuickActionControlFromLayoutControl");
            Put(GUIVirtualInputController.ButtonControlFromLayoutControl,
                _ => default,
                e => (GUIVirtualInputController.ControlType)TouchButtonLoader.ControlOf(e),
                v => GUIVirtualInputController.ButtonControlFromLayoutControl = v, "ButtonControlFromLayoutControl");
            Put(VirtualControllerInputState.DefaultMode,
                i => (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)i,
                e => (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)TouchButtonLoader.ActionOf(e),
                v => VirtualControllerInputState.DefaultMode = v, "DefaultMode");
            Put(VirtualControllerInputState.RemappedMode,
                i => (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)i,
                e => (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)TouchButtonLoader.ActionOf(e),
                v => VirtualControllerInputState.RemappedMode = v, "RemappedMode");
        }

        private static void InputState()
        {
            var state = Safe(() => GUIInstance.Active != null ? GUIInstance.Active.VirtualControllerInputState : null);
            if (state == null) return;
            var last = TouchButtonLoader.LastAction;
            var current = Safe(() => state.RemappedBindings);
            if (current != null && current.Length > last && (int)current[last] == last) return;

            Safe("TouchButtons: RefreshAlternativeModeMappings", () => state.RefreshAlternativeModeMappings());
            var after = Safe(() => state.RemappedBindings);
            if (after == null || after.Length <= last || (int)after[last] != last)
            {
                var mapped = Mapped(after, i => (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)i, e => (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)TouchButtonLoader.ActionOf(e));
                if (mapped != null) state.RemappedBindings = mapped;
            }
        }

        private static void Options()
        {
            var config = Safe(() => VirtualInputControllerControlConfiguration_Layout.Instance);
            var list = config != null ? Safe(() => config.ButtonControls) : null;
            if (list == null) return;

            var have = new HashSet<int>();
            for (int i = 0; i < list.Length; i++) have.Add((int)list[i]);
            var missing = new List<TouchButtonEntry>();
            foreach (var e in TouchButtonLoader.All) if (!have.Contains(TouchButtonLoader.ActionOf(e))) missing.Add(e);
            if (missing.Count > 0)
            {
                // 注意：不能 new Il2CppStructArray(len) 后逐元素写（interop setref 在 ARM64 上崩），用托管数组 + 隐式转换。
                var managed = new VirtualControlsHardwareConfigurationMapping_Layout.ContolType[list.Length + missing.Count];
                for (int i = 0; i < list.Length; i++) managed[i] = list[i];
                for (int i = 0; i < missing.Count; i++) managed[list.Length + i] = (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)TouchButtonLoader.ActionOf(missing[i]);
                config.ButtonControls = managed;
                list = managed;
            }
            var grid = Safe(() => config.PulldownOptionGrid);
            if (grid != null) Fit(grid, list.Length);
        }

        // 拉下列表的格子要能装下全部动作
        private static void Fit(object grid, int count)
        {
            var key = PointerOf(grid);
            if (!Grids.TryGetValue(key, out var baseData))
            {
                baseData = new GridBase { Items = ItemCount(grid), Line = Math.Max(1, ItemLineCount(grid)) };
                Grids[key] = baseData;
            }
            var items = baseData.Items;
            var line = baseData.Line;
            if (count > items)
            {
                if (Horizontal(grid))
                {
                    items = (int)Math.Ceiling(count / (double)line) * line;
                }
                else
                {
                    var columns = Math.Max(1, (int)Math.Ceiling(items / (double)line));
                    line = (int)Math.Ceiling(count / (double)columns);
                    items = line * columns;
                }
            }
            if (ItemLineCount(grid) != line) SetItemLineCount(grid, line);
            if (ItemCount(grid) != items) SetItemCount(grid, items);
        }

        // ---- 数组助手 ----

        private static Il2CppStructArray<T> Sized<T>(Il2CppStructArray<T> array, int size, Func<int, T> fill) where T : unmanaged
        {
            if (array == null) return null;
            var old = array.Length;
            if (old >= size) return array;
            var outArr = new T[size];
            Array.Copy(array, outArr, old);
            for (int i = old; i < size; i++) outArr[i] = fill(i);
            return outArr;
        }

        private static Il2CppStructArray<T> Mapped<T>(Il2CppStructArray<T> array, Func<int, T> fill, Func<TouchButtonEntry, T> value) where T : unmanaged
        {
            var outArr = Sized(array, TouchButtonLoader.LastAction + 1, fill);
            if (outArr == null) return null;
            foreach (var e in TouchButtonLoader.All)
            {
                var i = TouchButtonLoader.ActionOf(e);
                var v = value(e);
                if (!Equals(outArr[i], v)) outArr[i] = v;
            }
            return outArr;
        }

        private static void Put<T>(Il2CppStructArray<T> field, Func<int, T> fill, Func<TouchButtonEntry, T> value, Action<Il2CppStructArray<T>> write, string name) where T : unmanaged
        {
            if (field == null) return;
            var outArr = Mapped(field, fill, value);
            if (outArr != null && !ReferenceEquals(outArr, field)) write(outArr);
        }

        // ---- 空间 ----

        private static SlotData SlotAt(int slot)
        {
            var styles = TouchButtonLoader.Styles();
            if (styles == null) return null;
            var profile = Safe(() => styles.ActiveProfile);
            if (profile == null) return null;
            var controls = Safe(() => profile.Controls);
            var hardware = Safe(() => profile.HardwareConfig);
            var binding = controls != null && slot < controls.Length ? controls[slot] : null;
            var slots = hardware != null ? Safe(() => hardware.Slots) : null;
            var hw = slots != null && slot < slots.Length ? slots[slot] : null;
            if (binding == null || hw == null) return null;
            return new SlotData { binding = binding, type = ControlTypeOf(hw) };
        }

        private static List<(string id, object groups)> GroupSlots()
        {
            if (_frame - GroupCache.Frame < 30) return GroupCache.List;

            var list = new List<(string id, object groups)>();
            var styles = TouchButtonLoader.Styles();
            var profile = styles != null ? Safe(() => styles.ActiveProfile) : null;
            var controls = profile != null ? Safe(() => profile.Controls) : null;
            var hardware = profile != null ? Safe(() => profile.HardwareConfig) : null;
            var slots = hardware != null ? Safe(() => hardware.Slots) : null;
            var n = controls != null && slots != null ? Math.Min(controls.Length, slots.Length) : 0;
            for (int i = 0; i < n; i++)
            {
                var hw = slots[i];
                var binding = controls[i];
                if (hw == null || binding == null || ControlTypeOf(hw) != Group) continue;
                var groups = Safe(() => binding.GroupActions);
                if (groups == null) continue;
                var ours = false;
                for (int j = 0; j < groups.Length; j++) if (TouchButtonLoader.ByAction((int)groups[j]) != null) ours = true;
                if (ours) list.Add((SlotIdOf(hw), groups));
            }
            GroupCache.Frame = _frame;
            GroupCache.List = list;
            return list;
        }

        private static int GroupIndex(GUIVirtualInputController controller, string id)
        {
            var states = Safe(() => controller.GroupStates);
            if (states == null || !states.ContainsKey(id)) return -1;
            try { return states[id].pressedIndex; }
            catch { return -1; }
        }

        // ---- 反射/读取小工具（interop 动态成员走这里，避免编译期硬依赖不确定的签名）----

        internal static object Val(object obj, string member)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            var prop = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null) return prop.GetValue(obj);
            var field = type.GetField(member, BindingFlags.Public | BindingFlags.Instance);
            return field?.GetValue(obj);
        }

        internal static object ValStatic(Type type, string member)
        {
            var prop = type.GetProperty(member, BindingFlags.Public | BindingFlags.Static);
            if (prop != null) return prop.GetValue(null);
            var field = type.GetField(member, BindingFlags.Public | BindingFlags.Static);
            return field?.GetValue(null);
        }

        private static int Safe( Func<int> fn)
        {
            try { return fn(); }
            catch { return 0; }
        }

        private static T Safe<T>(Func<T> fn)
        {
            try { return fn(); }
            catch { return default; }
        }

        private static void Safe(string label, Action action)
        {
            try { action(); }
            catch (Exception e) { TouchButtonLoader.Log(label + "：错误：" + ModUI.Flatten(e)); }
        }

        // 条目内部集合的转发（避免把 Loader 的私有字段全暴露）：
        private static int SafeAction2(VirtualControlsBinding_Layout binding)
        {
            try { return (int)binding.Action; }
            catch { return -1; }
        }

        private static int ControlTypeOf(VirtualControlsHardwareConfigurationMapping_Layout slot)
        {
            try { return (int)Val(slot, "ControlType"); }
            catch { return 0; }
        }

        private static string SlotIdOf(VirtualControlsHardwareConfigurationMapping_Layout slot)
        {
            try
            {
                var value = Val(slot, "SlotId");
                return value?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private static int GroupActionsLength(object groups) => groups is Il2CppStructArray<VirtualControlsHardwareConfigurationMapping_Layout.ContolType> arr ? arr.Length : 0;

        private static int GroupActionAt(object groups, int j)
        {
            try { return (int)((Il2CppStructArray<VirtualControlsHardwareConfigurationMapping_Layout.ContolType>)groups)[j]; }
            catch { return -1; }
        }

        private static bool SafePressedAt(GUIVirtualInputController controller, int control)
        {
            try { return control < controller.Pressed.Length && controller.Pressed[control]; }
            catch { return false; }
        }

        private static void SetPressedAt(GUIVirtualInputController controller, int control, bool value)
        {
            try { controller.Pressed[control] = value; }
            catch { }
        }

        private static int ItemCount(object grid) => Convert.ToInt32(Val(grid, "ItemCount") ?? 0);
        private static int ItemLineCount(object grid) => Convert.ToInt32(Val(grid, "ItemLineCount") ?? 0);
        private static bool Horizontal(object grid) => Convert.ToBoolean(Val(grid, "Horizontal") ?? false);
        private static void SetItemCount(object grid, int value) => SetVal(grid, "ItemCount", value);
        private static void SetItemLineCount(object grid, int value) => SetVal(grid, "ItemLineCount", value);

        internal static void SetVal(object obj, string member, object value)
        {
            if (obj == null) return;
            var type = obj.GetType();
            var prop = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite) { prop.SetValue(obj, value); return; }
            var field = type.GetField(member, BindingFlags.Public | BindingFlags.Instance);
            field?.SetValue(obj, value);
        }

        private static IntPtr PointerOf(object obj)
        {
            if (obj is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase il2cpp) return new IntPtr((long)il2cpp.Pointer);
            return IntPtr.Zero;
        }

        private sealed class SlotData
        {
            public VirtualControlsBinding_Layout binding;
            public int type;
        }

        private sealed class GroupCacheData
        {
            public int Frame;
            public List<(string id, object groups)> List = new();
        }

        private sealed class GridBase
        {
            public int Items;
            public int Line;
        }
    }
}