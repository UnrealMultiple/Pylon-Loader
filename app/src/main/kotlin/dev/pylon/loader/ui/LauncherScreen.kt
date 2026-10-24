package dev.pylon.loader.ui

import android.app.Activity
import android.content.Context
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.text.TextUtils
import android.transition.AutoTransition
import android.transition.TransitionManager
import android.view.Gravity
import android.view.View
import android.view.WindowInsets
import android.view.animation.DecelerateInterpolator
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.ScrollView
import android.widget.Space
import android.widget.TextView
import dev.pylon.loader.BuildConfig
import dev.pylon.loader.FileCheck
import dev.pylon.loader.LauncherState
import dev.pylon.loader.R
import dev.pylon.loader.common.RuntimeLayout
import kotlin.math.roundToInt

/**
 * 层：启动器 UI / 页面
 *
 * 一屏完成"看状态 → 导入 → 启动 → 排障"：品牌区 / 价值主张 / 运行时状态卡（可展开校验详情）/
 * 崩溃日志卡 / 主操作 + 次级操作 / 免责声明。
 *
 * 页面只负责渲染与交互转发；读文件、起线程、弹对话框都在 MainActivity，
 * 这样刷新状态不必重建视图树，导入中动画也不会被打断。
 */
internal class LauncherScreen(
    private val host: Activity,
    private val listener: Listener,
) {

    interface Listener {
        fun onPrimaryAction()
        fun onChooseGame()
        fun onReimport()
        fun onCopyDiagnostics()
        fun onOpenMods()
        fun onViewCrash()
        fun onClearCrash()
    }

    private val context: Context get() = host

    val root: FrameLayout = FrameLayout(host)

    private val scroll = ScrollView(host)
    private val content = LinearLayout(host)
    private lateinit var topScrim: View
    private lateinit var headerView: View
    private lateinit var heroView: View
    private lateinit var actionsView: View
    private lateinit var footerView: View
    private lateinit var targetChip: TextView

    private lateinit var statusCard: PylonCard
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

    private lateinit var crashCard: PylonCard

    private lateinit var modsCard: PylonCard
    private lateinit var modsCountBadge: TextView
    private lateinit var modsPathText: TextView
    private lateinit var modsHint: TextView

    private lateinit var primaryButton: ActionButton
    private lateinit var reimportButton: ActionButton
    private lateinit var diagnosticsButton: ActionButton
    private lateinit var secondaryGap: Space

    private lateinit var snackbar: SnackbarView
    private lateinit var overlay: FrameLayout
    private lateinit var progressBar: ImportProgressBar
    private lateinit var progressStage: TextView
    private lateinit var progressPercent: TextView

    private var detailsExpanded = false
    private var busy = false
    private var lastState: LauncherState? = null
    private var importShownAt = 0L
    private var pendingHide: Runnable? = null

    init {
        build()
    }

    // ------------------------------------------------------------------
    // 视图搭建
    // ------------------------------------------------------------------

    private fun build() {
        root.setBackgroundColor(Palette.BackgroundBottom)
        root.addView(AmbientBackdrop(host), FrameLayout.LayoutParams(MATCH, MATCH))

        scroll.isVerticalScrollBarEnabled = false
        scroll.clipToPadding = false
        scroll.isFillViewport = true
        content.orientation = LinearLayout.VERTICAL
        scroll.addView(content, FrameLayout.LayoutParams(MATCH, WRAP))
        root.addView(scroll, FrameLayout.LayoutParams(MATCH, MATCH))

        // 内容会滚到状态栏下面；垫一条同色渐隐带保证状态栏图标可读，高度由 insets 回调按刘海/状态栏调整。
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
        val status = buildStatusCard()
        buildCrashCard()
        actionsView = buildActions()
        modsCard = buildModsCard()
        footerView = buildFooter()

        content.addView(headerView, LinearLayout.LayoutParams(MATCH, WRAP))
        content.addView(heroView, topMargin(28))
        content.addView(status, topMargin(22))
        content.addView(crashCard, topMargin(14))
        content.addView(actionsView, topMargin(26))
        content.addView(modsCard, topMargin(18))
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

        buildOverlay()
        root.addView(overlay, FrameLayout.LayoutParams(MATCH, MATCH))
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
                "从你已安装的正版导入游戏运行时，一键启动 Mod。\n不改 APK、无需 root、不用悬浮窗权限。",
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
        targetChip = chip(
            text = "选择游戏 ▾",
            textColor = Palette.TextSecondary,
            bgColor = Palette.SurfaceInput,
            border = Palette.Stroke,
        ).apply {
            isClickable = true
            isFocusable = true
            maxWidth = context.dp(220)
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            contentDescription = "选择已安装的游戏"
            setOnClickListener { listener.onChooseGame() }
        }
        chips.addView(targetChip)
        chips.addView(
            chip(
                text = "arm64-v8a",
                textColor = Palette.TextSecondary,
                bgColor = Palette.SurfaceInput,
                border = Palette.Stroke,
            ),
            LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = context.dp(8) },
        )
        column.addView(chips)

        return column
    }

    private fun buildStatusCard(): View {
        statusCard = PylonCard(host)
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        statusCard.addView(column, FrameLayout.LayoutParams(MATCH, WRAP))

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

        return statusCard
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

    private fun buildModsCard(): PylonCard {
        val card = PylonCard(host)
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        card.addView(column, FrameLayout.LayoutParams(MATCH, WRAP))

        val top = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        top.addView(
            ImageView(host).apply {
                setImageResource(R.drawable.ic_folder)
                setColorFilter(Palette.TextSecondary)
            },
            LinearLayout.LayoutParams(context.dp(16), context.dp(16)),
        )
        top.addView(
            uiText("Mod 管理", 13f, Palette.TextPrimary, Fonts.medium).apply {
                setPadding(context.dp(8), 0, 0, 0)
            },
            LinearLayout.LayoutParams(0, WRAP, 1f),
        )
        modsCountBadge = chip("", Palette.TextTertiary, Palette.SurfaceInput, Palette.StrokeSoft, 9.5f)
        top.addView(modsCountBadge)
        column.addView(top, LinearLayout.LayoutParams(MATCH, WRAP))

        modsPathText = uiText("", 9.5f, Palette.TextSecondary, Fonts.mono).apply {
            setTextIsSelectable(true)
            setLineSpacing(context.dpf(2f), 1.2f)
        }
        column.addView(modsPathText, topMargin(10))

        val bottom = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        modsHint = uiText("", 10.5f, Palette.TextTertiary).apply {
            setLineSpacing(context.dpf(2f), 1.2f)
        }
        bottom.addView(modsHint, LinearLayout.LayoutParams(0, WRAP, 1f))
        bottom.addView(linkText("打开 Mod 管理 ›", Palette.Accent) { listener.onOpenMods() })
        column.addView(bottom, topMargin(8))

        card.isClickable = true
        card.isFocusable = true
        card.contentDescription = "打开 Mod 管理"
        card.setOnClickListener { listener.onOpenMods() }
        return card
    }

    private fun buildActions(): View {
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }

        primaryButton = ActionButton(host, ActionButton.Kind.PRIMARY).apply {
            setOnClickListener { listener.onPrimaryAction() }
        }
        column.addView(primaryButton, LinearLayout.LayoutParams(MATCH, context.dp(58)))

        val secondary = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        reimportButton = secondaryButton("重新导入", R.drawable.ic_refresh) { listener.onReimport() }
        diagnosticsButton = secondaryButton("复制诊断", R.drawable.ic_copy) { listener.onCopyDiagnostics() }
        secondaryGap = Space(host).apply {
            layoutParams = LinearLayout.LayoutParams(context.dp(10), 1)
        }
        secondary.addView(reimportButton, LinearLayout.LayoutParams(0, context.dp(50), 1f))
        secondary.addView(secondaryGap)
        secondary.addView(diagnosticsButton, LinearLayout.LayoutParams(0, context.dp(50), 1f))
        column.addView(secondary, topMargin(10))

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
                "本工具不提供游戏文件，请使用自己购买的正版游戏。\nPylon Loader 与 Re-Logic / 505 Games 无关联。",
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
                "v${BuildConfig.VERSION_NAME} · arm64-v8a",
                9.5f,
                Palette.TextTertiary.withAlpha(0.8f),
            ).apply { gravity = Gravity.CENTER },
            topMargin(10),
        )
        return column
    }

    private fun buildOverlay() {
        overlay = FrameLayout(host).apply {
            visibility = View.GONE
            isClickable = true
            isFocusable = true
            setBackgroundColor(Color.argb(232, 5, 7, 12))
            setOnClickListener { /* 吃掉点击：导入过程中不允许操作底下页面 */ }
        }

        val card = PylonCard(host).apply {
            layoutParams = FrameLayout.LayoutParams(MATCH, WRAP, Gravity.CENTER).apply {
                leftMargin = context.dp(6)
                rightMargin = context.dp(6)
            }
            elevation = context.dpf(16f)
        }
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }

        val titleRow = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        titleRow.addView(
            ProgressBar(host, null, android.R.attr.progressBarStyleSmall).apply {
                indeterminateTintList = ColorStateList.valueOf(Palette.Accent)
            },
            LinearLayout.LayoutParams(context.dp(22), context.dp(22)),
        )
        titleRow.addView(
            uiText("正在导入运行时", 15f, Palette.TextPrimary, Fonts.medium).apply {
                setPadding(context.dp(10), 0, 0, 0)
            },
        )
        column.addView(titleRow)

        progressBar = ImportProgressBar(host)
        column.addView(progressBar, LinearLayout.LayoutParams(MATCH, context.dp(8)).apply {
            topMargin = context.dp(20)
        })

        val progressRow = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        progressStage = uiText("准备导入…", 11.5f, Palette.TextSecondary).apply {
            maxLines = 2
            setLineSpacing(context.dpf(2f), 1.15f)
        }
        progressRow.addView(progressStage, LinearLayout.LayoutParams(0, WRAP, 1f))
        progressPercent = uiText("", 14f, Palette.Accent, Fonts.medium).apply {
            setPadding(context.dp(10), 0, 0, 0)
        }
        progressRow.addView(progressPercent)
        column.addView(progressRow, topMargin(10))

        column.addView(
            uiText("约 175 MB · 请保持应用在前台，不要锁屏或退出", 10f, Palette.TextTertiary),
            topMargin(12),
        )

        card.addView(column, FrameLayout.LayoutParams(MATCH, WRAP))
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

            scroll.setPadding(
                context.dp(20),
                top + context.dp(18),
                context.dp(20),
                bottom + context.dp(28),
            )
            overlay.setPadding(
                context.dp(24),
                top + context.dp(24),
                context.dp(24),
                bottom + context.dp(24),
            )
            (snackbar.layoutParams as? FrameLayout.LayoutParams)?.let {
                it.bottomMargin = bottom + context.dp(14)
                snackbar.layoutParams = it
            }
            (topScrim.layoutParams as? FrameLayout.LayoutParams)?.height = top + context.dp(14)
            topScrim.requestLayout()
            insets
        }
        root.requestApplyInsets()
    }

    // ------------------------------------------------------------------
    // 渲染
    // ------------------------------------------------------------------

    fun render(state: LauncherState) {
        lastState = state
        renderStatus(state)
        renderDetails(state)
        renderCrash(state)
        renderMods(state)
        renderActions(state)
    }

    private fun renderStatus(state: LauncherState) {
        targetChip.text = if (state.gameInstalled) {
            "${state.gameLabel ?: state.selectedPackage} ▾"
        } else {
            "选择游戏 ▾"
        }

        val copy = statusCopy(state)
        statusTitle.text = copy.title
        statusSubtitle.text = copy.subtitle
        statusDot.configure(state.ready, copy.color)
        styleBadge(copy.badge, copy.color)

        val manifest = state.manifest
        if (manifest == null) {
            statsRow.visibility = View.GONE
            metaText.visibility = View.GONE
        } else {
            statsRow.visibility = View.VISIBLE
            metaText.visibility = View.VISIBLE
            statsRow.removeAllViews()
            statsRow.addView(stat(state.nativeChecks.count { it.matches }.toString(), "原生库"))
            statsRow.addView(statDivider())
            statsRow.addView(stat(manifest.dataFiles.size.toString(), "资源文件"))
            statsRow.addView(statDivider())
            statsRow.addView(stat(formatBytes(manifest.totalBytes), "占用空间"))
            metaText.text = buildString {
                append("Terraria ${manifest.gameVersionName} · ${manifest.abi}")
                append(" · 导入于 ${formatTime(manifest.importedAtEpochMs)}")
                append(" · 清单 ${manifest.nativeLibs.size + manifest.dataFiles.size} 项")
                if (manifest.gamePackage.isNotEmpty() && manifest.gamePackage != state.selectedPackage) {
                    append(" · 来自 ${manifest.gamePackage}")
                }
            }
        }

        val passed = state.checks.count { it.matches }
        val total = state.checks.size
        detailsSummary.text = when {
            state.manifest == null -> "未导入"
            state.failedChecks.isEmpty() -> "全部通过"
            else -> "${total - passed} 项异常"
        }
        detailsSummary.setTextColor(
            when {
                state.manifest == null -> Palette.Warning
                state.failedChecks.isEmpty() -> Palette.Success
                else -> Palette.Warning
            },
        )
    }

    private fun renderDetails(state: LauncherState) {
        detailsContainer.removeAllViews()
        addSection("原生库 · 按加载顺序")
        state.nativeChecks.forEach { addCheckRow(it) }
        addSection("关键资源", topDp = 14)
        state.dataChecks.forEach { addCheckRow(it) }
        addSection("运行时目录", topDp = 18)
        detailsContainer.addView(
            uiText(state.runtimeDir.absolutePath, 10f, Palette.TextSecondary, Fonts.mono).apply {
                setTextIsSelectable(true)
                setLineSpacing(context.dpf(2f), 1.2f)
            },
            topMargin(5),
        )
        detailsContainer.addView(
            uiText("清单记录导入时的文件大小与 SHA-256；这里核对文件是否仍在、大小是否一致。", 10f, Palette.TextTertiary).apply {
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
                setColorFilter(Palette.Warning)
            },
            LinearLayout.LayoutParams(context.dp(16), context.dp(16)),
        )
        top.addView(
            uiText("宿主崩溃日志", 13f, Palette.TextPrimary, Fonts.medium).apply {
                setPadding(context.dp(8), 0, 0, 0)
            },
            LinearLayout.LayoutParams(0, WRAP, 1f),
        )
        top.addView(uiText(formatTime(crash.modifiedAt), 9.5f, Palette.TextTertiary))
        column.addView(top, LinearLayout.LayoutParams(MATCH, WRAP))

        column.addView(
            uiText(
                "${crash.file.name} · ${formatBytes(crash.size)} · 可查看后反馈给开发者。",
                11f,
                Palette.TextSecondary,
            ).apply { setPadding(0, context.dp(8), 0, 0) },
        )

        val actions = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        actions.addView(linkText("查看内容", Palette.Accent) { listener.onViewCrash() })
        actions.addView(
            linkText("清除", Palette.TextSecondary) { listener.onClearCrash() },
            LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = context.dp(18) },
        )
        column.addView(actions, topMargin(10))
    }

    private fun renderMods(state: LauncherState) {
        val total = state.mods.size
        val enabled = state.enabledModCount
        modsPathText.text = state.modsDir.absolutePath

        if (total == 0) {
            stylePill(modsCountBadge, "空", Palette.TextTertiary)
            modsHint.text = "还没有 Mod；打开管理页导入第一只。"
        } else {
            stylePill(
                modsCountBadge,
                "$enabled/$total 启用",
                if (enabled > 0) Palette.Success else Palette.TextTertiary,
            )
            modsHint.text = "导入、启停与删除都在 Mod 管理页；开关重启后生效。"
        }
    }

    private fun renderActions(state: LauncherState) {
        when {
            state.ready -> {
                primaryButton.text = "启动游戏"
                primaryButton.setIcon(R.drawable.ic_play)
                primaryButton.isEnabled = !busy
            }
            !state.gameInstalled -> {
                primaryButton.text = "选择已安装的游戏"
                primaryButton.setIcon(R.drawable.ic_folder)
                primaryButton.isEnabled = !busy
            }
            !state.gameVersionMatches -> {
                primaryButton.text = "选择其他游戏"
                primaryButton.setIcon(R.drawable.ic_folder)
                primaryButton.isEnabled = !busy
            }
            else -> {
                primaryButton.text = "导入运行时"
                primaryButton.setIcon(R.drawable.ic_download)
                primaryButton.isEnabled = !busy
            }
        }
        // 没有清单时"重新导入"没有意义：隐藏它，让"复制诊断"占满整行。
        val hasManifest = state.manifest != null
        reimportButton.visibility = if (hasManifest) View.VISIBLE else View.GONE
        secondaryGap.visibility = if (hasManifest) View.VISIBLE else View.GONE
        reimportButton.isEnabled = state.canImport && !busy
    }

    private fun statusCopy(state: LauncherState): StatusCopy = when {
        !state.gameInstalled -> StatusCopy(
            title = "未选择可用的游戏",
            subtitle = "当前选择 ${state.selectedPackage}，但设备上没有安装。点上方目标或主按钮，从已安装应用里选择游戏。",
            badge = "选择游戏",
            color = Palette.Warning,
        )

        !state.gameVersionMatches -> StatusCopy(
            title = "已选游戏版本不受支持",
            subtitle = "${state.gameLabel ?: state.selectedPackage} 是 " +
                "${state.gameVersionName ?: "未知版本"}（${state.gameVersionCode ?: "?"}）。" +
                "本版本需要 ${RuntimeLayout.GAME_VERSION_NAME}（${RuntimeLayout.GAME_VERSION_CODE}）。",
            badge = "版本不符",
            color = Palette.Warning,
        )

        state.manifest == null -> StatusCopy(
            title = "尚未导入运行时",
            subtitle = "将从 ${state.gameLabel ?: state.selectedPackage} 的安装包解出关键文件并逐文件校验。" +
                "只需一次，约 175 MB。",
            badge = "未就绪",
            color = Palette.Warning,
        )

        !state.manifestMatchesSelection -> StatusCopy(
            title = "运行时来自另一个游戏",
            subtitle = "当前清单是 ${state.manifest?.gamePackage} 的，和已选游戏 ${state.selectedPackage} 不一致，" +
                "请重新导入。",
            badge = "需重导",
            color = Palette.Warning,
        )

        !state.manifestValid -> StatusCopy(
            title = "运行时版本不匹配",
            subtitle = "清单来自 ${state.manifest?.gameVersionName}（${state.manifest?.gameVersionCode}），" +
                "与当前目标版本不一致，请重新导入。",
            badge = "需重导",
            color = Palette.Warning,
        )

        state.failedChecks.isNotEmpty() -> StatusCopy(
            title = "运行时文件不完整",
            subtitle = "有 ${state.failedChecks.size} 个关键文件缺失或大小不符，请重新导入以修复。",
            badge = "需修复",
            color = Palette.Warning,
        )

        else -> StatusCopy(
            title = "运行时已就绪",
            subtitle = "游戏、导入清单与关键文件一致，可以启动。",
            badge = "已就绪",
            color = Palette.Success,
        )
    }

    private data class StatusCopy(
        val title: String,
        val subtitle: String,
        val badge: String,
        val color: Int,
    )

    // ------------------------------------------------------------------
    // 对外动画/反馈
    // ------------------------------------------------------------------

    fun playEntrance() {
        val targets = listOf(headerView, heroView, statusCard, actionsView, footerView)
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
            reimportButton.isEnabled = false
            diagnosticsButton.isEnabled = false
        } else {
            diagnosticsButton.isEnabled = true
            lastState?.let { renderActions(it) }
        }
    }

    fun showImport() {
        pendingHide?.let { overlay.removeCallbacks(it) }
        pendingHide = null
        importShownAt = android.os.SystemClock.uptimeMillis()
        progressBar.setFraction(null)
        progressStage.text = "准备导入…"
        progressPercent.text = "—"
        overlay.animate().cancel()
        overlay.visibility = View.VISIBLE
        overlay.alpha = 0f
        overlay.animate().alpha(1f).setDuration(180L).start()
    }

    fun updateImport(stage: String, fraction: Float?) {
        progressStage.text = stage
        progressPercent.text = fraction?.let { "${(it * 100f).roundToInt()}%" } ?: ""
        progressBar.setFraction(fraction)
    }

    /** 最小展示 700ms：小运行时可能 300ms 就导完，直接消失会像"闪了一下"；剩余时间用 postDelayed 补足。 */
    fun hideImport() {
        val elapsed = android.os.SystemClock.uptimeMillis() - importShownAt
        val remain = (MIN_IMPORT_OVERLAY_MS - elapsed).coerceAtLeast(0L)
        pendingHide?.let { overlay.removeCallbacks(it) }
        val hide = Runnable { hideOverlayNow() }
        pendingHide = hide
        if (remain > 0L) overlay.postDelayed(hide, remain) else hide.run()
    }

    private fun hideOverlayNow() {
        pendingHide = null
        overlay.animate().cancel()
        overlay.animate()
            .alpha(0f)
            .setDuration(180L)
            .withEndAction { overlay.visibility = View.GONE }
            .start()
    }

    fun showSuccess(message: String) = snackbar.show(message, SnackbarView.Kind.SUCCESS)

    fun showError(message: String) =
        snackbar.show(message, SnackbarView.Kind.ERROR, durationMs = 7_000L)

    fun showInfo(message: String) = snackbar.show(message, SnackbarView.Kind.INFO)

    // ------------------------------------------------------------------
    // 小部件
    // ------------------------------------------------------------------

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

    /** 页面内的文字构造入口：统一 typeface 与 includeFontPadding。 */
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

    private fun secondaryButton(
        text: String,
        icon: Int,
        onClick: () -> Unit,
    ): ActionButton = ActionButton(host, ActionButton.Kind.GHOST).apply {
        this.text = text
        setIcon(icon)
        setOnClickListener { onClick() }
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
        const val MIN_IMPORT_OVERLAY_MS = 700L
    }
}
