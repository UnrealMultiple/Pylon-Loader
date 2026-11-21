package dev.pylon.loader

import android.app.Activity
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.WindowInsets
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import dev.pylon.loader.ui.ActionButton
import dev.pylon.loader.ui.AmbientBackdrop
import dev.pylon.loader.ui.Fonts
import dev.pylon.loader.ui.Palette
import dev.pylon.loader.ui.PylonCard
import dev.pylon.loader.ui.PylonDialog
import dev.pylon.loader.ui.SnackbarView
import dev.pylon.loader.ui.bodyText
import dev.pylon.loader.ui.dp
import dev.pylon.loader.ui.dpf
import dev.pylon.loader.ui.formatTime
import dev.pylon.loader.ui.withAlpha

/**
 * 运行日志页（底部导航第三 Tab）：
 * 一屏读完"上次游戏会话发生了什么"——托管层（框架 + Mod）的错误/警告、Mod 输出、
 * 宿主 boot 轨迹、自进程 logcat 摘录与崩溃记录，报告内联渲染（Bunny Loader 的
 * Diagnostics 卡片模式），不弹窗。写端是 C# SessionLog 与宿主 PylonBootLog。
 */
internal class LogScreen(private val host: Activity) {

    val root: FrameLayout = FrameLayout(host)

    private val scroll = ScrollView(host)
    private val content = LinearLayout(host)
    private val topScrim: View
    private val summaryBadge: TextView
    private val reportView: TextView
    private val emptyText: TextView
    private val reportCard: PylonCard
    private val snackbar: SnackbarView

    private var reportText: String = ""
    private var extraBottomInset = 0

    // 页面里统一走裸名调用，避免每处都写 host. 前缀。
    private fun dp(value: Int): Int = host.dp(value)
    private fun dpf(value: Float): Float = host.dpf(value)
    private fun bodyText(
        text: CharSequence,
        sizeSp: Float,
        color: Int,
        typeface: Typeface = Fonts.regular,
    ): TextView = host.bodyText(text, sizeSp, color, typeface)

    init {
        root.setBackgroundColor(Palette.BackgroundBottom)
        root.addView(AmbientBackdrop(host), FrameLayout.LayoutParams(MATCH, MATCH))

        scroll.isVerticalScrollBarEnabled = false
        scroll.clipToPadding = false
        scroll.isFillViewport = true
        content.orientation = LinearLayout.VERTICAL
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

        val summary = PylonCard(host)
        val summaryColumn = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        summary.addView(summaryColumn, FrameLayout.LayoutParams(MATCH, WRAP))
        val row = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        row.addView(
            bodyText("上次会话", 13f, Palette.TextPrimary, Fonts.medium),
            LinearLayout.LayoutParams(0, WRAP, 1f),
        )
        summaryBadge = pill("", Palette.TextTertiary)
        row.addView(summaryBadge)
        summaryColumn.addView(row, LinearLayout.LayoutParams(MATCH, WRAP))
        summaryColumn.addView(
            bodyText(
                "托管层（框架 + Mod）的错误与警告落在会话日志，宿主另有 boot 轨迹与 logcat 摘录；" +
                    "崩溃记录在主页。出问题把下面的报告复制发给开发者。",
                10.5f,
                Palette.TextTertiary,
            ).apply { setLineSpacing(dpf(3f), 1.25f) },
            topMargin(10),
        )
        content.addView(summary, LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(20) })

        content.addView(
            ActionButton(host, ActionButton.Kind.GHOST).apply {
                text = "刷新"
                setOnClickListener { refresh(showHint = true) }
            },
            LinearLayout.LayoutParams(MATCH, dp(50)).apply { topMargin = dp(14) },
        )

        val actions = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        actions.addView(
            ActionButton(host, ActionButton.Kind.GHOST).apply {
                text = "复制报告"
                setOnClickListener { copyReport() }
            },
            LinearLayout.LayoutParams(0, dp(50), 1f),
        )
        actions.addView(View(host), LinearLayout.LayoutParams(dp(10), 1))
        actions.addView(
            ActionButton(host, ActionButton.Kind.GHOST).apply {
                text = "清除"
                setOnClickListener { confirmClear() }
            },
            LinearLayout.LayoutParams(0, dp(50), 1f),
        )
        content.addView(actions, LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(10) })

        reportCard = PylonCard(host)
        val reportColumn = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        reportCard.addView(reportColumn, FrameLayout.LayoutParams(MATCH, WRAP))
        reportView = TextView(host).apply {
            typeface = Fonts.mono
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 9.5f)
            setTextColor(Palette.TextSecondary)
            setTextIsSelectable(true)
            setLineSpacing(0f, 1.15f)
        }
        reportColumn.addView(reportView, LinearLayout.LayoutParams(MATCH, WRAP))
        content.addView(reportCard, LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(14) })

        emptyText = bodyText(
            "还没有日志。启动一次游戏后，托管层与宿主的输出会落在这里。",
            11.5f,
            Palette.TextTertiary,
        ).apply { gravity = Gravity.CENTER; setLineSpacing(dpf(3f), 1.2f) }
        content.addView(emptyText, LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = dp(28) })

        snackbar = SnackbarView(host)
        root.addView(
            snackbar,
            FrameLayout.LayoutParams(MATCH, WRAP, Gravity.BOTTOM).apply {
                leftMargin = dp(16)
                rightMargin = dp(16)
            },
        )
        installInsets()
        refresh()
    }

    /** 底部导航栏占位高度，随 insets 到达后由 MainActivity 更新。 */
    fun setExtraBottomInset(px: Int) {
        extraBottomInset = px
        root.requestApplyInsets()
    }

    /** 重新读全部日志源并渲染。数据小（各自有上限），主线程直接读。 */
    fun refresh(showHint: Boolean = false) {
        val runLog = RunLogs.inspect(host)
        val session = RunLogs.newestSession(host)
        if (runLog == null && session == null) {
            summaryBadge.text = "无日志"
            stylePill(summaryBadge, "无日志", Palette.TextTertiary)
            reportCard.visibility = View.GONE
            emptyText.visibility = View.VISIBLE
            reportText = ""
            if (showHint) showInfo("还没有会话日志")
            return
        }
        emptyText.visibility = View.GONE
        reportCard.visibility = View.VISIBLE

        val errors = runLog?.errorCount ?: 0
        val warnings = runLog?.warnCount ?: 0
        val color = when {
            errors > 0 -> Palette.Danger
            warnings > 0 -> Palette.Warning
            else -> Palette.Success
        }
        summaryBadge.text = "$errors 错误 · $warnings 警告"
        stylePill(summaryBadge, summaryBadge.text.toString(), color)

        reportText = RunLogs.fullReport(host)
        reportView.text = reportText
        if (showHint) showInfo("已刷新")
    }

    private fun copyReport() {
        val text = if (reportText.isNotBlank()) reportText else RunLogs.fullReport(host)
        if (text.isBlank()) {
            showInfo("没有日志可复制")
            return
        }
        val clipboard = host.getSystemService(android.content.ClipboardManager::class.java)
        clipboard?.setPrimaryClip(android.content.ClipData.newPlainText("Pylon 运行报告", text))
        showInfo("运行报告已复制到剪贴板")
    }

    private fun confirmClear() {
        PylonDialog(
            host = host,
            title = "清除运行日志？",
            message = "会删除会话日志（logs/pylon_*.txt）、boot 轨迹与 logcat 摘录，不影响崩溃记录与 Mod。",
            positiveLabel = "清除",
            positiveColor = Palette.Danger,
            onPositive = {
                if (RunLogs.clear(host)) {
                    refresh()
                    showInfo("已清除运行日志")
                } else {
                    showError("清除失败")
                }
            },
        ).show()
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

    private fun buildHeader(): View {
        val row = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        row.addView(
            bodyText("运行日志", 20f, Palette.TextPrimary, Fonts.medium),
            LinearLayout.LayoutParams(0, WRAP, 1f),
        )
        return row
    }

    private fun pill(text: String, color: Int): TextView =
        bodyText(text, 9.5f, color, Fonts.medium).apply {
            letterSpacing = 0.03f
            gravity = Gravity.CENTER
            setPadding(dp(10), dp(5), dp(10), dp(5))
            background = GradientDrawable().apply {
                cornerRadius = dpf(100f)
                setColor(color.withAlpha(0.14f))
                setStroke(dp(1), color.withAlpha(0.34f))
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

    private fun showError(message: String) = snackbar.show(message, SnackbarView.Kind.ERROR, 7_000L)
    private fun showInfo(message: String) = snackbar.show(message, SnackbarView.Kind.INFO)

    private fun topMargin(dp: Int): LinearLayout.LayoutParams =
        LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = this@LogScreen.host.dp(dp) }

    private companion object {
        const val MATCH = LinearLayout.LayoutParams.MATCH_PARENT
        const val WRAP = LinearLayout.LayoutParams.WRAP_CONTENT
    }
}
