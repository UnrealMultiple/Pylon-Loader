namespace Pylon.ModLoader.UI;

/// <summary>
/// 覆盖层的 Android 侧：WindowManager 上挂 SurfaceView，把 Surface 交给内核。
/// Surface 会在切后台时销毁、回前台时重建，所以必须定期复验 isValid。
/// </summary>
internal static class ImGuiPlatform
{
    private const int TypeApplication = 2;
    private const int FormatTranslucent = -3;
    private const int MatchParent = -1;
    private const int GravityLeftTop = 3 | 48;
    private const int FlagNotFocusable = 0x00000008;
    private const int FlagNotTouchable = 0x00000010;
    private const int FlagNotTouchModal = 0x00000020;
    private const int FlagLayoutInScreen = 0x00000100;
    private const int FlagFullscreen = 0x00000400;
    private const int FlagHardwareAccel = 0x01000000;
    private const int OverlayFlags =
        FlagNotFocusable | FlagNotTouchable | FlagNotTouchModal |
        FlagLayoutInScreen | FlagFullscreen | FlagHardwareAccel;

    private const int SurfaceHealthIntervalFrames = 10;

    private static JniObject? _windowManager;
    private static JniObject? _view;
    private static JniObject? _holder;
    private static bool _added;
    private static bool _attached;
    private static bool _pauseHooked;
    private static int _healthCountdown;
    private static bool _missingSurfaceLogged;
    private static string? _lastPendingReason;

    internal static bool Attached => _attached;

    /// <summary>建 View 并挂到窗口（幂等）。</summary>
    internal static bool EnsureView()
    {
        InstallPauseHook();
        if (_added) return true;

        using var activity = FindActivity();
        if (activity == null)
        {
            ModUI.Logger?.Warn("找不到 Activity，覆盖层不启动");
            return false;
        }

        JniObject? windowManager;
        using (var serviceName = Jni.NewString("window"))
        {
            windowManager = activity.CallObject("getSystemService",
                "(Ljava/lang/String;)Ljava/lang/Object;", serviceName.AsArg());
        }
        if (windowManager == null)
        {
            ModUI.Logger?.Warn("取 WindowManager 失败：" + Jni.LastError);
            return false;
        }
        _windowManager = windowManager;

        // WindowManager.addView 可以在 UnityMain 上调；decorView.addView 不行。
        var view = JniObject.New("android/view/SurfaceView",
            "(Landroid/content/Context;)V", activity.AsArg());
        if (view == null)
        {
            ModUI.Logger?.Warn("建 SurfaceView 失败：" + Jni.LastError);
            ReleaseView();
            return false;
        }
        _view = view;
        view.Call("setZOrderOnTop", "(Z)V", true);
        _holder = view.CallObject("getHolder", "()Landroid/view/SurfaceHolder;");
        _holder?.Call("setFormat", "(I)V", FormatTranslucent);

        using var layoutParams = BuildLayoutParams();
        if (layoutParams == null)
        {
            ReleaseView();
            return false;
        }

        Jni.ClearError();
        windowManager.Call("addView",
            "(Landroid/view/View;Landroid/view/ViewGroup$LayoutParams;)V",
            view.AsArg(), layoutParams.AsArg());
        if (Jni.LastError.Length != 0)
        {
            ModUI.Logger?.Warn("挂 SurfaceView 失败：" + Jni.LastError);
            ReleaseView();
            return false;
        }

        _added = true;
        ModUI.Logger?.Info("覆盖层 SurfaceView 已挂到窗口");
        return true;
    }

    /// <summary>每帧轮询有效 Surface 并交给内核。</summary>
    internal static bool TryAttachSurface()
    {
        if (!_added || _holder == null || _view == null) return false;

        if (_attached && ++_healthCountdown < SurfaceHealthIntervalFrames) return true;
        _healthCountdown = 0;

        var surface = ReadSurface();
        if (surface == null)
        {
            if (!_missingSurfaceLogged)
            {
                _missingSurfaceLogged = true;
                ModUI.Logger?.Warn("取 mSurface 失败：" + Jni.LastError);
            }
            DetachNative();
            return false;
        }

        using (surface)
        {
            if (!IsSurfaceValid(surface) || !AttachSurface(surface))
            {
                DetachNative();
                return false;
            }
        }

        return true;
    }

    private static JniObject? ReadSurface()
    {
        // 不用 SurfaceHolder.getSurface()：本机 JNI 查找失败，读字段与原 C++ 实现一致。
        return _view?.GetFieldObject("mSurface", "Landroid/view/Surface;")
               ?? _holder?.GetFieldObject("mSurface", "Landroid/view/Surface;");
    }

    private static bool IsSurfaceValid(JniObject surface)
    {
        try { return surface.CallBool("isValid", "()Z"); }
        catch (Exception e)
        {
            ModUI.Logger?.Warn("Surface.isValid 调用失败：" + e.Message);
            return false;
        }
    }

    private static bool AttachSurface(JniObject surface)
    {
        bool attached;
        try
        {
            attached = ImGuiNativeApi.AttachSurface(surface.Handle) != 0;
        }
        catch (Exception e)
        {
            ModUI.Logger?.Warn("覆盖层绑定表面失败：" + e.Message);
            return false;
        }

        if (!attached) return false;

        if (!_attached)
        {
            _attached = true;
            _missingSurfaceLogged = false;
            _lastPendingReason = null;
            ModUI.Logger?.Info("覆盖层 Surface 已交给渲染器");
        }
        return true;
    }

    /// <summary>只释放内核表面，不摘 SurfaceView。</summary>
    private static void DetachNative()
    {
        if (!_attached) return;
        _attached = false;
        try { ImGuiNativeApi.DetachSurface(); }
        catch (Exception e) { ModUI.Logger?.Warn("覆盖层释放表面失败：" + e.Message); }
    }

    private static void InstallPauseHook()
    {
        if (_pauseHooked) return;
        _pauseHooked = true;
        try
        {
            On.XNAUnityRunner.OnApplicationPause += OnApplicationPausePostfix;
            ModUI.Logger?.Info("覆盖层：后台暂停钩子已挂");
        }
        catch (Exception e)
        {
            ModUI.Logger?.Warn("覆盖层后台钩子失败：" + e.Message);
        }
    }

    private static void OnApplicationPausePostfix(On.XNAUnityRunner.orig_OnApplicationPause orig,
                                                  XNAUnityRunner self, bool paused)
    {
        orig(self, paused);
        if (paused && _attached) DetachNative();
    }

    internal static void LogPendingState()
    {
        var why = !_added ? "视图未挂载"
            : _holder == null ? "取不到 SurfaceHolder"
            : "Surface 尚未就绪或绑定失败";
        if (why == _lastPendingReason) return;
        _lastPendingReason = why;
        ModUI.Logger?.Warn($"覆盖层未接管：{why}；{Jni.LastError}");
    }

    internal static void Detach()
    {
        DetachNative();
        if (_added && _windowManager != null && _view != null)
        {
            try { _windowManager.Call("removeView", "(Landroid/view/View;)V", _view.AsArg()); }
            catch (Exception) { }   // 摘视图失败不影响后续释放
        }
        ReleaseView();
        _healthCountdown = 0;
        _lastPendingReason = null;
    }

    private static void ReleaseView()
    {
        _holder?.Dispose();
        _holder = null;
        _view?.Dispose();
        _view = null;
        _windowManager?.Dispose();
        _windowManager = null;
        _added = false;
        _attached = false;
        _missingSurfaceLogged = false;
    }

    private static JniObject? BuildLayoutParams()
    {
        // 不设 token：交给 WindowManagerImpl 用 Activity 的默认 token。
        var layoutParams = JniObject.New("android/view/WindowManager$LayoutParams", "()V");
        if (layoutParams == null) return null;

        layoutParams.SetIntField("type", "I", TypeApplication);
        layoutParams.SetIntField("flags", "I", OverlayFlags);
        layoutParams.SetIntField("width", "I", MatchParent);
        layoutParams.SetIntField("height", "I", MatchParent);
        layoutParams.SetIntField("format", "I", FormatTranslucent);
        layoutParams.SetIntField("gravity", "I", GravityLeftTop);
        return layoutParams;
    }

    private static JniObject? FindActivity()
    {
        using var unity = JniClass.Find("com/unity3d/player/UnityPlayer");
        if (unity != null)
        {
            var activity = unity.GetStaticObject("currentActivity", "Landroid/app/Activity;");
            if (activity != null) return activity;
        }
        return FindActivityViaThread();
    }

    private static JniObject? FindActivityViaThread()
    {
        var activityThread = JniClass.Find("android/app/ActivityThread");
        if (activityThread == null) return null;

        JniObject? thread = null, activities = null;
        try
        {
            thread = activityThread.CallStaticObject("currentActivityThread", "()Landroid/app/ActivityThread;");
            if (thread == null) return null;
            activities = thread.GetFieldObject("mActivities", "Landroid/util/ArrayMap;");
            if (activities == null) return null;

            var count = activities.CallInt("size", "()I");
            for (var i = 0; i < count; i++)
            {
                using var activityRecord = activities.CallObject("valueAt", "(I)Ljava/lang/Object;", i);
                var activity = activityRecord?.GetFieldObject("activity", "Landroid/app/Activity;");
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
            activityThread.Dispose();
        }
    }
}
