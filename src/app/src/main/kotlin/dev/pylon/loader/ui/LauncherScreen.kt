package dev.pylon.loader.ui

import android.app.Activity
import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.text.TextUtils
import android.transition.AutoTransition
import android.transition.TransitionManager
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.WindowInsets
import android.view.animation.DecelerateInterpolator
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Space
import android.widget.TextView
import dev.pylon.loader.BuildConfig
import dev.pylon.loader.FileCheck
import dev.pylon.loader.LauncherState
import dev.pylon.loader.R
import dev.pylon.loader.common.RuntimeLayout

/**
 * 层：启动器 UI / 主页
 *
 * 一屏完成"看状态 → 启动 → 排障"：品牌区 / 价值主张 / 运行时状态卡（可展开校验详情）/
 * 崩溃日志卡 / 主操作 / Mod 摘要卡 / 免责声明。运行时随构建内置，不再有导入流程。
 *
 * 页面只负责渲染与交互转发；读文件、起线程、弹对话框都在 MainActivity，
 * 这样刷新状态不必重建视图树，动画也不会被打断。
 */
internal class LauncherScreen(
    private val host: Activity,
    private val listener: Listener,
) {

    interface Listener {
        fun onPrimaryAction()
        fun onCopyDiagnostics()
        fun onOpenMods()
        fun onViewCrash()
        fun onCopyCrash()
        fun onClearCrash()
    }

    private val context: Context get() = host

    val root: FrameLayout = FrameLayout(host)

    private val scroll = ScrollView(host)
    private val content = LinearLayout(host)
    private lateinit var topScrim: View
    private lateinit var headerView: View
    private lateinit var heroView: View
    private lateinit var statusCard: PylonCard
    private lateinit var crashCard: PylonCard
    private lateinit var footerView: View

    private lateinit var statusDot: PulseDotView
    private lateinit var statusTitle: TextView
    private lateinit var statusSubtitle: TextView
    private lateinit var statusBadge: TextView
    private lateinit var statsRow: LinearLayout
    private lateinit var metaText: TextView
    private lateinit var detailsToggle: LinearLayout
    private lateinit var detailsSummary: TextView
    private lateinit var detailsChevron: ImageView
    private lateinit var detailsContainer: LinearLayout


    private lateinit var primaryButton: ActionButton
    private lateinit var diagnosticsButton: ActionButton

    private lateinit var snackbar: SnackbarView

    private var detailsExpanded = false
    private var busy = false
    private var lastState: LauncherState? = null
    private var extraBottomInset = 0

    init {
        build()
    }

    private fun build() {
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

        headerView = buildHeader()
        heroView = buildHero()
        statusCard = buildStatusCard()
        buildCrashCard()
        footerView = buildFooter()

        content.addView(headerView, LinearLayout.LayoutParams(MATCH, WRAP))
        content.addView(heroView, topMargin(28))
        content.addView(statusCard, topMargin(22))
        content.addView(crashCard, topMargin(14))
        content.addView(buildActions(), topMargin(26))
        // 把页脚推到底部：内容比屏幕短时不留一块"没画完"的黑色空档。
        content.addView(Space(host), LinearLayout.LayoutParams(1, 0, 1f))
        content.addView(footerView, topMargin(26))

        snackbar = SnackbarView(host)
        root.addView(
            snackbar,
            FrameLayout.LayoutParams(MATCH, WRAP, Gravity.BOTTOM).apply {
                leftMargin = context.dp(16)
                rightMargin = context.dp(16)
            },
        )
        installInsets()
    }

    private fun buildHeader(): View {
        val row = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }

        val tile = FrameLayout(host).apply {
            background = GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM,
                intArrayOf(Palette.Accent.withAlpha(0.18f), Palette.AccentViolet.withAlpha(0.10f)),
            ).apply {
                cornerRadius = context.dpf(14f)
                setStroke(context.dp(1), Palette.Accent.withAlpha(0.26f))
            }
        }
        tile.addView(
            ImageView(host).apply {
                setImageResource(R.drawable.ic_pylon_mark)
                scaleType = ImageView.ScaleType.CENTER_INSIDE
            },
            FrameLayout.LayoutParams(context.dp(28), context.dp(28), Gravity.CENTER),
        )
        row.addView(tile, LinearLayout.LayoutParams(context.dp(46), context.dp(46)))

        val lockup = LinearLayout(host).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(context.dp(12), 0, 0, 0)
        }
        lockup.addView(
            uiText("PYLON", 19f, Palette.TextPrimary, Fonts.medium).apply {
                letterSpacing = 0.26f
            },
        )
        lockup.addView(
            uiText("MOD LOADER", 8.5f, Palette.Accent, Fonts.medium).apply {
                letterSpacing = 0.34f
                setPadding(0, context.dp(2), 0, 0)
            },
        )
        row.addView(lockup, LinearLayout.LayoutParams(WRAP, WRAP))

        row.addView(Space(host), LinearLayout.LayoutParams(0, 1, 1f))
        row.addView(
            chip(
                text = "v${BuildConfig.VERSION_NAME}",
                textColor = Palette.TextTertiary,
                bgColor = Palette.SurfaceInput,
                border = Palette.StrokeSoft,
                sizeSp = 9.5f,
            ),
        )
        return row
    }

    private fun buildHero(): View {
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }

        val overline = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        overline.addView(
            View(host).apply { setBackgroundColor(Palette.Accent.withAlpha(0.6f)) },
            LinearLayout.LayoutParams(context.dp(18), context.dp(1)),
        )
        overline.addView(
            uiText("TERRARIA MOBILE", 9f, Palette.Accent, Fonts.medium).apply {
                letterSpacing = 0.3f
                setPadding(context.dp(8), 0, 0, 0)
            },
        )
        column.addView(overline)

        column.addView(
            uiText("让手机版 Terraria\n跑起你的 Mod", 27f, Palette.TextPrimary, Fonts.medium).apply {
                setLineSpacing(context.dpf(5f), 1.02f)
                setPadding(0, context.dp(12), 0, 0)
            },
        )

        column.addView(
            uiText(
                "游戏运行时已随加载器内置并打过补丁，一键启动 Mod。\n无需 root、不用悬浮窗权限。",
                12.5f,
                Palette.TextSecondary,
            ).apply {
                setLineSpacing(context.dpf(4f), 1.18f)
                setPadding(0, context.dp(10), 0, 0)
            },
        )

        val chips = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, context.dp(14), 0, 0)
        }
        chips.addView(
            chip(
                text = "内置运行时",
                textColor = Palette.Success,
                bgColor = Palette.Success.withAlpha(0.10f),
                border = Palette.Success.withAlpha(0.30f),
            ),
        )
        chips.addView(
            chip(
                text = "Terraria ${RuntimeLayout.GAME_VERSION_LINE} · ${RuntimeLayout.ABI}",
                textColor = Palette.TextSecondary,
                bgColor = Palette.SurfaceInput,
                border = Palette.Stroke,
            ),
            LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = context.dp(8) },
        )
        column.addView(chips)

        return column
    }

    private fun buildStatusCard(): PylonCard {
        val card = PylonCard(host)
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        card.addView(column, FrameLayout.LayoutParams(MATCH, WRAP))

        val top = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        statusDot = PulseDotView(host)
        top.addView(statusDot, LinearLayout.LayoutParams(context.dp(16), context.dp(16)))
        statusTitle = uiText("", 15f, Palette.TextPrimary, Fonts.medium).apply {
            setPadding(context.dp(10), 0, context.dp(8), 0)
        }
        top.addView(statusTitle, LinearLayout.LayoutParams(0, WRAP, 1f))
        statusBadge = chip("", Palette.TextTertiary, Palette.SurfaceInput, Palette.StrokeSoft, 9.5f)
        top.addView(statusBadge)
        column.addView(top, LinearLayout.LayoutParams(MATCH, WRAP))

        statusSubtitle = uiText("", 12.5f, Palette.TextSecondary).apply {
            setLineSpacing(context.dpf(3f), 1.22f)
        }
        column.addView(statusSubtitle, topMargin(9))

        statsRow = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        column.addView(statsRow, topMargin(16))

        metaText = uiText("", 10.5f, Palette.TextTertiary).apply {
            setLineSpacing(context.dpf(2f), 1.25f)
        }
        column.addView(metaText, topMargin(12))

        column.addView(
            View(host).apply { setBackgroundColor(Palette.StrokeSoft) },
            LinearLayout.LayoutParams(MATCH, context.dp(1)).apply { topMargin = context.dp(16) },
        )

        detailsToggle = buildDetailsToggle()
        column.addView(detailsToggle, topMargin(12))

        detailsContainer = LinearLayout(host).apply {
            orientation = LinearLayout.VERTICAL
            visibility = View.GONE
        }
        column.addView(detailsContainer, topMargin(8))

        return card
    }

    private fun buildDetailsToggle(): LinearLayout {
        val row = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            isClickable = true
            isFocusable = true
            minimumHeight = context.dp(44)
            contentDescription = "展开校验详情"
            setOnClickListener { setDetailsExpanded(!detailsExpanded, animate = true) }
        }
        row.addView(
            ImageView(host).apply {
                setImageResource(R.drawable.ic_shield)
                setColorFilter(Palette.TextSecondary)
            },
            LinearLayout.LayoutParams(context.dp(16), context.dp(16)),
        )
        row.addView(
            uiText("校验详情", 12.5f, Palette.TextPrimary, Fonts.medium).apply {
                setPadding(context.dp(8), 0, 0, 0)
            },
        )
        row.addView(Space(host), LinearLayout.LayoutParams(0, 1, 1f))

        detailsSummary = uiText("", 10f, Palette.TextTertiary)
        row.addView(
            detailsSummary,
            LinearLayout.LayoutParams(WRAP, WRAP).apply { marginEnd = context.dp(6) },
        )

        detailsChevron = ImageView(host).apply {
            setImageResource(R.drawable.ic_chevron_down)
            setColorFilter(Palette.TextTertiary)
        }
        row.addView(detailsChevron, LinearLayout.LayoutParams(context.dp(16), context.dp(16)))
        return row
    }

    private fun buildCrashCard() {
        crashCard = PylonCard(
            host,
            fill = Palette.CrashCardGradient,
            border = Palette.Danger.withAlpha(0.28f),
        )
        crashCard.visibility = View.GONE
    }

    private fun buildActions(): View {
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }

        primaryButton = ActionButton(host, ActionButton.Kind.PRIMARY).apply {
            text = "启动游戏"
            setIcon(R.drawable.ic_play)
            setOnClickListener { listener.onPrimaryAction() }
        }
        column.addView(primaryButton, LinearLayout.LayoutParams(MATCH, context.dp(58)))

        diagnosticsButton = ActionButton(host, ActionButton.Kind.GHOST).apply {
            text = "复制诊断"
            setIcon(R.drawable.ic_copy)
            setOnClickListener { listener.onCopyDiagnostics() }
        }
        column.addView(diagnosticsButton, LinearLayout.LayoutParams(MATCH, context.dp(50)).apply { topMargin = context.dp(10) })

        return column
    }

    private fun buildFooter(): View {
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        column.addView(
            View(host).apply { setBackgroundColor(Palette.StrokeSoft) },
            LinearLayout.LayoutParams(MATCH, context.dp(1)),
        )
        column.addView(
            uiText(
                "内置运行时来自开发者自己购买的正版游戏，仅供学习研究。\nPylon Loader 与 Re-Logic / 505 Games 无关联。",
                10.5f,
                Palette.TextTertiary,
            ).apply {
                gravity = Gravity.CENTER
                setLineSpacing(context.dpf(3f), 1.2f)
            },
            topMargin(18),
        )
        column.addView(
            uiText(
                "v${BuildConfig.VERSION_NAME} · ${RuntimeLayout.ABI}",
                9.5f,
                Palette.TextTertiary.withAlpha(0.8f),
            ).apply { gravity = Gravity.CENTER },
            topMargin(10),
        )
        return column
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

            scroll.setPadding(
                context.dp(20),
                top + context.dp(18),
                context.dp(20),
                bottom + context.dp(28) + extraBottomInset,
            )
            (snackbar.layoutParams as? FrameLayout.LayoutParams)?.let {
                it.bottomMargin = bottom + context.dp(14) + extraBottomInset
                snackbar.layoutParams = it
            }
            (topScrim.layoutParams as? FrameLayout.LayoutParams)?.height = top + context.dp(14)
            topScrim.requestLayout()
            insets
        }
        root.requestApplyInsets()
    }

    /** 底部导航栏占位高度，随 insets 到达后由 MainActivity 更新，防止内容被导航栏盖住。 */
    fun setExtraBottomInset(px: Int) {
        extraBottomInset = px
        root.requestApplyInsets()
    }

    fun render(state: LauncherState) {
        lastState = state
        renderStatus(state)
        renderDetails(state)
        renderCrash(state)
        renderActions(state)
    }

    private fun renderStatus(state: LauncherState) {
        val copy = statusCopy(state)
        statusTitle.text = copy.title
        statusSubtitle.text = copy.subtitle
        statusDot.configure(state.ready, copy.color)
        styleBadge(copy.badge, copy.color)

        statsRow.visibility = View.VISIBLE
        metaText.visibility = View.VISIBLE
        statsRow.removeAllViews()
        statsRow.addView(stat(state.nativeChecks.count { it.matches }.toString(), "原生库"))
        statsRow.addView(statDivider())
        statsRow.addView(stat(state.dataChecks.count { it.matches }.toString(), "关键资源"))
        statsRow.addView(statDivider())
        statsRow.addView(stat("${state.enabledModCount}/${state.mods.size}", "Mod 启用"))
        metaText.text = "Terraria ${RuntimeLayout.GAME_VERSION_LINE} · ${RuntimeLayout.ABI} · 随 APK 内置"

        val passed = state.checks.count { it.matches }
        val total = state.checks.size
        detailsSummary.text = when {
            state.failedChecks.isEmpty() -> "全部通过"
            else -> "${total - passed} 项异常"
        }
        detailsSummary.setTextColor(if (state.failedChecks.isEmpty()) Palette.Success else Palette.Warning)
    }

    private fun renderDetails(state: LauncherState) {
        detailsContainer.removeAllViews()
        addSection("原生库 · 按加载顺序")
        state.nativeChecks.forEach { addCheckRow(it) }
        addSection("关键资源（APK 内置 assets）", topDp = 14)
        state.dataChecks.forEach { addCheckRow(it) }
        addSection("Mod 目录", topDp = 18)
        detailsContainer.addView(
            uiText(state.modsDir.absolutePath, 10f, Palette.TextSecondary, Fonts.mono).apply {
                setTextIsSelectable(true)
                setLineSpacing(context.dpf(2f), 1.2f)
            },
            topMargin(5),
        )
        detailsContainer.addView(
            uiText("运行时文件随 APK 分发，这里核对 nativeLibraryDir 与 assets 是否完整。", 10f, Palette.TextTertiary).apply {
                setLineSpacing(context.dpf(2f), 1.25f)
            },
            topMargin(10),
        )
        // 展开状态在状态刷新后要保持。
        detailsContainer.visibility = if (detailsExpanded) View.VISIBLE else View.GONE
    }

    private fun renderCrash(state: LauncherState) {
        val crash = state.crash
        if (crash == null) {
            crashCard.visibility = View.GONE
            crashCard.removeAllViews()
            return
        }
        crashCard.visibility = View.VISIBLE
        crashCard.removeAllViews()

        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        crashCard.addView(column, FrameLayout.LayoutParams(MATCH, WRAP))

        val top = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        top.addView(
            ImageView(host).apply {
                setImageResource(R.drawable.ic_alert)
                setColorFilter(Palette.Danger)
            },
            LinearLayout.LayoutParams(context.dp(16), context.dp(16)),
        )
        top.addView(
            uiText("游戏崩溃了", 13f, Palette.TextPrimary, Fonts.medium).apply {
                setPadding(context.dp(8), 0, context.dp(8), 0)
            },
            LinearLayout.LayoutParams(0, WRAP, 1f),
        )
        top.addView(
            chip(
                text = crash.kind.label,
                textColor = Palette.Danger,
                bgColor = Palette.Danger.withAlpha(0.12f),
                border = Palette.Danger.withAlpha(0.32f),
                sizeSp = 9.5f,
            ),
        )
        top.addView(
            uiText(formatTime(crash.time), 9.5f, Palette.TextTertiary),
            LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = context.dp(8) },
        )
        column.addView(top, LinearLayout.LayoutParams(MATCH, WRAP))

        column.addView(
            uiText(crash.detail, 11.5f, Palette.TextSecondary).apply {
                maxLines = 2
                ellipsize = TextUtils.TruncateAt.END
                setLineSpacing(context.dpf(3f), 1.2f)
            },
            topMargin(9),
        )

        if (crash.preview.isNotBlank()) {
            column.addView(
                uiText(crash.preview, 9.5f, Palette.TextSecondary, Fonts.mono).apply {
                    maxLines = CRASH_PREVIEW_LINES
                    ellipsize = TextUtils.TruncateAt.END
                    setLineSpacing(context.dpf(2f), 1.15f)
                    setTextIsSelectable(true)
                    setPadding(context.dp(10), context.dp(8), context.dp(10), context.dp(8))
                    background = GradientDrawable().apply {
                        cornerRadius = context.dpf(10f)
                        setColor(Palette.SurfaceInput)
                        setStroke(context.dp(1), Palette.StrokeSoft)
                    }
                },
                topMargin(10),
            )
        }

        column.addView(
            uiText(
                "${crash.sources} · ${formatBytes(crash.size)} · 可直接复制反馈给开发者。",
                10f,
                Palette.TextTertiary,
            ),
            topMargin(8),
        )

        val actions = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        actions.addView(linkText("查看完整日志", Palette.Accent) { listener.onViewCrash() })
        actions.addView(
            linkText("复制", Palette.TextSecondary) { listener.onCopyCrash() },
            LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = context.dp(18) },
        )
        actions.addView(
            linkText("清除", Palette.TextTertiary) { listener.onClearCrash() },
            LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = context.dp(18) },
        )
        column.addView(actions, topMargin(6))
    }

    fun scrollToCrash() {
        crashCard.post {
            if (crashCard.visibility != View.VISIBLE) return@post
            scroll.smoothScrollTo(0, (crashCard.top - scroll.paddingTop).coerceAtLeast(0))
        }
    }
    private fun renderActions(state: LauncherState) {
        primaryButton.text = if (state.ready) "启动游戏" else "运行时不完整"
        primaryButton.setIcon(if (state.ready) R.drawable.ic_play else R.drawable.ic_alert)
        primaryButton.isEnabled = !busy
        diagnosticsButton.isEnabled = !busy
    }

    private fun statusCopy(state: LauncherState): StatusCopy = when {
        state.failedChecks.isEmpty() -> StatusCopy(
            title = "运行时已就绪",
            subtitle = "游戏运行时随 APK 内置，关键文件完整，可以启动。",
            badge = "已就绪",
            color = Palette.Success,
        )

        else -> StatusCopy(
            title = "内置运行时不完整",
            subtitle = "有 ${state.failedChecks.size} 个关键文件缺失（${state.failedChecks.first().name}…）。" +
                "这是构建问题：请检查 refs/ 是否按 README 备齐后重新打包 APK。",
            badge = "不完整",
            color = Palette.Warning,
        )
    }

    private data class StatusCopy(
        val title: String,
        val subtitle: String,
        val badge: String,
        val color: Int,
    )

    fun playEntrance() {
        val targets = listOf(headerView, heroView, statusCard, footerView)
        targets.forEachIndexed { index, view ->
            view.alpha = 0f
            view.translationY = context.dpf(14f)
            view.animate()
                .alpha(1f)
                .translationY(0f)
                .setStartDelay(40L + index * 55L)
                .setDuration(420L)
                .setInterpolator(DecelerateInterpolator())
                .start()
        }
    }

    fun setBusy(value: Boolean) {
        busy = value
        if (value) {
            primaryButton.isEnabled = false
            diagnosticsButton.isEnabled = false
        } else {
            lastState?.let { renderActions(it) }
        }
    }

    fun showSuccess(message: String) = snackbar.show(message, SnackbarView.Kind.SUCCESS)

    fun showError(message: String) =
        snackbar.show(message, SnackbarView.Kind.ERROR, durationMs = 7_000L)

    fun showInfo(message: String) = snackbar.show(message, SnackbarView.Kind.INFO)

    private fun setDetailsExpanded(expanded: Boolean, animate: Boolean) {
        detailsExpanded = expanded
        if (animate) {
            TransitionManager.beginDelayedTransition(statusCard, AutoTransition().setDuration(220L))
        }
        detailsContainer.visibility = if (expanded) View.VISIBLE else View.GONE
        detailsToggle.contentDescription = if (expanded) "收起校验详情" else "展开校验详情"
        detailsChevron.animate()
            .rotation(if (expanded) 180f else 0f)
            .setDuration(200L)
            .start()
    }

    private fun addSection(text: String, topDp: Int = 12) {
        detailsContainer.addView(
            uiText(text, 10f, Palette.TextSecondary, Fonts.medium).apply { letterSpacing = 0.10f },
            topMargin(topDp),
        )
    }

    private fun addCheckRow(check: FileCheck) {
        val row = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, context.dp(6), 0, context.dp(6))
            layoutParams = LinearLayout.LayoutParams(MATCH, WRAP)
        }
        row.addView(
            uiText(if (check.matches) "✓" else "✕", 13f, if (check.matches) Palette.Success else Palette.Danger, Fonts.medium).apply {
                gravity = Gravity.CENTER
            },
            LinearLayout.LayoutParams(context.dp(22), WRAP),
        )
        row.addView(
            uiText(check.name, 11.5f, if (check.matches) Palette.TextPrimary else Palette.Danger, Fonts.mono).apply {
                maxLines = 1
                ellipsize = TextUtils.TruncateAt.MIDDLE
            },
            LinearLayout.LayoutParams(0, WRAP, 1f),
        )
        row.addView(
            uiText(check.problem ?: formatBytes(check.size), 10.5f, if (check.matches) Palette.TextSecondary else Palette.Danger),
            LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = context.dp(8) },
        )
        detailsContainer.addView(row)
    }

    private fun stat(value: String, label: String): LinearLayout =
        LinearLayout(host).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            addView(
                uiText(value, 16f, Palette.TextPrimary, Fonts.medium).apply {
                    gravity = Gravity.CENTER
                },
            )
            addView(
                uiText(label, 9.5f, Palette.TextTertiary).apply {
                    letterSpacing = 0.06f
                    gravity = Gravity.CENTER
                    setPadding(0, context.dp(3), 0, 0)
                },
            )
        }.also {
            it.layoutParams = LinearLayout.LayoutParams(0, WRAP, 1f)
        }

    private fun statDivider(): View =
        View(host).apply {
            setBackgroundColor(Palette.StrokeSoft)
            layoutParams = LinearLayout.LayoutParams(context.dp(1), context.dp(26)).apply {
                gravity = Gravity.CENTER_VERTICAL
            }
        }

    private fun uiText(
        content: CharSequence,
        sizeSp: Float,
        color: Int,
        typeface: Typeface = Fonts.regular,
    ): TextView = context.bodyText(content, sizeSp, color, typeface)

    private fun styleBadge(text: String, color: Int) {
        stylePill(statusBadge, text, color)
    }

    private fun stylePill(view: TextView, text: String, color: Int) {
        view.text = text
        view.setTextColor(color)
        view.background = GradientDrawable().apply {
            cornerRadius = context.dpf(100f)
            setColor(color.withAlpha(0.14f))
            setStroke(context.dp(1), color.withAlpha(0.34f))
        }
    }

    private fun chip(
        text: String,
        textColor: Int,
        bgColor: Int,
        border: Int?,
        sizeSp: Float = 10f,
    ): TextView = uiText(text, sizeSp, textColor, Fonts.medium).apply {
        letterSpacing = 0.03f
        gravity = Gravity.CENTER
        setPadding(context.dp(10), context.dp(5), context.dp(10), context.dp(5))
        background = GradientDrawable().apply {
            cornerRadius = context.dpf(100f)
            setColor(bgColor)
            border?.let { setStroke(context.dp(1), it) }
        }
    }

    private fun linkText(text: String, color: Int, onClick: () -> Unit): TextView =
        uiText(text, 11.5f, color, Fonts.medium).apply {
            isClickable = true
            isFocusable = true
            setPadding(0, context.dp(6), context.dp(8), context.dp(6))
            setOnClickListener { onClick() }
        }

    private fun topMargin(topDp: Int): LinearLayout.LayoutParams =
        LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = context.dp(topDp) }

    private companion object {
        const val MATCH = LinearLayout.LayoutParams.MATCH_PARENT
        const val WRAP = LinearLayout.LayoutParams.WRAP_CONTENT
        const val CRASH_PREVIEW_LINES = 8
    }
}
