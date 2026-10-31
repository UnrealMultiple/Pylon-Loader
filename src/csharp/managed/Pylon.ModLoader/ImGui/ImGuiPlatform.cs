using System;

namespace Pylon.ModLoader.UI
{
    /// <summary>
    /// 覆盖层的安卓侧搭建（反射链全在这里，内核只负责 Surface → ANativeWindow 与 Vulkan）：
    /// Unity 的 Activity → WindowManager 上挂一个全屏 SurfaceView → 从它的 SurfaceHolder 上
    /// 读 mSurface 字段拿到 Surface → 交给内核。
    ///
    /// 两个要点：
    /// · 走 WindowManager.addView 而不是 decorView.addView：后者只能在 Android 的 main 线程动，
    ///   而 Unity 的渲染线程叫 UnityMain，直接改会抛 CalledFromWrongThreadException。
    /// · Surface 用**字段**读，不用 SurfaceHolder.getSurface()：那个方法的 JNI 查找在本机一直失败；
    ///   读 mSurface 与原 C++ 实现（NativeWindowProvider 读 ViewRootImpl.mSurface）是同一手法。
    /// </summary>
    internal static class ImGuiPlatform
    {
        private const int TypeApplication    = 2;
        private const int FormatTranslucent  = -3;
        private const int MatchParent        = -1;
        private const int GravityLeftTop     = 3 | 48;
        private const int FlagNotFocusable   = 0x00000008;
        private const int FlagNotTouchable   = 0x00000010;
        private const int FlagNotTouchModal  = 0x00000020;
        private const int FlagLayoutInScreen = 0x00000100;
        private const int FlagFullscreen     = 0x00000400;
        private const int FlagHardwareAccel  = 0x01000000;
        private const int OverlayFlags =
            FlagNotFocusable | FlagNotTouchable | FlagNotTouchModal |
            FlagLayoutInScreen | FlagFullscreen | FlagHardwareAccel;

        private static JniObject _windowManager;
        private static JniObject _view;
        private static JniObject _holder;
        private static bool _added;
        private static bool _attached;
        private static int _attempts;
        private static int _probeFrames;
        private static int _fieldProbes;

        internal static bool Attached => _attached;

        /// <summary>建 View 并挂到窗口（幂等）。返回是否可用。</summary>
        internal static bool EnsureView()
        {
            if (_added) return true;

            using var activity = FindActivity();
            if (activity == null)
            {
                ModUI.Logger?.Warn("找不到 Activity，覆盖层不启动");
                return false;
            }

            using (var serviceName = Jni.NewString("window"))
            {
                _windowManager = activity.CallObject("getSystemService",
                    "(Ljava/lang/String;)Ljava/lang/Object;", serviceName.AsArg());
            }
            if (_windowManager == null)
            {
                ModUI.Logger?.Warn("取 WindowManager 失败：" + Jni.LastError);
                return false;
            }

            _view = JniObject.New("android/view/SurfaceView",
                "(Landroid/content/Context;)V", activity.AsArg());
            if (_view == null)
            {
                ModUI.Logger?.Warn("建 SurfaceView 失败：" + Jni.LastError);
                return false;
            }
            _view.Call("setZOrderOnTop", "(Z)V", true);
            _holder = _view.CallObject("getHolder", "()Landroid/view/SurfaceHolder;");
            // 半透明像素格式，否则这层 SurfaceView 会把整块游戏画面遮成黑屏。
            _holder?.Call("setFormat", "(I)V", FormatTranslucent);

            using var lp = BuildLayoutParams();
            if (lp == null) return false;

            Jni.ClearError();
            _windowManager.Call("addView",
                "(Landroid/view/View;Landroid/view/ViewGroup$LayoutParams;)V",
                _view.AsArg(), lp.AsArg());
            if (Jni.LastError.Length != 0)
            {
                ModUI.Logger?.Warn("挂 SurfaceView 失败：" + Jni.LastError);
                return false;
            }

            _added = true;
            ModUI.Logger?.Info("覆盖层 SurfaceView 已挂到窗口");
            return true;
        }

        /// <summary>每帧轮询：拿到 Surface 后交给内核。返回是否已接管。</summary>
        internal static bool TryAttachSurface()
        {
            if (_attached) return true;
            if (!_added || _holder == null) return false;

            // 字段名按对象实际实现的类来找：先 SurfaceView 自身，再它的 SurfaceHolder。
            // 签名是 android.view.Surface —— 这套 ROM 里 Surface 就在这个包下（原 C++ 用的也是它）。
            var surface = _view.GetFieldObject("mSurface", "Landroid/view/Surface;")
                          ?? _holder.GetFieldObject("mSurface", "Landroid/view/Surface;");
            if (surface == null)
            {
                if (++_fieldProbes == 1) ModUI.Logger?.Warn("取 mSurface 失败：" + Jni.LastError);
                return false;
            }

            if (ImGuiNativeApi.AttachSurface(surface.Handle) != 0)
            {
                _attached = true;
                ModUI.Logger?.Info("覆盖层 Surface 已交给渲染器");
            }
            return _attached;
        }

        internal static void LogPendingState()
        {
            if (++_probeFrames != 1 && _probeFrames % 300 != 0) return;
            var why = !_added ? "视图未挂载" : _holder == null ? "取不到 SurfaceHolder" : "Surface 尚未就绪";
            ModUI.Logger?.Warn($"覆盖层未接管（第 {_probeFrames} 帧）：{why}；{Jni.LastError}");
        }

        internal static void Detach()
        {
            if (_attached)
            {
                try { ImGuiNativeApi.DetachSurface(); } catch { }
                _attached = false;
            }
            if (_added && _windowManager != null && _view != null)
            {
                try { _windowManager.Call("removeView", "(Landroid/view/View;)V", _view.AsArg()); }
                catch { }
                _added = false;
            }
            _holder?.Dispose(); _holder = null;
            _view?.Dispose(); _view = null;
            _windowManager?.Dispose(); _windowManager = null;
        }

        // ------------------------------------------------------------------

        /// <summary>不设 token：交给 WindowManagerImpl 用 Activity 的默认 token，自己填容易拿到失效的那个。</summary>
        private static JniObject BuildLayoutParams()
        {
            var lp = JniObject.New("android/view/WindowManager$LayoutParams", "()V");
            if (lp == null) return null;

            lp.SetIntField("type", "I", TypeApplication);
            lp.SetIntField("flags", "I", OverlayFlags);
            lp.SetIntField("width", "I", MatchParent);
            lp.SetIntField("height", "I", MatchParent);
            lp.SetIntField("format", "I", FormatTranslucent);
            lp.SetIntField("gravity", "I", GravityLeftTop);
            return lp;
        }

        private static JniObject FindActivity()
        {
            var unity = JniClass.Find("com/unity3d/player/UnityPlayer");
            if (unity != null)
            {
                using (unity)
                {
                    var activity = unity.GetStaticObject("currentActivity", "Landroid/app/Activity;");
                    if (activity != null) return activity;
                }
            }
            return FindActivityViaThread();
        }

        private static JniObject FindActivityViaThread()
        {
            var at = JniClass.Find("android/app/ActivityThread");
            if (at == null) return null;

            JniObject thread = null, activities = null;
            try
            {
                thread = at.CallStaticObject("currentActivityThread", "()Landroid/app/ActivityThread;");
                if (thread == null) return null;
                activities = thread.GetFieldObject("mActivities", "Landroid/util/ArrayMap;");
                if (activities == null) return null;

                int count = activities.CallInt("size", "()I");
                for (int i = 0; i < count; i++)
                {
                    using var record = activities.CallObject("valueAt", "(I)Ljava/lang/Object;", i);
                    var activity = record?.GetFieldObject("activity", "Landroid/app/Activity;");
                    if (activity == null) continue;
                    if (activity.CallBool("isFinishing", "()Z")) { activity.Dispose(); continue; }
                    return activity;
                }
                return null;
            }
            finally
            {
                activities?.Dispose();
                thread?.Dispose();
                at.Dispose();
            }
        }
    }
}
