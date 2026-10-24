package dev.pylon.loader

import android.app.Activity
import android.content.Intent
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.ScrollView
import android.widget.Space
import android.widget.TextView
import dev.pylon.loader.ui.ActionButton
import dev.pylon.loader.ui.AmbientBackdrop
import dev.pylon.loader.ui.Fonts
import dev.pylon.loader.ui.Palette
import dev.pylon.loader.ui.PylonCard
import dev.pylon.loader.ui.PylonDialog
import dev.pylon.loader.ui.SnackbarView
import dev.pylon.loader.ui.ToggleSwitch
import dev.pylon.loader.ui.bodyText
import dev.pylon.loader.ui.dp
import dev.pylon.loader.ui.dpf
import dev.pylon.loader.ui.withAlpha
import java.io.File

/**
 * 独立的 Mod 管理页：扫描 Mods/、启停、导入 .dll/.zip/文件夹、删除。
 * 与主页面分开后，主页面只留摘要入口，这一页承担文件操作。
 * 导入走 SAF 无需存储权限；写入先落临时目录，校验主程序集后再换入。
 */
class ModsActivity : Activity() {

    private lateinit var root: FrameLayout
    private lateinit var scroll: ScrollView
    private lateinit var topScrim: View
    private lateinit var importButton: ActionButton
    private lateinit var folderButton: ActionButton
    private lateinit var packButton: ActionButton
    private lateinit var countBadge: TextView
    private lateinit var modsList: LinearLayout
    private lateinit var snackbar: SnackbarView
    private lateinit var overlay: FrameLayout
    private lateinit var overlayLabel: TextView
    private lateinit var overlayProgress: ProgressBar

    private val modsDir: File by lazy { ModStorage.ensure(this) }
    private var busy = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        configureWindow()
        build()
        setContentView(root)
        applySystemBarAppearance()
        refresh()
    }

    override fun onResume() {
        super.onResume()
        if (!busy) refresh()
    }

    // ------------------------------------------------------------------
    // 窗口 / 视图
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

    private fun applySystemBarAppearance() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.insetsController?.setSystemBarsAppearance(
                0,
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
                    WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS,
            )
        }
    }

    private fun build() {
        root = FrameLayout(this).apply { setBackgroundColor(Palette.BackgroundBottom) }
        root.addView(AmbientBackdrop(this), FrameLayout.LayoutParams(MATCH, MATCH))

        scroll = ScrollView(this).apply {
            isVerticalScrollBarEnabled = false
            clipToPadding = false
            isFillViewport = true
        }
        val content = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(content, FrameLayout.LayoutParams(MATCH, WRAP))
        root.addView(scroll, FrameLayout.LayoutParams(MATCH, MATCH))

        topScrim = View(this).apply {
            background = GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM,
                intArrayOf(Palette.BackgroundTop, Palette.BackgroundTop, Color.TRANSPARENT),
            )
            isClickable = false
            importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
        }
        root.addView(topScrim, FrameLayout.LayoutParams(MATCH, 0, Gravity.TOP))

        content.addView(buildHeader(), LinearLayout.LayoutParams(MATCH, WRAP))
        content.addView(
            bodyText("导入 Mod 后回到这里启停；开关只影响下次启动，游戏运行中不会热重载。", 12.5f, Palette.TextSecondary).apply {
                setLineSpacing(dpf(4f), 1.2f)
            },
            topMargin(14, MATCH, WRAP),
        )

        importButton = ActionButton(this, ActionButton.Kind.PRIMARY).apply {
            text = "导入 Mod 文件"
            setIcon(R.drawable.ic_download)
            setOnClickListener { pickFile() }
        }
        content.addView(importButton, LinearLayout.LayoutParams(MATCH, dp(58)).apply { topMargin = dp(22) })

        folderButton = ActionButton(this, ActionButton.Kind.GHOST).apply {
            text = "导入 Mod 文件夹"
            setIcon(R.drawable.ic_folder)
            setOnClickListener { pickFolder() }
        }
        content.addView(folderButton, LinearLayout.LayoutParams(MATCH, dp(50)).apply { topMargin = dp(10) })

        packButton = ActionButton(this, ActionButton.Kind.GHOST).apply {
            text = "打包为 .pmod"
            setIcon(R.drawable.ic_archive)
            setOnClickListener { pickPackSource() }
        }
        content.addView(packButton, LinearLayout.LayoutParams(MATCH, dp(50)).apply { topMargin = dp(10) })

        content.addView(
            bodyText(
                "支持 .dll / .zip / .pmod，或包含 mod.dll 的整个文件夹。" +
                    "打包会把文件夹收成单个 .pmod，宿主启动时解到内部缓存再加载；没有图标会自动生成占位图。",
                10.5f,
                Palette.TextTertiary,
            ).apply { setLineSpacing(dpf(3f), 1.25f) },
            topMargin(10, MATCH, WRAP),
        )

        val card = PylonCard(this)
        val column = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        card.addView(column, FrameLayout.LayoutParams(MATCH, WRAP))
        column.addView(
            bodyText("已安装的 Mod", 12f, Palette.TextPrimary, Fonts.medium),
            LinearLayout.LayoutParams(MATCH, WRAP),
        )
        modsList = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        column.addView(modsList, LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(10) })
        content.addView(card, LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(20) })

        content.addView(Space(this), LinearLayout.LayoutParams(1, 0, 1f))
        content.addView(buildFooter(), LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(22) })

        snackbar = SnackbarView(this)
        root.addView(
            snackbar,
            FrameLayout.LayoutParams(MATCH, WRAP, Gravity.BOTTOM).apply {
                leftMargin = dp(16)
                rightMargin = dp(16)
            },
        )

        buildOverlay()
        root.addView(overlay, FrameLayout.LayoutParams(MATCH, MATCH))
        installInsets()
    }

    private fun buildHeader(): View {
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }

        val back = ImageView(this).apply {
            setImageResource(R.drawable.ic_arrow_back)
            setColorFilter(Palette.TextPrimary)
            contentDescription = "返回"
            isClickable = true
            isFocusable = true
            background = rippleCircle(Palette.SurfaceInput, Palette.Stroke)
            setOnClickListener { finish() }
        }
        row.addView(back, LinearLayout.LayoutParams(dp(42), dp(42)))

        val title = bodyText("Mod 管理", 20f, Palette.TextPrimary, Fonts.medium).apply {
            setPadding(dp(14), 0, 0, 0)
        }
        row.addView(title, LinearLayout.LayoutParams(0, WRAP, 1f))

        countBadge = pill("", Palette.TextTertiary, Palette.SurfaceInput, Palette.StrokeSoft)
        row.addView(countBadge)
        return row
    }

    private fun buildFooter(): View {
        val column = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        column.addView(
            View(this).apply { setBackgroundColor(Palette.StrokeSoft) },
            LinearLayout.LayoutParams(MATCH, dp(1)),
        )
        column.addView(
            bodyText(ModStorage.directory(this).absolutePath, 9.5f, Palette.TextSecondary, Fonts.mono).apply {
                setTextIsSelectable(true)
                setLineSpacing(dpf(2f), 1.2f)
            },
            topMargin(12, MATCH, WRAP),
        )
        val actions = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        actions.addView(
            bodyText("Mod 目录", 10f, Palette.TextTertiary).apply { letterSpacing = 0.08f },
            LinearLayout.LayoutParams(0, WRAP, 1f),
        )
        actions.addView(link("复制路径", Palette.Accent) { copyPath() })
        column.addView(actions, LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(4) })
        return column
    }

    private fun buildOverlay() {
        overlay = FrameLayout(this).apply {
            visibility = View.GONE
            isClickable = true
            isFocusable = true
            setBackgroundColor(Color.argb(232, 5, 7, 12))
        }
        val card = PylonCard(this).apply {
            layoutParams = FrameLayout.LayoutParams(MATCH, WRAP, Gravity.CENTER).apply {
                leftMargin = dp(6)
                rightMargin = dp(6)
            }
            elevation = dpf(16f)
        }
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        overlayProgress = ProgressBar(this, null, android.R.attr.progressBarStyleSmall).apply {
            indeterminateTintList = ColorStateList.valueOf(Palette.Accent)
        }
        row.addView(overlayProgress, LinearLayout.LayoutParams(dp(22), dp(22)))
        overlayLabel = bodyText("正在处理…", 14f, Palette.TextPrimary, Fonts.medium).apply {
            setPadding(dp(10), 0, 0, 0)
        }
        row.addView(overlayLabel, LinearLayout.LayoutParams(0, WRAP, 1f))
        card.addView(row, FrameLayout.LayoutParams(MATCH, WRAP))
        overlay.addView(card)
    }

    private fun installInsets() {
        root.setOnApplyWindowInsetsListener { _, insets ->
            val top: Int
            val bottom: Int
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
                top = bars.top
                bottom = bars.bottom
            } else {
                @Suppress("DEPRECATION")
                top = insets.systemWindowInsetTop
                @Suppress("DEPRECATION")
                bottom = insets.systemWindowInsetBottom
            }
            scroll.setPadding(dp(20), top + dp(16), dp(20), bottom + dp(24))
            overlay.setPadding(dp(24), top + dp(24), dp(24), bottom + dp(24))
            (topScrim.layoutParams as? FrameLayout.LayoutParams)?.height = top + dp(14)
            topScrim.requestLayout()
            (snackbar.layoutParams as? FrameLayout.LayoutParams)?.let {
                it.bottomMargin = bottom + dp(14)
                snackbar.layoutParams = it
            }
            insets
        }
        root.requestApplyInsets()
    }

    // ------------------------------------------------------------------
    // 列表
    // ------------------------------------------------------------------

    private fun refresh() {
        val mods = ModCatalog.scan(modsDir)
        val enabled = mods.count { it.enabled }
        stylePill(
            countBadge,
            if (mods.isEmpty()) "空" else "$enabled/${mods.size} 启用",
            if (enabled > 0) Palette.Success else Palette.TextTertiary,
        )

        modsList.removeAllViews()
        if (mods.isEmpty()) {
            modsList.addView(
                bodyText("还没有 Mod。点上面的按钮导入第一只。", 11.5f, Palette.TextTertiary),
                LinearLayout.LayoutParams(MATCH, WRAP),
            )
            return
        }

        mods.forEachIndexed { index, mod ->
            if (index > 0) {
                modsList.addView(
                    View(this).apply { setBackgroundColor(Palette.StrokeSoft) },
                    LinearLayout.LayoutParams(MATCH, dp(1)).apply {
                        topMargin = dp(2)
                        bottomMargin = dp(2)
                    },
                )
            }
            modsList.addView(modRow(mod))
        }
    }

    private fun modRow(mod: ModInfo): LinearLayout {
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, dp(9), 0, dp(9))
            layoutParams = LinearLayout.LayoutParams(MATCH, WRAP)
        }

        val iconTile = FrameLayout(this).apply {
            background = GradientDrawable().apply {
                cornerRadius = dpf(11f)
                setColor(Palette.SurfaceInput)
                setStroke(dp(1), Palette.StrokeSoft)
            }
            clipToOutline = true
        }
        val icon = ImageView(this).apply {
            if (mod.icon != null) {
                setImageBitmap(mod.icon)
                scaleType = ImageView.ScaleType.CENTER_CROP
            } else {
                setImageResource(R.drawable.ic_folder)
                setColorFilter(if (mod.enabled) Palette.TextSecondary else Palette.TextTertiary)
                scaleType = ImageView.ScaleType.CENTER_INSIDE
                setPadding(dp(9), dp(9), dp(9), dp(9))
            }
        }
        iconTile.addView(icon, FrameLayout.LayoutParams(MATCH, MATCH))
        row.addView(iconTile, LinearLayout.LayoutParams(dp(38), dp(38)))

        val info = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12), 0, dp(8), 0)
            alpha = if (mod.enabled) 1f else 0.55f
        }
        info.addView(
            bodyText(mod.name.ifBlank { mod.id }, 12.5f, Palette.TextPrimary, Fonts.medium).apply {
                maxLines = 1
                ellipsize = android.text.TextUtils.TruncateAt.END
            },
            LinearLayout.LayoutParams(MATCH, WRAP),
        )
        val meta = buildString {
            append("v${mod.version} · ${mod.source}")
            if (mod.kind == ModKind.PACKAGE) append(" · 单文件")
            if (mod.error != null) append(" · ${mod.error}")
        }
        info.addView(
            bodyText(meta, 9.5f, if (mod.error != null) Palette.Danger else Palette.TextTertiary, Fonts.mono).apply {
                maxLines = 1
                ellipsize = android.text.TextUtils.TruncateAt.MIDDLE
                setPadding(0, dp(3), 0, 0)
            },
            LinearLayout.LayoutParams(MATCH, WRAP),
        )
        row.addView(info, LinearLayout.LayoutParams(0, WRAP, 1f))

        val toggle = ToggleSwitch(this).apply {
            setChecked(mod.enabled, animate = false)
            contentDescription = "${if (mod.enabled) "停用" else "启用"} ${mod.name.ifBlank { mod.id }}"
            onCheckedChange = { checked -> setEnabled(mod, checked) }
        }
        row.addView(toggle, LinearLayout.LayoutParams(dp(46), dp(28)))

        val delete = ImageView(this).apply {
            setImageResource(R.drawable.ic_trash)
            setColorFilter(Palette.Danger)
            contentDescription = "删除 ${mod.name.ifBlank { mod.id }}"
            isClickable = true
            isFocusable = true
            background = rippleCircle(Color.TRANSPARENT, Color.TRANSPARENT)
            setOnClickListener { confirmDelete(mod) }
        }
        row.addView(delete, LinearLayout.LayoutParams(dp(40), dp(40)).apply { marginStart = dp(4) })
        return row
    }

    private fun setEnabled(mod: ModInfo, enabled: Boolean) {
        if (busy) return
        runCatching { ModCatalog.setEnabled(modsDir, mod.id, enabled) }
            .onSuccess {
                refresh()
                showInfo("${if (enabled) "已启用" else "已停用"} ${mod.id} · 重启游戏后生效")
            }
            .onFailure { showError("保存选择失败：${it.javaClass.simpleName}") }
    }

    // ------------------------------------------------------------------
    // 导入 / 删除
    // ------------------------------------------------------------------

    private fun pickFile() {
        if (busy) return
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "*/*"
            putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        startActivityForResult(intent, REQUEST_FILE)
    }

    private fun pickFolder() {
        if (busy) return
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).apply {
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        startActivityForResult(intent, REQUEST_TREE)
    }

    private fun pickPackSource() {
        if (busy) return
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).apply {
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        startActivityForResult(intent, REQUEST_PACK_SOURCE)
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != RESULT_OK || data == null) return
        when (requestCode) {
            REQUEST_FILE -> {
                val uris = mutableListOf<Uri>()
                data.data?.let { uris += it }
                data.clipData?.let { clip ->
                    for (i in 0 until clip.itemCount) uris += clip.getItemAt(i).uri
                }
                if (uris.isNotEmpty()) importDocuments(uris)
            }
            REQUEST_TREE -> data.data?.let { importTree(it) }
            REQUEST_PACK_SOURCE -> data.data?.let { packTree(it) }
        }
    }

    private fun importDocuments(uris: List<Uri>) {
        if (busy) return
        showBusy("正在导入…")
        val appContext = applicationContext
        Thread({
            val results = uris.map { uri -> ModImporter.importDocument(appContext, modsDir, uri) }
            runOnUiThread {
                if (isFinishing || isDestroyed) return@runOnUiThread
                hideBusy()
                refresh()
                val failures = results.filterNot { it.success }
                if (failures.isEmpty()) {
                    showSuccess("已导入 ${results.size} 个 Mod")
                } else {
                    showError(
                        "导入完成：成功 ${results.size - failures.size}，失败 ${failures.size}" +
                            "（${failures.first().message}）",
                    )
                }
            }
        }, "pylon-mod-import").start()
    }

    private fun importTree(treeUri: Uri) {
        if (busy) return
        showBusy("正在导入文件夹…")
        val appContext = applicationContext
        Thread({
            val result = ModImporter.importTree(appContext, modsDir, treeUri)
            runOnUiThread {
                if (isFinishing || isDestroyed) return@runOnUiThread
                hideBusy()
                refresh()
                if (result.success) showSuccess(result.message) else showError(result.message)
            }
        }, "pylon-mod-import-tree").start()
    }

    private fun packTree(treeUri: Uri) {
        if (busy) return
        showBusy("正在打包…")
        val appContext = applicationContext
        Thread({
            val result = ModPackager.pack(appContext, treeUri, modsDir)
            runOnUiThread {
                if (isFinishing || isDestroyed) return@runOnUiThread
                hideBusy()
                refresh()
                if (result.success) showSuccess(result.message) else showError(result.message)
            }
        }, "pylon-mod-pack").start()
    }

    private fun confirmDelete(mod: ModInfo) {
        if (busy) return
        val target = when (mod.kind) {
            ModKind.PACKAGE -> mod.packageFile
            ModKind.LOOSE_DLL -> mod.dll
            ModKind.FOLDER -> mod.dll?.parentFile
        }
        PylonDialog(
            host = this,
            title = "删除 ${mod.id}？",
            message = "会删除 ${target?.absolutePath}，不可恢复。",
            positiveLabel = "删除",
            positiveColor = Palette.Danger,
            onPositive = { delete(mod) },
        ).show()
    }

    private fun delete(mod: ModInfo) {
        showBusy("正在删除…")
        val appContext = applicationContext
        Thread({
            val ok = ModCatalog.delete(modsDir, mod)
            runOnUiThread {
                if (isFinishing || isDestroyed) return@runOnUiThread
                hideBusy()
                refresh()
                if (ok) showInfo("已删除 ${mod.id}") else showError("删除失败：${mod.id}（检查文件权限）")
            }
        }, "pylon-mod-delete").start()
    }

    private fun copyPath() {
        val clipboard = getSystemService(android.content.ClipboardManager::class.java)
        clipboard?.setPrimaryClip(
            android.content.ClipData.newPlainText("Pylon Mods", ModStorage.directory(this).absolutePath),
        )
        showInfo("Mod 目录路径已复制")
    }

    private fun showBusy(label: String) {
        busy = true
        importButton.isEnabled = false
        folderButton.isEnabled = false
        packButton.isEnabled = false
        overlayLabel.text = label
        overlay.visibility = View.VISIBLE
        overlay.alpha = 0f
        overlay.animate().alpha(1f).setDuration(160L).start()
    }

    private fun hideBusy() {
        busy = false
        importButton.isEnabled = true
        folderButton.isEnabled = true
        packButton.isEnabled = true
        overlay.animate()
            .alpha(0f)
            .setDuration(160L)
            .withEndAction { overlay.visibility = View.GONE }
            .start()
    }

    private fun showSuccess(message: String) = snackbar.show(message, SnackbarView.Kind.SUCCESS)
    private fun showError(message: String) = snackbar.show(message, SnackbarView.Kind.ERROR, 7_000L)
    private fun showInfo(message: String) = snackbar.show(message, SnackbarView.Kind.INFO)

    // ------------------------------------------------------------------
    // 小部件
    // ------------------------------------------------------------------

    private fun pill(text: String, textColor: Int, bgColor: Int, border: Int): TextView =
        bodyText(text, 9.5f, textColor, Fonts.medium).apply {
            letterSpacing = 0.03f
            gravity = Gravity.CENTER
            setPadding(dp(10), dp(5), dp(10), dp(5))
            background = GradientDrawable().apply {
                cornerRadius = dpf(100f)
                setColor(bgColor)
                setStroke(dp(1), border)
            }
        }

    private fun stylePill(view: TextView, text: String, color: Int) {
        view.text = text
        view.setTextColor(color)
        view.background = GradientDrawable().apply {
            cornerRadius = dpf(100f)
            setColor(color.withAlpha(0.14f))
            setStroke(dp(1), color.withAlpha(0.34f))
        }
    }

    private fun rippleCircle(fill: Int, border: Int): RippleDrawable {
        val radius = dpf(100f)
        val content = GradientDrawable().apply {
            setColor(fill)
            cornerRadius = radius
            if (border != Color.TRANSPARENT) setStroke(dp(1), border)
        }
        val mask = GradientDrawable().apply {
            setColor(Color.WHITE)
            cornerRadius = radius
        }
        return RippleDrawable(ColorStateList.valueOf(Palette.TextPrimary.withAlpha(0.12f)), content, mask)
    }

    private fun link(text: String, color: Int, onClick: () -> Unit): TextView =
        bodyText(text, 11.5f, color, Fonts.medium).apply {
            isClickable = true
            isFocusable = true
            setPadding(0, dp(6), 0, dp(6))
            setOnClickListener { onClick() }
        }

    private fun topMargin(dp: Int, width: Int, height: Int): LinearLayout.LayoutParams =
        LinearLayout.LayoutParams(width, height).apply { topMargin = this@ModsActivity.dp(dp) }

    private companion object {
        const val MATCH = LinearLayout.LayoutParams.MATCH_PARENT
        const val WRAP = LinearLayout.LayoutParams.WRAP_CONTENT
        const val REQUEST_FILE = 1001
        const val REQUEST_TREE = 1002
        const val REQUEST_PACK_SOURCE = 1003
    }
}
