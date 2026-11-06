package dev.pylon.loader

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.os.Build
import android.os.Bundle
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.WindowInsetsController
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import dev.pylon.loader.common.ImportManifest
import dev.pylon.loader.common.RuntimeLayout
import dev.pylon.loader.game.HostOptions
import dev.pylon.loader.ui.Fonts
import dev.pylon.loader.ui.LauncherScreen
import dev.pylon.loader.ui.Palette
import dev.pylon.loader.ui.PylonDialog
import dev.pylon.loader.ui.dp
import dev.pylon.loader.ui.dpf
import dev.pylon.loader.ui.formatTime
import dev.pylon.loader.ui.withAlpha
import java.io.File

/**
 * 启动器入口：读磁盘/包管理器的真实状态（[LauncherState]）、后台导入运行时（[RuntimeImport]）并把进度投回 UI、
 * 拉起 :game 宿主进程并原样转交 adb 验收开关；视图与动画都在 [LauncherScreen]，这里只做编排。
 * 状态永远从文件重新读：进程随时可能被杀掉重启，内存里的 flag 不能当作事实。
 */
class MainActivity : Activity(), LauncherScreen.Listener {

    private lateinit var screen: LauncherScreen
    private var currentState: LauncherState? = null
    private var importing = false

    /** debug 入口只处理一次（防止 Activity 重建后重复自动启动）。 */
    private var intentConsumed = false

    private var checkingExit = false
    private val preferences by lazy { getSharedPreferences("pylon-launcher", MODE_PRIVATE) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        configureWindow()

        screen = LauncherScreen(this, this)
        setContentView(screen.root)
        applySystemBarAppearance()

        val state = readState()
        currentState = state
        screen.render(state)
        screen.playEntrance()
        handleDebugIntent(intent)
        inspectGameExit()
    }

    /** Activity 已存在时系统只发 onNewIntent，不重建；adb 驱动必须两条路都处理。 */
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleDebugIntent(intent)
    }

    override fun onResume() {
        super.onResume()
        // 导入中不要用磁盘状态覆盖进度卡；结束后统一刷新。
        if (!importing) {
            refreshState()
            // 从宿主回来时最可能这里发现崩溃：原生死掉的进程不会留下任何 Java 痕迹。
            inspectGameExit()
        }
    }

    private fun refreshState() {
        val state = readState()
        currentState = state
        screen.render(state)
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
            @Suppress("DEPRECATION")
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

    // ------------------------------------------------------------------
    // 状态读取
    // ------------------------------------------------------------------

    private fun readState(): LauncherState {
        val selectedPackage = selectedGamePackage()
        val pkgInfo = try {
            packageManager.getPackageInfo(selectedPackage, 0)
        } catch (t: Throwable) {
            null
        }
        val gameLabel = pkgInfo?.applicationInfo?.let { info ->
            runCatching { info.loadLabel(packageManager).toString() }.getOrNull()
        }

        val modsDir = ModStorage.directory(this)
        val manifest = ImportManifest.readFrom(RuntimeImport.manifestFile(this))
        val libDir = RuntimeImport.nativeLibDir(this)
        val dataDir = File(RuntimeImport.runtimeDir(this), RuntimeLayout.DATA_SUBDIR)

        val nativeChecks = RuntimeLayout.NATIVE_LIB_LOAD_ORDER.map { name ->
            val file = File(libDir, name)
            FileCheck(
                name = name,
                path = file.absolutePath,
                exists = file.isFile,
                size = if (file.isFile) file.length() else 0L,
                expectedSize = manifest?.nativeLibs?.get(name)?.size,
            )
        }
        val dataChecks = CRITICAL_DATA_PROBES.map { probe ->
            val file = File(dataDir, probe)
            FileCheck(
                name = probe,
                path = file.absolutePath,
                exists = file.isFile,
                size = if (file.isFile) file.length() else 0L,
                expectedSize = manifest?.dataFiles?.get(probe)?.size,
            )
        }

        // 崩溃卡片要读文件头尾，这里只做小段读取（完整日志在用户点开时才读）。
        val crash = CrashReports.inspect(this)

        return LauncherState(
            selectedPackage = selectedPackage,
            gameLabel = gameLabel,
            gameInstalled = pkgInfo != null,
            gameVersionName = pkgInfo?.versionName,
            gameVersionCode = pkgInfo?.longVersionCode,
            manifest = manifest,
            nativeChecks = nativeChecks,
            dataChecks = dataChecks,
            crash = crash,
            runtimeDir = RuntimeImport.runtimeDir(this),
            modsDir = modsDir,
            mods = ModCatalog.scan(modsDir),
        )
    }

    private fun selectedGamePackage(): String =
        preferences.getString(KEY_SELECTED_GAME, null) ?: RuntimeLayout.GAME_PACKAGE

    private fun setSelectedGamePackage(packageName: String) {
        preferences.edit().putString(KEY_SELECTED_GAME, packageName).apply()
    }

    // ------------------------------------------------------------------
    // 用户操作
    // ------------------------------------------------------------------

    override fun onPrimaryAction() {
        val state = currentState ?: readState()
        if (!state.gameInstalled || !state.gameVersionMatches) {
            // 没选到可用游戏时，主按钮直接打开"已安装游戏"选择器。
            onChooseGame()
            return
        }
        if (state.ready) {
            launchGameHost()
        } else {
            // 清单在但校验不过 / 不属于当前游戏 = 需要修复；force=false 会跳过同版本清单。
            runImport(force = state.manifest != null)
        }
    }

    override fun onChooseGame() {
        val choices = runCatching { queryInstalledGames() }.getOrElse {
            screen.showError("读取已安装应用失败：${it.javaClass.simpleName}")
            return
        }
        if (choices.isEmpty()) {
            screen.showError("没有找到可选择的已安装游戏")
            return
        }

        val list = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        lateinit var dialog: PylonDialog
        choices.forEachIndexed { index, choice ->
            if (index > 0) {
                list.addView(
                    View(this).apply { setBackgroundColor(Palette.StrokeSoft) },
                    LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(1)),
                )
            }
            list.addView(
                gameRow(choice) {
                    if (!choice.supported) return@gameRow
                    dialog.dismiss()
                    selectGame(choice)
                },
                LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT),
            )
        }

        val scroller = ScrollView(this).apply {
            addView(list)
            val wanted = choices.size * dp(62) + dp(2)
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                minOf(wanted, (resources.displayMetrics.heightPixels * 0.50f).toInt()),
            )
        }
        dialog = PylonDialog(
            host = this,
            title = "选择已安装的游戏",
            message = "只支持 Terraria ${RuntimeLayout.GAME_VERSION_LINE} 系列 · ${RuntimeLayout.ABI}。\n" +
                "导入会从所选游戏的 APK 解出关键文件，不读取打包产物。",
            content = scroller,
            positiveLabel = "关闭",
            negativeLabel = null,
            positiveColor = Palette.TextSecondary,
        )
        dialog.show()
    }

    /** 只列出发射器可见的应用；优先游戏分类，找不到分类时退回全部可启动应用。 */
    private fun queryInstalledGames(): List<GameChoice> {
        val pm = packageManager
        val launchIntent = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)
        val apps = pm.queryIntentActivities(launchIntent, 0)
            .asSequence()
            .mapNotNull { it.activityInfo?.applicationInfo }
            .distinctBy { it.packageName }
            .filter { it.packageName != packageName }
            .toList()

        val selected = selectedGamePackage()
        val games = apps.filter { info ->
            info.category == ApplicationInfo.CATEGORY_GAME ||
                info.packageName == selected ||
                info.packageName == RuntimeLayout.GAME_PACKAGE
        }
        val pool = (if (games.isNotEmpty()) games else apps).take(80)

        return pool.mapNotNull { info ->
            val pkg = runCatching { pm.getPackageInfo(info.packageName, 0) }.getOrNull()
                ?: return@mapNotNull null
            val versionOk = RuntimeLayout.isSupportedVersion(pkg.versionName)
            val abiOk = info.nativeLibraryDir?.contains("/arm64") == true
            GameChoice(
                packageName = info.packageName,
                label = runCatching { info.loadLabel(pm).toString() }.getOrDefault(info.packageName),
                versionName = pkg.versionName,
                versionCode = pkg.longVersionCode,
                icon = runCatching { info.loadIcon(pm) }.getOrNull(),
                supported = versionOk && abiOk,
                reason = when {
                    !versionOk -> "版本 ${pkg.versionName ?: pkg.longVersionCode} 不在 ${RuntimeLayout.GAME_VERSION_LINE} 线"
                    !abiOk -> "不是 arm64-v8a"
                    else -> null
                },
                selected = info.packageName == selected,
            )
        }.sortedWith(
            compareByDescending<GameChoice> { it.supported }
                .thenByDescending { it.selected }
                .thenBy { it.label.lowercase() },
        )
    }

    private fun selectGame(choice: GameChoice) {
        setSelectedGamePackage(choice.packageName)
        refreshState()
        val state = currentState ?: return
        if (state.ready) {
            screen.showSuccess("已选择 ${choice.label} ${choice.versionName ?: ""}".trim())
            return
        }
        if (!state.canImport) {
            screen.showError("${choice.label} 的版本不受支持：${choice.reason ?: "未知原因"}")
            return
        }
        screen.showInfo("已选择 ${choice.label}，开始导入运行时")
        runImport(force = state.manifest != null)
    }

    private fun gameRow(choice: GameChoice, onPick: () -> Unit): View {
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(10), dp(9), dp(10), dp(9))
            isClickable = choice.supported
            isFocusable = choice.supported
            if (choice.supported) setOnClickListener { onPick() }
        }

        val icon = ImageView(this).apply {
            if (choice.icon != null) {
                setImageDrawable(choice.icon)
            } else {
                setImageResource(R.drawable.ic_folder)
                setColorFilter(Palette.TextSecondary)
            }
        }
        row.addView(icon, LinearLayout.LayoutParams(dp(38), dp(38)))

        val info = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12), 0, dp(8), 0)
        }
        info.addView(
            TextView(this).apply {
                text = choice.label
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
                setTextColor(Palette.TextPrimary)
                typeface = Fonts.medium
                includeFontPadding = false
                maxLines = 1
                ellipsize = android.text.TextUtils.TruncateAt.END
            },
            LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT),
        )
        info.addView(
            TextView(this).apply {
                text = "${choice.versionName ?: "?"} (${choice.versionCode}) · ${choice.packageName}"
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 9.5f)
                setTextColor(Palette.TextTertiary)
                typeface = Fonts.mono
                includeFontPadding = false
                maxLines = 1
                ellipsize = android.text.TextUtils.TruncateAt.MIDDLE
                setPadding(0, dp(3), 0, 0)
            },
            LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT),
        )
        row.addView(info, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))

        val trailing = TextView(this).apply {
            text = when {
                !choice.supported -> "不支持"
                choice.selected -> "当前"
                else -> "选择"
            }
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
            typeface = Fonts.medium
            includeFontPadding = false
            setTextColor(
                when {
                    !choice.supported -> Palette.Warning
                    choice.selected -> Palette.Success
                    else -> Palette.Accent
                },
            )
            setPadding(dp(8), dp(4), 0, dp(4))
        }
        row.addView(trailing, LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT))

        if (!choice.supported) row.alpha = 0.55f

        val radius = dpf(14f)
        val fill = GradientDrawable().apply {
            cornerRadius = radius
            setColor(if (choice.selected) Palette.Accent.withAlpha(0.10f) else Color.TRANSPARENT)
            if (choice.selected) setStroke(dp(1), Palette.Accent.withAlpha(0.30f))
        }
        val mask = GradientDrawable().apply {
            setColor(Color.WHITE)
            cornerRadius = radius
        }
        row.background = if (choice.supported) {
            RippleDrawable(ColorStateList.valueOf(Palette.Accent.withAlpha(0.14f)), fill, mask)
        } else {
            fill
        }
        return row
    }

    override fun onReimport() {
        val state = currentState ?: readState()
        if (!state.canImport) {
            screen.showError(
                if (!state.gameInstalled) "未检测到正版游戏，无法导入"
                else "当前游戏版本不受支持，无法导入",
            )
            return
        }
        PylonDialog(
            host = this,
            title = "重新导入运行时？",
            message = "会重新从已安装的游戏里解出全部文件并逐项校验，完成后替换现有导入。\n\n期间请不要退出应用。",
            positiveLabel = "重新导入",
            onPositive = { runImport(force = true) },
        ).show()
    }

    override fun onCopyDiagnostics() {
        if (copyText("Pylon Loader 诊断", buildDiagnostics())) {
            screen.showInfo("诊断信息已复制到剪贴板")
        } else {
            screen.showError("复制失败：剪贴板不可用")
        }
    }

    override fun onOpenMods() {
        startActivity(Intent(this, ModsActivity::class.java))
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

    private fun showCrashLog(title: String = "崩溃日志") {
        val text = CrashReports.readAll(this)
        if (text.isBlank()) {
            screen.showInfo("没有崩溃日志")
            return
        }
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
            message = "日志来自宿主进程与系统退出记录，可直接复制反馈给开发者。",
            content = scroller,
            positiveLabel = "复制日志",
            negativeLabel = "关闭",
            onPositive = { onCopyCrash() },
        ).show()
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
    // 导入与启动
    // ------------------------------------------------------------------

    /**
     * adb 驱动入口（该 action 只在 debug manifest 里声明）。
     *
     * 只消费一次：`launch=true` 的 intent 若留着，Activity 每次被重建/回到前台都会重新处理一次，
     * 于是"崩溃 → 宿主退出 → Activity 重建 → 又拉一次游戏"变成无限重启（真机踩到）。
     */
    private fun handleDebugIntent(intent: Intent?) {
        if (intent?.action != ACTION_IMPORT) return
        if (intentConsumed) return
        intentConsumed = true

        val force = intent.getBooleanExtra(EXTRA_FORCE, false)
        val launch = intent.getBooleanExtra(EXTRA_LAUNCH, false)
        Log.stage("launcher", "收到测试入口：force=$force launch=$launch")

        // 消费掉 action，避免重建时再触发一次自动启动
        setIntent(Intent(this, MainActivity::class.java))
        // 验收路径不因界面状态提前拦截：要能观察"未就绪时宿主怎么失败"。
        runImport(force) { if (launch) launchGameHost(requireReady = false) }
    }

    /** 导入约 175MB 并逐文件核对哈希，放后台线程（主线程会 ANR）；完成回调在 UI 线程执行。 */
    private fun runImport(force: Boolean, onDone: (() -> Unit)? = null) {
        if (importing) {
            Log.w("已有导入在进行，忽略本次请求")
            onDone?.invoke()
            return
        }
        importing = true
        screen.setBusy(true)
        screen.showImport()
        screen.updateImport("准备导入…", null)

        val appContext = applicationContext
        val targetPackage = currentState?.selectedPackage ?: selectedGamePackage()
        Thread({
            val result = runCatching {
                RuntimeImport.import(
                    appContext,
                    force = force,
                    gamePackage = targetPackage,
                ) { progress ->
                    // 回调在导入线程；UI 已销毁时不再碰 View。
                    runOnUiThread {
                        if (importing && !isFinishing && !isDestroyed) {
                            screen.updateImport(progress.stage, progress.fraction)
                        }
                    }
                }
            }
            runOnUiThread {
                if (isFinishing || isDestroyed) return@runOnUiThread
                importing = false
                screen.hideImport()
                screen.setBusy(false)
                refreshState()

                result.onSuccess { importResult ->
                    when {
                        !importResult.success -> screen.showError(importResult.error ?: "导入失败")
                        importResult.skipped -> screen.showInfo("运行时已是最新，无需重新导入")
                        else -> screen.showSuccess(
                            "导入完成 · ${importResult.manifest?.nativeLibs?.size ?: 0} 个原生库 / " +
                                "${importResult.manifest?.dataFiles?.size ?: 0} 个资源",
                        )
                    }
                }.onFailure { t ->
                    screen.showError("导入异常：${t.javaClass.simpleName}: ${t.message}")
                }
                onDone?.invoke()
            }
        }, "pylon-import").start()
    }

    private fun launchGameHost(requireReady: Boolean = true) {
        val state = currentState
        if (requireReady && state != null && !state.ready) {
            screen.showError(
                if (!state.gameInstalled) "未检测到正版游戏，无法启动"
                else "运行时未就绪，请先完成导入",
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
        val gamePackage = state?.selectedPackage ?: selectedGamePackage()
        try {
            startActivity(
                Intent().apply {
                    setClassName(packageName, GAME_ACTIVITY_CLASS)
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    HostOptions.copyInto(intent, this) // 把 adb 传来的验收开关转交给宿主
                    putExtra(HostOptions.EXTRA_GAME_PACKAGE, gamePackage)
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
                "target    : Terraria ${RuntimeLayout.GAME_VERSION_LINE} line · ${RuntimeLayout.ABI}",
            )
            appendLine("selected  : ${state.selectedPackage} (${state.gameLabel ?: "?"})")
            appendLine(
                "game      : " + if (state.gameInstalled) {
                    "installed ${state.gameVersionName} (${state.gameVersionCode})"
                } else {
                    "not installed"
                },
            )
            appendLine(
                "manifest  : " + if (state.manifest == null) {
                    "none"
                } else {
                    "${state.manifest.gameVersionName} (${state.manifest.gameVersionCode}) · " +
                        "native ${state.manifest.nativeLibs.size} / data ${state.manifest.dataFiles.size} · " +
                        "${state.manifest.totalBytes} B"
                },
            )
            appendLine("checks    : ${state.checks.count { it.matches }}/${state.checks.size} passed")
            state.checks.filterNot { it.matches }.forEach { check ->
                appendLine("  failed  : ${check.name} · ${check.problem ?: "unknown"}")
            }
            appendLine("runtime   : ${state.runtimeDir.absolutePath}")
            appendLine("mods      : ${state.modsDir.absolutePath} " +
                "(${state.enabledModCount}/${state.mods.size} enabled)")
            appendLine(
                "crash     : " + (state.crash?.let {
                    "${it.kind.label} · ${it.detail} · ${formatTime(it.time)} · " +
                        "${it.sources} (${it.size} B)"
                } ?: "none"),
            )
        }
    }

    private data class GameChoice(
        val packageName: String,
        val label: String,
        val versionName: String?,
        val versionCode: Long,
        val icon: Drawable?,
        val supported: Boolean,
        val reason: String?,
        val selected: Boolean,
    )

    private companion object {
        const val GAME_ACTIVITY_CLASS = "dev.pylon.loader.game.GameActivity"

        /** 与 src/debug/AndroidManifest.xml 里的 action 一致。 */
        const val ACTION_IMPORT = "dev.pylon.loader.action.IMPORT"

        /** 仅本页使用：导入与启动的两个驱动开关。 */
        const val EXTRA_FORCE = "force"
        const val EXTRA_LAUNCH = "launch"
        const val KEY_SELECTED_GAME = "selectedGamePackage"

        /** 上一次拉起宿主的时刻（毫秒）：用来掐掉"崩溃→自动重启"的循环。 */
        const val KEY_LAST_LAUNCH_AT = "lastLaunchAt"

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
