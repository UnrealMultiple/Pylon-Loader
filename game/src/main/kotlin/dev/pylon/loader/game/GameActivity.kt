package dev.pylon.loader.game

import android.app.Activity
import android.content.Intent
import android.content.res.Configuration
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.InputDevice
import android.view.InputEvent
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.PointerIcon
import android.view.SurfaceView
import android.view.View
import android.view.Window
import android.view.WindowManager

/**
 * 宿主进程里唯一的 Activity，也是传给 UnityPlayer 的那个 Context。
 *
 * 三件必须做对的事（证据见 docs/07）：
 *  1. `UnityPlayer.<init>` 内部是裸 `instanceof Activity`（不拆 ContextWrapper），
 *     所以必须传真 Activity，否则 `mActivity` 为 null。
 *  2. 游戏 C# 会按名字在本 Activity 上取 `MouseInside` / `MouseMode` / `PressedStates` /
 *     `mUnityPlayer` 等成员（游戏自己的 UnityPlayerActivity 被改过），名字与数组长度照 dex 实现。
 *  3. 生命周期要按 **UnityPlayer** 的方法名调（`onStart`/`onResume`/…/`windowFocusChanged`），
 *     不是 Activity 的 `on*` 名字；叫错会静默跳过。
 *
 * 启动编排在 HostBootstrap，诊断探针在 HostDiagnostics，这里只留生命周期、输入与 JNI 面。
 */
class GameActivity : Activity() {

    /** 后台线程要读它（HostBootstrap 投递到游戏线程时），所以必须是 volatile。 */
    @Volatile
    private var player: UnityPlayerRef? = null

    @Volatile
    private var runtimeLoaded = false

    /**
     * 引擎拿 `getPackageCodePath()` 打开 base APK 的 zip（assets/bin/Data）；
     * 导入形态要指到所选游戏的 APK，onCreate 解析 options 后才设置，之前回落到宿主 APK。
     */
    @Volatile
    private var gameApkPath: String? = null

    // 引擎可能晚于生命周期创建，所以自己记一份当前状态，创建后补投（见 replayLifecycle）。
    private var startedState = false
    private var resumedState = false
    private var focusState = false
    private lateinit var options: HostOptions
    private lateinit var bootstrap: HostBootstrap
    private val uiHandler = Handler(Looper.getMainLooper())
    private val diagnosticsTasks = mutableListOf<Runnable>()

    // ------------------------------------------------------------------
    // 游戏 C# 经 JNI 按名字查找的成员。@JvmField 是必须的：它们得是字段而不是 Kotlin 属性。
    // 声明来源：refs/classes.dex 的 Lcom/unity3d/player/UnityPlayerActivity;
    //   MouseInside Z / MouseMode I / PressedStates [Z（长度 330）/ mUnityPlayer Lcom/unity3d/player/UnityPlayer;
    // ------------------------------------------------------------------

    @JvmField
    var MouseInside: Boolean = false

    @JvmField
    var MouseMode: Int = 0

    @JvmField
    val PressedStates = BooleanArray(PRESSED_STATES_SIZE)

    @JvmField
    var mUnityPlayer: Any? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        // requestWindowFeature 必须在 super.onCreate 之前，否则窗口特性不生效、
        // decorView 尺寸为 0x0 → SurfaceView 拿不到 surface（黑屏且引擎无日志）。
        requestWindowFeature(Window.FEATURE_NO_TITLE)
        super.onCreate(savedInstanceState)
        installCrashHandler()

        val debuggable = (applicationInfo.flags and android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE) != 0
        options = HostOptions.from(intent, debuggable)
        gameApkPath = runCatching {
            packageManager.getPackageInfo(options.gamePackage, 0).applicationInfo?.sourceDir
        }.getOrNull()
        Log.stage("host", "游戏 APK 路径：${gameApkPath ?: "（回落到宿主 APK）"}")
        Log.stage("host", "GameActivity.onCreate 进程=${android.os.Process.myPid()} 线程=${Thread.currentThread().name}")

        // 游戏的 onCreate 会把 `unity` extra 原样写回（本构建里 updateUnityCommandLineArguments 是恒等函数）。
        intent?.putExtra(UNITY_EXTRA, intent.getStringExtra(UNITY_EXTRA) ?: "")

        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        // 先放一帧：装载运行时要 dlopen 57MB 的 libil2cpp.so，不能让它压着首帧。
        setContentView(android.view.View(this).apply { setBackgroundColor(android.graphics.Color.BLACK) })

        bootstrap = HostBootstrap(this, options) { player }
        loadRuntimeThenStartUnity()
    }

    /**
     * Unity 打开 base APK 资源走这两个路径；导入形态指向已装游戏 APK，options 解析后才有效，之前回落宿主 APK。
     */
    override fun getPackageCodePath(): String = gameApkPath ?: super.getPackageCodePath()

    override fun getPackageResourcePath(): String = gameApkPath ?: super.getPackageResourcePath()

    /**
     * 运行时装载放工作线程（dex 注入 + 四次 dlopen，含 57MB 的 libil2cpp.so）；
     * 主线程做会卡住首帧与输入派发（ANR 按主线程判定）。完成后回主线程创建 UnityPlayer。
     */
    private fun loadRuntimeThenStartUnity() {
        Thread({
            val loaded = bootstrap.loadRuntime()
            uiHandler.post {
                if (isDestroyed || isFinishing) return@post
                if (!loaded) return@post
                runtimeLoaded = true
                if (options.diagnostics) HostDiagnostics.logResources(this)
                createUnityPlayer()
                // UnityPlayer 已就位才开观察线程：否则后台线程可能读到 null 而永久放弃投递。
                bootstrap.beginWatch()
            }
        }, "pylon-runtime-load").apply { isDaemon = true }.start()
    }

    private fun createUnityPlayer() {
        try {
            val proxy = UnityPlayerRef.createLifecycleProxy(
                onUnloaded = { runOnUiThread { moveTaskToBack(true) } },
                onQuit = {
                    Log.stage("host", "Unity 请求退出")
                    runOnUiThread { finish() }
                },
            )
            val created = UnityPlayerRef.create(this, proxy)
            player = created
            mUnityPlayer = created.rawInstance()  // 游戏 C# 读的就是这个字段

            val view = created.asView()
            if (view == null) {
                Log.w("UnityPlayer 不是 View，无法 setContentView")
            } else {
                setContentView(view)
                view.requestFocus()
            }
            // 装载在工作线程，onStart/onResume 可能已过去，必须补投，否则引擎构造完不被驱动（实测约 1.5s 后空指针 SIGSEGV）。
            replayLifecycle(created)
            if (options.diagnostics) {
                postDelayedDiagnostics(5_000) { HostDiagnostics.logPlayerInternals(this, created) }
            }
            Log.stage("host", "UnityPlayer 已创建，等待 IL2CPP 就绪")
        } catch (t: Throwable) {
            Log.e("创建 UnityPlayer 失败", t)
            showStartupFailure("创建 UnityPlayer 失败：${t.javaClass.simpleName}: ${t.message}")
        }
    }

    // ------------------------------------------------------------------
    // 游戏 C# 按名字调用的方法（照 dex 语义）
    // ------------------------------------------------------------------

    /** `IsKeyPressed(int): boolean`。原版直接数组取值，这里加边界检查（越界会抛回 C#）。 */
    fun IsKeyPressed(code: Int): Boolean = code in PressedStates.indices && PressedStates[code]

    /** Android KeyEvent → Unity KeyCode，整张表照 dex 的 `GetUnityKeyCode` 抄写，未命中返回 0。 */
    fun GetUnityKeyCode(androidKeyCode: Int): Int = when (androidKeyCode) {
        in KeyEvent.KEYCODE_A..KeyEvent.KEYCODE_Z -> androidKeyCode - KeyEvent.KEYCODE_A + 'a'.code
        in KeyEvent.KEYCODE_1..KeyEvent.KEYCODE_9 -> androidKeyCode - KeyEvent.KEYCODE_1 + '1'.code
        in KeyEvent.KEYCODE_F1..KeyEvent.KEYCODE_F12 -> androidKeyCode - KeyEvent.KEYCODE_F1 + 282
        KeyEvent.KEYCODE_ESCAPE, KeyEvent.KEYCODE_BACK -> 27
        KeyEvent.KEYCODE_GRAVE -> '`'.code
        KeyEvent.KEYCODE_0 -> '0'.code
        KeyEvent.KEYCODE_SPACE -> ' '.code
        KeyEvent.KEYCODE_MINUS -> '-'.code
        KeyEvent.KEYCODE_EQUALS -> '='.code
        KeyEvent.KEYCODE_DEL -> 8
        KeyEvent.KEYCODE_FORWARD_DEL -> 127
        KeyEvent.KEYCODE_SHIFT_LEFT -> 304
        KeyEvent.KEYCODE_SHIFT_RIGHT -> 303
        KeyEvent.KEYCODE_ENTER -> 13
        KeyEvent.KEYCODE_DPAD_UP -> 273
        KeyEvent.KEYCODE_DPAD_DOWN -> 274
        KeyEvent.KEYCODE_DPAD_RIGHT -> 275
        KeyEvent.KEYCODE_DPAD_LEFT -> 276
        KeyEvent.KEYCODE_COMMA -> ','.code
        KeyEvent.KEYCODE_PERIOD -> '.'.code
        KeyEvent.KEYCODE_SLASH -> '/'.code
        KeyEvent.KEYCODE_ALT_LEFT -> 308
        KeyEvent.KEYCODE_ALT_RIGHT -> 307
        KeyEvent.KEYCODE_CTRL_RIGHT -> 305
        KeyEvent.KEYCODE_CTRL_LEFT -> 306
        KeyEvent.KEYCODE_CAPS_LOCK -> 301
        KeyEvent.KEYCODE_TAB -> 9
        KeyEvent.KEYCODE_LEFT_BRACKET -> '['.code
        KeyEvent.KEYCODE_RIGHT_BRACKET -> ']'.code
        KeyEvent.KEYCODE_APOSTROPHE -> '\''.code
        KeyEvent.KEYCODE_SEMICOLON -> ';'.code
        KeyEvent.KEYCODE_BACKSLASH -> '\\'.code
        else -> 0
    }

    /** C# 读的是 `MouseMode` 字段，赋值才是有效部分；光标图标在触摸屏上看不见，照抄逻辑即可。 */
    fun SetMouseCursorMode(mode: Int) {
        MouseMode = mode
        applyArrowPointerIcon()
    }

    /** 游戏里是 `if (SDK_INT < 31) return`，然后给 Surface 设帧率。 */
    fun SetSurfaceFrameRate(fps: Float) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return
        runCatching { player?.surfaceView()?.holder?.surface?.setFrameRate(fps, 0) }
    }

    /** Unity 的 SurfaceView（游戏原版用反射读 `UnityPlayer.mGlView`）。 */
    fun getSurfaceView(): SurfaceView? = player?.surfaceView()

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    override fun onStart() {
        super.onStart()
        startedState = true
        if (runtimeLoaded) player?.onStart()
    }

    override fun onResume() {
        super.onResume()
        resumedState = true
        // onStart/onResume 内部会 setupUnityToBeResumed，引擎从这里开始启动。
        if (runtimeLoaded) player?.onResume()
        if (options.diagnostics) {
            HostDiagnostics.logViewTree(this)
            // attach 是异步的，onResume 时看还偏早，10 秒后补一次。
            postDelayedDiagnostics(10_000) { HostDiagnostics.logViewTree(this) }
        }
    }

    override fun onPause() {
        // 对齐 configChanges 后多窗口暂停由 Unity 自己处理，只有真正结束时才 pause（同游戏）。
        resumedState = false
        if (runtimeLoaded && !isFinishing) player?.onPause()
        super.onPause()
    }

    override fun onStop() {
        startedState = false
        if (runtimeLoaded) player?.onStop()
        super.onStop()
    }

    override fun onDestroy() {
        diagnosticsTasks.forEach { uiHandler.removeCallbacks(it) }
        diagnosticsTasks.clear()
        uiHandler.removeCallbacksAndMessages(null)
        bootstrap.cancel()  // 别让内核等待线程白跑 45s 后再来碰已销毁的 Activity
        if (runtimeLoaded) player?.destroy()  // UnityPlayer 只有 destroy()，没有 onDestroy()；不要调两次
        player = null
        mUnityPlayer = null
        runtimeLoaded = false
        super.onDestroy()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        focusState = hasFocus
        // 引擎的恢复路径就在这里（UnityPlayer.windowFocusChanged），名字没有 on 前缀。
        if (runtimeLoaded) player?.windowFocusChanged(hasFocus)
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        if (runtimeLoaded) player?.configurationChanged(newConfig)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        if (runtimeLoaded) player?.newIntent(intent)
    }

    override fun onLowMemory() {
        super.onLowMemory()
        if (runtimeLoaded) player?.lowMemory()
    }

    @Suppress("DEPRECATION")  // 常量被标过时，但游戏就是按 15 判定的
    override fun onTrimMemory(level: Int) {
        super.onTrimMemory(level)
        if (level == android.content.ComponentCallbacks2.TRIM_MEMORY_COMPLETE) player?.lowMemory()
    }

    // ------------------------------------------------------------------
    // 输入：一律经 UnityPlayer.injectEvent 交给引擎（照 dex 语义）
    // ------------------------------------------------------------------

    @Suppress("DEPRECATION")  // 照 dex 语义走
    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (event.action == KeyEvent.ACTION_MULTIPLE && player?.injectEvent(event) == true) return true
        return super.dispatchKeyEvent(event)
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        setPressed(keyCode, true)
        return player?.injectEvent(event) ?: super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean {
        setPressed(keyCode, false)
        return player?.injectEvent(event) ?: super.onKeyUp(keyCode, event)
    }

    /** 触摸：只转发鼠标来源事件（游戏 `onTouch` 语义）；手指触摸由 Unity 的 SurfaceView 原生处理，再注入会重复。 */
    override fun onTouchEvent(event: MotionEvent): Boolean =
        if (isMouseEvent(event)) player?.injectEvent(event) ?: false else false

    /** 鼠标/触控板悬停（游戏 `onGenericMotion`）：真实鼠标悬停由游戏自己吞掉，进入/离开维护 MouseInside 并更新光标，其余统一注入。 */
    override fun onGenericMotionEvent(event: MotionEvent): Boolean {
        if (isMouseEvent(event)) return true
        when (event.action) {
            MotionEvent.ACTION_HOVER_ENTER -> {
                applyArrowPointerIcon()
                MouseInside = true
                SetMouseCursorMode(MouseMode)
            }
            MotionEvent.ACTION_HOVER_EXIT -> {
                applyArrowPointerIcon()
                MouseInside = false
            }
        }
        return player?.injectEvent(event) ?: false
    }

    /** 鼠标来源（且不是手指）：游戏用同样条件区分"鼠标事件"与"触摸事件"。 */
    private fun isMouseEvent(event: MotionEvent): Boolean {
        val device = event.device ?: return false
        return device.supportsSource(InputDevice.SOURCE_MOUSE) &&
            event.getToolType(0) != MotionEvent.TOOL_TYPE_FINGER
    }

    private fun setPressed(androidKeyCode: Int, down: Boolean) {
        val unityCode = GetUnityKeyCode(androidKeyCode)
        if (unityCode in PressedStates.indices) PressedStates[unityCode] = down
    }

    private fun applyArrowPointerIcon() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.N) return
        val instance = mUnityPlayer ?: return
        runCatching {
            val icon = PointerIcon.getSystemIcon(baseContext, PointerIcon.TYPE_ARROW)
            instance.javaClass.getMethod("setPointerIcon", PointerIcon::class.java).invoke(instance, icon)
        }
    }

    // ------------------------------------------------------------------

    /** 启动失败：留痕 + 显示原因。故意不 finish，方便真机上看清原因。 */
    internal fun showStartupFailure(reason: String) {
        CrashLog.write(this, Thread.currentThread(), IllegalStateException("宿主启动失败：$reason"))
        runCatching {
            setContentView(
                android.widget.TextView(this).apply {
                    setBackgroundColor(android.graphics.Color.BLACK)
                    setTextColor(android.graphics.Color.RED)
                    textSize = 14f
                    setPadding(32, 64, 32, 32)
                    text = "Pylon 宿主启动失败\n\n$reason\n\n详情见 logcat（tag=PylonLoader）与崩溃日志文件。"
                }
            )
        }.onFailure { Log.w("显示失败原因时出错：${it.message}") }
    }

    private fun postDelayedDiagnostics(delayMs: Long, task: () -> Unit) {
        val runnable = Runnable { task() }
        diagnosticsTasks += runnable
        uiHandler.postDelayed(runnable, delayMs)
    }

    /** 把 Activity 当前生命周期状态补投给刚创建的引擎，顺序按 onStart → onResume → windowFocusChanged。 */
    private fun replayLifecycle(created: UnityPlayerRef) {
        if (!startedState && !resumedState) return
        Log.stage("host", "补投生命周期：started=$startedState resumed=$resumedState focus=$focusState")
        if (startedState) created.onStart()
        if (resumedState) created.onResume()
        if (focusState) created.windowFocusChanged(true)
    }

    /**
     * 未捕获异常留痕。装在 Activity 而非 Application（ActivityThread 会先装自己的处理器，在 Application 里装会被覆盖）。
     * flag 保证只装一次：否则每次 Activity 重建都会往链上再包一层，一次崩溃写 N 次日志。
     */
    private fun installCrashHandler() {
        if (!crashHandlerInstalled.compareAndSet(false, true)) return
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            CrashLog.write(this, thread, throwable)
            if (previous != null) {
                previous.uncaughtException(thread, throwable)  // 交给上一层，只增加留痕
            } else {
                // 没人接手时必须自己结束进程：吞掉异常会让状态已损坏的进程继续跑。
                android.os.Process.killProcess(android.os.Process.myPid())
                kotlin.system.exitProcess(10)
            }
        }
    }

    private companion object {
        /** 游戏 `PressedStates` 的数组长度（dex 的 <init> 里 new-array 330）。 */
        const val PRESSED_STATES_SIZE = 330

        /** 游戏 UnityPlayerActivity.onCreate 里搬运的命令行 extra 名。 */
        const val UNITY_EXTRA = "unity"

        /** 崩溃处理器只装一次（Activity 会重建）。 */
        val crashHandlerInstalled = java.util.concurrent.atomic.AtomicBoolean(false)
    }
}
