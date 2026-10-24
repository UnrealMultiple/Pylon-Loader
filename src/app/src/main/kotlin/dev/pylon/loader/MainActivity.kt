package dev.pylon.loader

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.content.res.AssetManager
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Bundle
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import dev.pylon.loader.common.RuntimeLayout
import dev.pylon.loader.game.HostOptions
import dev.pylon.loader.ui.BottomNavBar
import dev.pylon.loader.ui.Fonts
import dev.pylon.loader.ui.LauncherScreen
import dev.pylon.loader.ui.Palette
import dev.pylon.loader.ui.PylonDialog
import dev.pylon.loader.ui.dpf
import dev.pylon.loader.ui.formatTime
import dev.pylon.loader.ui.dp
import java.io.File

/**
 * 启动器入口：读磁盘/AssetManager 的真实状态（[LauncherState]）、挂主页/日志/Mod 三页 + 底部导航、
 * 拉起 :game 宿主进程并原样转交 adb 验收开关。运行时随构建内置，没有导入流程。
 * 状态永远从文件重新读：进程随时可能被杀掉重启，内存里的 flag 不能当作事实。
 */
class MainActivity : Activity(), LauncherScreen.Listener {

    private lateinit var screen: LauncherScreen
    private var logScreen: LogScreen? = null
    private var modsScreen: ModsScreen? = null
    private lateinit var navBar: BottomNavBar
    private var currentState: LauncherState? = null

    /** debug 入口只处理一次（防止 Activity 重建后重复自动启动）。 */
    private var intentConsumed = false

    private var checkingExit = false
    private val preferences by lazy { getSharedPreferences("pylon-launcher", MODE_PRIVATE) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        configureWindow()

        screen = LauncherScreen(this, this)
        logScreen = LogScreen(this)
        modsScreen = ModsScreen(this).also { it.refresh() }
        setContentView(buildContentView())
        applySystemBarAppearance()

        refreshState()
        screen.playEntrance()
        handleDebugIntent(intent)
        inspectGameExit()
    }

    private fun buildContentView(): View {
        val pages = FrameLayout(this)
        pages.addView(screen.root, FrameLayout.LayoutParams(MATCH_PARENT, MATCH_PARENT))
        logScreen?.root?.let { log ->
            log.visibility = View.GONE
            pages.addView(log, FrameLayout.LayoutParams(MATCH_PARENT, MATCH_PARENT))
        }
        modsScreen?.let { mods ->
            mods.root.visibility = View.GONE
            pages.addView(mods.root, FrameLayout.LayoutParams(MATCH_PARENT, MATCH_PARENT))
        }

        navBar = BottomNavBar(this) { index -> selectTab(index) }

        val root = FrameLayout(this).apply { setBackgroundColor(Palette.BackgroundBottom) }
        root.addView(pages, FrameLayout.LayoutParams(MATCH_PARENT, MATCH_PARENT))
        root.addView(
            navBar,
            FrameLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT, Gravity.BOTTOM),
        )

        root.setOnApplyWindowInsetsListener { _, insets ->
            val bottom = systemBottomInset(insets)
            navBar.setPadding(dp(16), dp(6), dp(16), dp(6) + bottom)
            val extra = bottom + dp(62)
            screen.setExtraBottomInset(extra)
            logScreen?.setExtraBottomInset(extra)
            modsScreen?.setExtraBottomInset(extra)
            insets
        }
        return root
    }

    private fun selectTab(tab: Int) {
        val log = logScreen
        val mods = modsScreen
        val show = { view: View?, visible: Boolean -> view?.visibility = if (visible) View.VISIBLE else View.GONE }
        show(log?.root, tab == TAB_LOG)
        show(mods?.root, tab == TAB_MODS)
        screen.root.visibility = if (tab == TAB_HOME) View.VISIBLE else View.GONE
        navBar.setSelected(tab)
        if (tab == TAB_LOG) log?.refresh()
        if (tab == TAB_MODS) mods?.refresh()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleDebugIntent(intent)
    }

    override fun onResume() {
        super.onResume()
        refreshState()
        // 从宿主回来时最可能这里发现崩溃：原生死掉的进程不会留下任何 Java 痕迹。
        inspectGameExit()
    }

    private fun refreshState() {
        val state = readState()
        currentState = state
        screen.render(state)
        logScreen?.refresh()
        modsScreen?.refresh()
    }

    // ------------------------------------------------------------------
    // 窗口：边到边 + 深色系统栏
    // ------------------------------------------------------------------

    @Suppress("DEPRECATION")
    private fun configureWindow() {
        window.statusBarColor = Color.TRANSPARENT
        window.navigationBarColor = Color.TRANSPARENT

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            window.isNavigationBarContrastEnforced = false
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false)
        } else {
            window.decorView.systemUiVisibility =
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE or
                    View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
                    View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
        }
    }

    /** 深色系统栏图标。必须等 setContentView 之后：Android 15+ 的 PhoneWindow.getInsetsController() 在 decorView 建好前会 NPE。 */
    private fun applySystemBarAppearance() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.insetsController?.setSystemBarsAppearance(
                0,
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
                    WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS,
            )
        }
    }

    private fun systemBottomInset(insets: WindowInsets): Int =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            insets.getInsets(
                WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout(),
            ).bottom
        } else {
            @Suppress("DEPRECATION")
            insets.systemWindowInsetBottom
        }

    // ------------------------------------------------------------------
    // 状态读取
    // ------------------------------------------------------------------

    private fun readState(): LauncherState {
        val nativeDir = File(applicationInfo.nativeLibraryDir)
        val nativeChecks = RuntimeLayout.NATIVE_LIB_LOAD_ORDER.map { name ->
            val file = File(nativeDir, name)
            FileCheck(
                name = name,
                path = file.absolutePath,
                exists = file.isFile,
                size = if (file.isFile) file.length() else 0L,
            )
        }
        val dataChecks = CRITICAL_DATA_PROBES.map { probe ->
            assetProbe(assets, "${RuntimeLayout.GAME_DATA_ASSET_PREFIX}/$probe")
        }

        // 崩溃卡片要读文件头尾，这里只做小段读取（完整日志在用户点开时才读）。
        val crash = CrashReports.inspect(this)

        return LauncherState(
            nativeChecks = nativeChecks,
            dataChecks = dataChecks,
            crash = crash,
            modsDir = ModStorage.directory(this),
            mods = ModCatalog.scan(ModStorage.directory(this)),
        )
    }

    private fun assetProbe(assets: AssetManager, path: String): FileCheck {
        val exists = runCatching { assets.open(path).use { true } }.getOrDefault(false)
        val size = runCatching { assets.openFd(path).use { it.length } }.getOrDefault(0L)
        return FileCheck(
            name = path,
            path = "assets/$path",
            exists = exists,
            size = size.coerceAtLeast(0L),
        )
    }

    // ------------------------------------------------------------------
    // 用户操作
    // ------------------------------------------------------------------

    override fun onPrimaryAction() {
        val state = currentState ?: readState()
        // 报错不拦启动：文件不完整只是警告，能不能起来交给宿主的启动失败页与日志页反馈。
        if (!state.ready) {
            screen.showError(
                "内置运行时不完整（缺失 " + state.failedChecks.joinToString { it.name } +
                    "），仍尝试启动；详情见日志页。",
            )
        }
        launchGameHost(requireReady = false)
    }

    override fun onCopyDiagnostics() {
        if (copyText("Pylon Loader 诊断", buildDiagnostics())) {
            screen.showInfo("诊断信息已复制到剪贴板")
        } else {
            screen.showError("复制失败：剪贴板不可用")
        }
    }

    override fun onOpenMods() {
        selectTab(TAB_MODS)
    }

    override fun onViewCrash() = showCrashLog()

    override fun onCopyCrash() {
        val text = CrashReports.readAll(this)
        if (text.isBlank()) {
            screen.showInfo("没有崩溃日志")
            return
        }
        if (copyText("Pylon Loader 崩溃日志", text)) {
            screen.showInfo("崩溃日志已复制到剪贴板")
        } else {
            screen.showError("复制失败：剪贴板不可用")
        }
    }

    override fun onClearCrash() {
        PylonDialog(
            host = this,
            title = "清除崩溃日志？",
            message = "会删除 crash-last.txt 与 crash-exit.txt，不影响运行时与 Mod。清除后本次记录不再弹出。",
            positiveLabel = "清除",
            positiveColor = Palette.Danger,
            onPositive = { clearCrash() },
        ).show()
    }


    /** 通用日志查看对话框：等宽文本 + 复制按钮（崩溃/运行日志共用）。 */
    private fun showLogDialog(title: String, text: String, copyLabel: String) {
        val logView = TextView(this).apply {
            this.text = text
            typeface = Fonts.mono
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
            setTextColor(Palette.TextSecondary)
            setTextIsSelectable(true)
            setPadding(dp(4), dp(4), dp(4), dp(4))
        }
        val scroller = ScrollView(this).apply {
            addView(logView)
            setPadding(dp(12), dp(10), dp(12), dp(10))
            background = GradientDrawable().apply {
                cornerRadius = dpf(12f)
                setColor(Palette.SurfaceInput)
                setStroke(dp(1), Palette.StrokeSoft)
            }
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                (resources.displayMetrics.heightPixels * 0.52f).toInt(),
            )
        }

        PylonDialog(
            host = this,
            title = title,
            message = "日志包含会话错误、Mod 输出与启动轨迹，可直接复制反馈给开发者。",
            content = scroller,
            positiveLabel = copyLabel,
            negativeLabel = "关闭",
            onPositive = {
                if (copyText(title, text)) screen.showInfo("已复制到剪贴板") else screen.showError("复制失败：剪贴板不可用")
            },
        ).show()
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        modsScreen?.onActivityResult(requestCode, resultCode, data)
    }

    private fun showCrashLog(title: String = "崩溃日志") {
        val text = CrashReports.readAll(this)
        if (text.isBlank()) {
            screen.showInfo("没有崩溃日志")
            return
        }
        showLogDialog(title = title, text = text, copyLabel = "复制日志")
    }

    private fun clearCrash() {
        if (!CrashReports.clear(this)) {
            screen.showError("清除失败：文件无法删除")
            return
        }
        screen.showInfo("已清除崩溃日志")
        refreshState()
    }

    private fun inspectGameExit() {
        if (checkingExit) return
        checkingExit = true
        val appContext = applicationContext
        Thread({
            val event = runCatching { ExitWatch.inspect(appContext) }
                .onFailure { Log.w("检查宿主退出原因失败：${it.javaClass.simpleName}: ${it.message}") }
                .getOrNull()
            runOnUiThread {
                checkingExit = false
                if (isFinishing || isDestroyed) return@runOnUiThread
                if (event == null) return@runOnUiThread
                refreshState()
                screen.showError("游戏崩溃：${event.detail}")
                screen.scrollToCrash()
                // 崩溃刚发生就把日志摆到眼前：用户接着要做的就是看原因或复制反馈。
                showCrashLog(title = "游戏崩溃日志")
            }
        }, "pylon-exit-watch").start()
    }

    // ------------------------------------------------------------------
    // 启动
    // ------------------------------------------------------------------

    /**
     * adb 驱动入口（该 action 只在 debug manifest 里声明）。
     *
     * 只消费一次：`launch=true` 的 intent 若留着，Activity 每次被重建/回到前台都会重新处理一次，
     * 于是"崩溃 → 宿主退出 → Activity 重建 → 又拉一次游戏"变成无限重启（真机踩到）。
     */
    private fun handleDebugIntent(intent: Intent?) {
        if (intent?.action != ACTION_LAUNCH) return
        if (intentConsumed) return
        intentConsumed = true

        val launch = intent.getBooleanExtra(EXTRA_LAUNCH, false)
        Log.stage("launcher", "收到测试入口：launch=$launch")

        // 消费掉 action，避免重建时再触发一次自动启动
        setIntent(Intent(this, MainActivity::class.java))
        if (launch) launchGameHost(requireReady = false)
    }

    private fun launchGameHost(requireReady: Boolean = true) {
        val state = currentState
        if (requireReady && state != null && !state.ready) {
            screen.showError(
                "内置运行时不完整，无法启动（缺失 " + state.failedChecks.joinToString { it.name } + "）",
            )
            return
        }

        // 崩溃循环保护：上一次启动在 20 秒内、且现在又要启动 = 崩了又被重新拉起，直接拒绝。
        val now = System.currentTimeMillis()
        val last = preferences.getLong(KEY_LAST_LAUNCH_AT, 0L)
        if (now - last in 1..20_000) {
            Log.w("启动宿主被拒绝：上一次启动 ${(now - last) / 1000}s 前，疑似崩溃重启循环")
            screen.showError("游戏刚才异常退出，已阻止自动重启；请手动点启动再试")
            return
        }
        preferences.edit().putLong(KEY_LAST_LAUNCH_AT, now).apply()

        Log.stage("launcher", "启动宿主：$GAME_ACTIVITY_CLASS")
        try {
            startActivity(
                Intent().apply {
                    setClassName(packageName, GAME_ACTIVITY_CLASS)
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    HostOptions.copyInto(intent, this)
                },
            )
        } catch (t: Throwable) {
            Log.e("启动宿主失败", t)
            screen.showError("启动宿主失败：${t.javaClass.simpleName}: ${t.message}")
        }
    }

    // ------------------------------------------------------------------
    // 诊断文本
    // ------------------------------------------------------------------

    private fun copyText(label: String, text: String): Boolean {
        val clipboard = getSystemService(ClipboardManager::class.java) ?: return false
        clipboard.setPrimaryClip(ClipData.newPlainText(label, text))
        return true
    }

    private fun buildDiagnostics(): String {
        val state = currentState ?: readState()
        return buildString {
            appendLine("Pylon Loader 诊断")
            appendLine("app       : ${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})")
            appendLine("time      : ${formatTime(System.currentTimeMillis())}")
            appendLine(
                "device    : ${Build.MANUFACTURER} ${Build.MODEL} · " +
                    "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})",
            )
            appendLine(
                "runtime   : Terraria ${RuntimeLayout.GAME_VERSION_LINE} line · ${RuntimeLayout.ABI} · 内置于本 APK",
            )
            appendLine("nativeDir : ${applicationInfo.nativeLibraryDir}")
            appendLine("checks    : ${state.checks.count { it.matches }}/${state.checks.size} passed")
            state.checks.filterNot { it.matches }.forEach { check ->
                appendLine("  failed  : ${check.name} · ${check.problem ?: "unknown"}")
            }
            appendLine("mods      : ${state.modsDir.absolutePath} " +
                "(${state.enabledModCount}/${state.mods.size} enabled)")
            RunLogs.inspect(this@MainActivity)?.let { runLog ->
                appendLine(
                    "lastrun   : ${runLog.sessionName} · " +
                        "${runLog.errorCount} errors / ${runLog.warnCount} warnings · ${formatTime(runLog.time)}",
                )
            }
            appendLine(
                "crash     : " + (state.crash?.let {
                    "${it.kind.label} · ${it.detail} · ${formatTime(it.time)} · " +
                        "${it.sources} (${it.size} B)"
                } ?: "none"),
            )
        }
    }

    private companion object {
        const val GAME_ACTIVITY_CLASS = "dev.pylon.loader.game.GameActivity"
        const val MATCH_PARENT = FrameLayout.LayoutParams.MATCH_PARENT
        const val WRAP_CONTENT = FrameLayout.LayoutParams.WRAP_CONTENT

        /** 与 src/debug/AndroidManifest.xml 里的 action 一致。 */
        const val ACTION_LAUNCH = "dev.pylon.loader.action.IMPORT"

        /** 仅本页使用：launch=true 直接拉宿主。 */
        const val EXTRA_LAUNCH = "launch"

        /** 上一次拉起宿主的时刻（毫秒）：用来掐掉"崩溃→自动重启"的循环。 */
        const val KEY_LAST_LAUNCH_AT = "lastLaunchAt"

        const val TAB_HOME = 0
        const val TAB_LOG = 1
        const val TAB_MODS = 2

        /** docs/03 排查表点名的关键资源，缺了会黑屏，单独列出。 */
        val CRITICAL_DATA_PROBES = listOf(
            "Managed/Metadata/global-metadata.dat",
            "data.unity3d",
            "resources.resource",
            "unity default resources",
            "boot.config",
        )
    }
}
