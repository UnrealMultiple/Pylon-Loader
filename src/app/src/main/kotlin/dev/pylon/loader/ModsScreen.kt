package dev.pylon.loader

import android.app.Activity
import android.content.Intent
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.net.Uri
import android.os.Build
import android.view.Gravity
import android.view.View
import android.view.WindowInsets
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
 * Mod 管理页：扫描 Mods/、启停、导入 .dll/.zip/文件夹、打包/解包 .pmod、删除。
 * 由 MainActivity 内嵌为底部导航的第二个页面，不再独立持有一个 Activity。
 * 导入走 SAF 无需存储权限；写入先落临时目录，校验主程序集后再换入。
 */
internal class ModsScreen(private val host: Activity) {

    val root: FrameLayout = FrameLayout(host)

    private val context: Activity get() = host
    private val scroll = ScrollView(host)
    private val topScrim: View
    private val importButton: ActionButton
    private val folderButton: ActionButton
    private val packButton: ActionButton
    private lateinit var countBadge: TextView
    private val modsList: LinearLayout
    private val snackbar: SnackbarView
    private val overlay: FrameLayout
    private lateinit var overlayLabel: TextView

    private val modsDir: File by lazy { ModStorage.ensure(host) }
    private var busy = false
    private var extraBottomInset = 0

    // 页面里统一走裸名调用，避免每处都写 context. 前缀。
    private fun dp(value: Int): Int = context.dp(value)
    private fun dpf(value: Float): Float = context.dpf(value)
    private fun bodyText(
        text: CharSequence,
        sizeSp: Float,
        color: Int,
        typeface: Typeface = Fonts.regular,
    ): TextView = context.bodyText(text, sizeSp, color, typeface)

    init {
        root.setBackgroundColor(Palette.BackgroundBottom)
        root.addView(AmbientBackdrop(host), FrameLayout.LayoutParams(MATCH, MATCH))

        scroll.isVerticalScrollBarEnabled = false
        scroll.clipToPadding = false
        scroll.isFillViewport = true
        val content = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(content, FrameLayout.LayoutParams(MATCH, WRAP))
        root.addView(scroll, FrameLayout.LayoutParams(MATCH, MATCH))

        topScrim = View(host).apply {
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

        val card = PylonCard(host)
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        card.addView(column, FrameLayout.LayoutParams(MATCH, WRAP))
        column.addView(
            bodyText("已安装的 Mod", 12f, Palette.TextPrimary, Fonts.medium),
            LinearLayout.LayoutParams(MATCH, WRAP),
        )
        modsList = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        column.addView(modsList, LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(10) })
        content.addView(card, LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(20) })

        importButton = ActionButton(host, ActionButton.Kind.PRIMARY).apply {
            text = "导入 Mod 文件"
            setIcon(R.drawable.ic_download)
            setOnClickListener { pickFile() }
        }
        content.addView(importButton, LinearLayout.LayoutParams(MATCH, dp(58)).apply { topMargin = dp(22) })

        folderButton = ActionButton(host, ActionButton.Kind.GHOST).apply {
            text = "导入 Mod 文件夹"
            setIcon(R.drawable.ic_folder)
            setOnClickListener { pickFolder() }
        }
        content.addView(folderButton, LinearLayout.LayoutParams(MATCH, dp(50)).apply { topMargin = dp(10) })

        packButton = ActionButton(host, ActionButton.Kind.GHOST).apply {
            text = "打包为 .pmod"
            setIcon(R.drawable.ic_archive)
            setOnClickListener { pickPackSource() }
        }
        content.addView(packButton, LinearLayout.LayoutParams(MATCH, dp(50)).apply { topMargin = dp(10) })

        content.addView(
            bodyText(
                "支持 .dll / .zip / .pmod，或包含主程序集的整个文件夹。" +
                    "打包会把文件夹收成单个 .pmod，宿主启动时解到内部缓存再加载；没有图标会自动生成占位图。",
                10.5f,
                Palette.TextTertiary,
            ).apply { setLineSpacing(dpf(3f), 1.25f) },
            topMargin(10, MATCH, WRAP),
        )

        content.addView(Space(host), LinearLayout.LayoutParams(1, 0, 1f))

        snackbar = SnackbarView(host)
        root.addView(
            snackbar,
            FrameLayout.LayoutParams(MATCH, WRAP, Gravity.BOTTOM).apply {
                leftMargin = dp(16)
                rightMargin = dp(16)
            },
        )

        overlay = buildOverlay()
        root.addView(overlay, FrameLayout.LayoutParams(MATCH, MATCH))
        installInsets()
    }

    /** 底部导航栏占位高度，随 insets 到达后由 MainActivity 更新，防止内容被导航栏盖住。 */
    fun setExtraBottomInset(px: Int) {
        extraBottomInset = px
        root.requestApplyInsets()
    }

    fun refresh() {
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
                    View(host).apply { setBackgroundColor(Palette.StrokeSoft) },
                    LinearLayout.LayoutParams(MATCH, dp(1)).apply {
                        topMargin = dp(2)
                        bottomMargin = dp(2)
                    },
                )
            }
            modsList.addView(modRow(mod))
        }
    }

    private fun buildHeader(): View {
        val row = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        row.addView(
            bodyText("Mod 管理", 20f, Palette.TextPrimary, Fonts.medium),
            LinearLayout.LayoutParams(0, WRAP, 1f),
        )
        countBadge = pill("", Palette.TextTertiary, Palette.SurfaceInput, Palette.StrokeSoft)
        row.addView(countBadge)
        return row
    }

    private fun buildOverlay(): FrameLayout {
        val frame = FrameLayout(host).apply {
            visibility = View.GONE
            isClickable = true
            isFocusable = true
            setBackgroundColor(Color.argb(232, 5, 7, 12))
        }
        val card = PylonCard(host).apply {
            layoutParams = FrameLayout.LayoutParams(MATCH, WRAP, Gravity.CENTER).apply {
                leftMargin = dp(6)
                rightMargin = dp(6)
            }
            elevation = dpf(16f)
        }
        val row = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val progress = ProgressBar(host, null, android.R.attr.progressBarStyleSmall).apply {
            indeterminateTintList = ColorStateList.valueOf(Palette.Accent)
        }
        row.addView(progress, LinearLayout.LayoutParams(dp(22), dp(22)))
        overlayLabel = bodyText("正在处理…", 14f, Palette.TextPrimary, Fonts.medium).apply {
            setPadding(dp(10), 0, 0, 0)
        }
        row.addView(overlayLabel, LinearLayout.LayoutParams(0, WRAP, 1f))
        card.addView(row, FrameLayout.LayoutParams(MATCH, WRAP))
        frame.addView(card)
        return frame
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
            scroll.setPadding(dp(20), top + dp(16), dp(20), bottom + dp(24) + extraBottomInset)
            overlay.setPadding(dp(24), top + dp(24), dp(24), bottom + dp(24))
            (topScrim.layoutParams as? FrameLayout.LayoutParams)?.height = top + dp(14)
            topScrim.requestLayout()
            (snackbar.layoutParams as? FrameLayout.LayoutParams)?.let {
                it.bottomMargin = bottom + dp(14) + extraBottomInset
                snackbar.layoutParams = it
            }
            insets
        }
        root.requestApplyInsets()
    }

    private fun modRow(mod: ModInfo): LinearLayout {
        val row = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, dp(9), 0, dp(9))
            layoutParams = LinearLayout.LayoutParams(MATCH, WRAP)
            isClickable = true
            isFocusable = true
            contentDescription = "查看 ${mod.name.ifBlank { mod.id }} 详情"
            setOnClickListener { showModDialog(mod) }
        }

        val iconTile = FrameLayout(host).apply {
            background = GradientDrawable().apply {
                cornerRadius = dpf(11f)
                setColor(Palette.SurfaceInput)
                setStroke(dp(1), Palette.StrokeSoft)
            }
            clipToOutline = true
        }
        val icon = ImageView(host).apply {
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

        val info = LinearLayout(host).apply {
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

        val toggle = ToggleSwitch(host).apply {
            setChecked(mod.enabled, animate = false)
            contentDescription = "${if (mod.enabled) "停用" else "启用"} ${mod.name.ifBlank { mod.id }}"
            onCheckedChange = { checked -> setEnabled(mod, checked) }
        }
        row.addView(toggle, LinearLayout.LayoutParams(dp(46), dp(28)))

        if (mod.kind == ModKind.PACKAGE && mod.error == null) {
            val unpack = ImageView(host).apply {
                setImageResource(R.drawable.ic_archive)
                setColorFilter(Palette.TextSecondary)
                contentDescription = "解包 ${mod.name.ifBlank { mod.id }} 为文件夹"
                isClickable = true
                isFocusable = true
                background = rippleCircle(Color.TRANSPARENT, Color.TRANSPARENT)
                setOnClickListener { confirmUnpack(mod) }
            }
            row.addView(unpack, LinearLayout.LayoutParams(dp(40), dp(40)).apply { marginStart = dp(4) })
        }

        val delete = ImageView(host).apply {
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

    /** Mod 详情对话框：图标 + 基本信息 + 本 Mod 最近的报错/输出（会话日志按来源过滤）。 */
    private fun showModDialog(mod: ModInfo) {
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }

        // 头部：图标 + 名称/版本
        val header = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, 0, 0, dp(10))
        }
        val iconTile = FrameLayout(host).apply {
            background = GradientDrawable().apply {
                cornerRadius = dpf(11f)
                setColor(Palette.SurfaceInput)
                setStroke(dp(1), Palette.StrokeSoft)
            }
            clipToOutline = true
        }
        val icon = ImageView(host).apply {
            if (mod.icon != null) {
                setImageBitmap(mod.icon)
                scaleType = ImageView.ScaleType.CENTER_CROP
            } else {
                setImageResource(R.drawable.ic_folder)
                setColorFilter(Palette.TextSecondary)
                scaleType = ImageView.ScaleType.CENTER_INSIDE
                setPadding(dp(9), dp(9), dp(9), dp(9))
            }
        }
        iconTile.addView(icon, FrameLayout.LayoutParams(MATCH, MATCH))
        header.addView(iconTile, LinearLayout.LayoutParams(dp(46), dp(46)))

        val nameColumn = LinearLayout(host).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12), 0, 0, 0)
        }
        nameColumn.addView(
            bodyText(mod.name.ifBlank { mod.id }, 14f, Palette.TextPrimary, Fonts.medium).apply {
                maxLines = 1
                ellipsize = android.text.TextUtils.TruncateAt.END
            },
            LinearLayout.LayoutParams(MATCH, WRAP),
        )
        nameColumn.addView(
            bodyText("v${mod.version} · ${if (mod.enabled) "已启用" else "已停用"}", 10f, Palette.TextTertiary, Fonts.mono),
            LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(3) },
        )
        header.addView(nameColumn, LinearLayout.LayoutParams(0, WRAP, 1f))
        column.addView(header, LinearLayout.LayoutParams(MATCH, WRAP))

        if (!mod.description.isNullOrBlank()) {
            column.addView(
                bodyText("介绍", 10f, Palette.TextTertiary),
                LinearLayout.LayoutParams(MATCH, WRAP).apply { bottomMargin = dp(2) },
            )
            column.addView(
                bodyText(mod.description, 10.5f, Palette.TextSecondary).apply {
                    setLineSpacing(dpf(2f), 1.2f)
                },
                LinearLayout.LayoutParams(MATCH, WRAP).apply { bottomMargin = dp(6) },
            )
        }

        fun infoRow(label: String, value: CharSequence, mono: Boolean = false) {
            val row = LinearLayout(host).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                setPadding(0, dp(4), 0, dp(4))
            }
            row.addView(
                bodyText(label, 10f, Palette.TextTertiary),
                LinearLayout.LayoutParams(dp(64), WRAP),
            )
            row.addView(
                bodyText(value, 10f, Palette.TextPrimary, if (mono) Fonts.mono else Fonts.regular).apply {
                    setTextIsSelectable(true)
                },
                LinearLayout.LayoutParams(0, WRAP, 1f),
            )
            column.addView(row, LinearLayout.LayoutParams(MATCH, WRAP))
        }

        val name = mod.name.ifBlank { mod.id }
        infoRow("名称", name)
        infoRow("ID", mod.id, mono = true)
        infoRow("版本", "v${mod.version}")
        infoRow("类型", when (mod.kind) {
            ModKind.FOLDER -> "文件夹"
            ModKind.LOOSE_DLL -> "松散 dll"
            ModKind.PACKAGE -> "单文件包（.pmod）"
        })
        infoRow("路径", mod.dll?.absolutePath ?: mod.packageFile?.absolutePath ?: "-", mono = true)
        val size = when (mod.kind) {
            ModKind.LOOSE_DLL -> mod.dll?.length()
            ModKind.PACKAGE -> mod.packageFile?.length()
            ModKind.FOLDER -> mod.dll?.parentFile?.let { dir -> dir.walkBottomUp().filter { it.isFile }.sumOf { it.length() } }
        }
        infoRow("大小", dev.pylon.loader.ui.formatBytes(size ?: 0L))
        infoRow("状态", if (mod.enabled) "已启用" else "已停用")
        mod.error?.let { infoRow("问题", it) }

        val own = runCatching { RunLogs.modRecentLines(host, mod.id) }.getOrNull()
        column.addView(
            View(host).apply { setBackgroundColor(Palette.StrokeSoft) },
            LinearLayout.LayoutParams(MATCH, dp(1)).apply { topMargin = dp(10); bottomMargin = dp(6) },
        )
        column.addView(
            bodyText(
                if (own == null) "最近日志（本 Mod）\n本次会话没有这个 Mod 的输出。"
                else "最近日志（本 Mod，末尾 ${own.size} 行）",
                10f, Palette.TextTertiary,
            ),
            LinearLayout.LayoutParams(MATCH, WRAP),
        )
        val logText = own?.joinToString("\n") ?: "(空)"
        column.addView(
            bodyText(logText, 9f, if (own != null && own.any { it.length > 13 && it[13] == 'E' }) Palette.Danger else Palette.TextSecondary, Fonts.mono).apply {
                setPadding(dp(8), dp(6), dp(8), dp(6))
                background = GradientDrawable().apply {
                    cornerRadius = dpf(10f)
                    setColor(Palette.SurfaceInput)
                    setStroke(dp(1), Palette.StrokeSoft)
                }
            },
            LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(6) },
        )

        val scroller = ScrollView(host).apply {
            addView(column)
            setPadding(dp(12), dp(10), dp(12), dp(10))
            background = GradientDrawable().apply {
                cornerRadius = dpf(12f)
                setColor(Palette.SurfaceInput)
                setStroke(dp(1), Palette.StrokeSoft)
            }
            // 高度按内容估：信息区固定 + 日志行数，封顶 56% 屏高；内容少时对话框收紧，不再撑满。
            val logLines = own?.size ?: 0
            val estimated = dp(400) + logLines * dp(24)
            val maxH = (host.resources.displayMetrics.heightPixels * 0.56f).toInt()
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                minOf(estimated, maxH).coerceAtLeast(dp(280)),
            )
        }

        PylonDialog(
            host = host,
            title = name,
            content = scroller,
            positiveLabel = "关闭",
            negativeLabel = null,
            positiveColor = Palette.TextSecondary,
        ).show()
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

    private fun pickFile() {
        if (busy) return
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "*/*"
            putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        host.startActivityForResult(intent, REQUEST_FILE)
    }

    private fun pickFolder() {
        if (busy) return
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).apply {
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        host.startActivityForResult(intent, REQUEST_TREE)
    }

    private fun pickPackSource() {
        if (busy) return
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).apply {
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        host.startActivityForResult(intent, REQUEST_PACK_SOURCE)
    }

    fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        if (resultCode != Activity.RESULT_OK || data == null) return
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
        val appContext = host.applicationContext
        Thread({
            val results = uris.map { uri -> ModImporter.importDocument(appContext, modsDir, uri) }
            onUiThread {
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
        val appContext = host.applicationContext
        Thread({
            val result = ModImporter.importTree(appContext, modsDir, treeUri)
            onUiThread {
                hideBusy()
                refresh()
                if (result.success) showSuccess(result.message) else showError(result.message)
            }
        }, "pylon-mod-import-tree").start()
    }

    private fun packTree(treeUri: Uri) {
        if (busy) return
        showBusy("正在打包…")
        val appContext = host.applicationContext
        Thread({
            val result = ModPackager.pack(appContext, treeUri, modsDir)
            onUiThread {
                hideBusy()
                refresh()
                if (result.success) showSuccess(result.message) else showError(result.message)
            }
        }, "pylon-mod-pack").start()
    }

    private fun confirmUnpack(mod: ModInfo) {
        if (busy) return
        PylonDialog(
            host = host,
            title = "解包 ${mod.id}？",
            message = "会把 ${mod.source} 展开成 Mods/${mod.id}/ 文件夹并删除原包；之后可直接改文件。重启游戏后生效。",
            positiveLabel = "解包",
            onPositive = { unpack(mod) },
        ).show()
    }

    private fun unpack(mod: ModInfo) {
        showBusy("正在解包…")
        val appContext = host.applicationContext
        Thread({
            val result = try {
                val name = ModCatalog.unpack(modsDir, mod)
                ModImportResult(true, name, "已解包为文件夹：$name（原包已删除）")
            } catch (t: Throwable) {
                ModImportResult(false, mod.id, "解包失败：${t.message}")
            }
            onUiThread {
                hideBusy()
                refresh()
                if (result.success) showSuccess(result.message) else showError(result.message)
            }
        }, "pylon-mod-unpack").start()
    }

    private fun confirmDelete(mod: ModInfo) {
        if (busy) return
        val target = when (mod.kind) {
            ModKind.PACKAGE -> mod.packageFile
            ModKind.LOOSE_DLL -> mod.dll
            ModKind.FOLDER -> mod.dll?.parentFile
        }
        PylonDialog(
            host = host,
            title = "删除 ${mod.id}？",
            message = "会删除 ${target?.absolutePath}，不可恢复。",
            positiveLabel = "删除",
            positiveColor = Palette.Danger,
            onPositive = { delete(mod) },
        ).show()
    }

    private fun delete(mod: ModInfo) {
        showBusy("正在删除…")
        val appContext = host.applicationContext
        Thread({
            val ok = ModCatalog.delete(modsDir, mod)
            onUiThread {
                hideBusy()
                refresh()
                if (ok) showInfo("已删除 ${mod.id}") else showError("删除失败：${mod.id}（检查文件权限）")
            }
        }, "pylon-mod-delete").start()
    }

    private fun copyPath() {
        val clipboard = host.getSystemService(android.content.ClipboardManager::class.java)
        clipboard?.setPrimaryClip(
            android.content.ClipData.newPlainText("Pylon Mods", ModStorage.directory(host).absolutePath),
        )
        showInfo("Mod 目录路径已复制")
    }

    private fun onUiThread(task: () -> Unit) {
        host.runOnUiThread {
            if (host.isFinishing || host.isDestroyed) return@runOnUiThread
            task()
        }
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
        LinearLayout.LayoutParams(width, height).apply { topMargin = this@ModsScreen.context.dp(dp) }

    private companion object {
        const val MATCH = LinearLayout.LayoutParams.MATCH_PARENT
        const val WRAP = LinearLayout.LayoutParams.WRAP_CONTENT
        const val REQUEST_FILE = 1001
        const val REQUEST_TREE = 1002
        const val REQUEST_PACK_SOURCE = 1003
    }
}
