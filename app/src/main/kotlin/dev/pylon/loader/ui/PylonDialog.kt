package dev.pylon.loader.ui

import android.app.Activity
import android.app.Dialog
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.view.Gravity
import android.view.View
import android.view.Window
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.Space
import android.widget.TextView

/**
 * 层：启动器 UI / 对话框
 *
 * 不用平台 AlertDialog：它在 MIUI / 新系统上走厂商浅色样式，与深色界面割裂（真机截图是一块灰底）。
 * 用 PylonCard + 自绘按钮拼同风格深色对话框，内容区可塞任意 View（如崩溃日志滚动文本）。
 */
internal class PylonDialog(
    private val host: Activity,
    private val title: String,
    private val message: CharSequence? = null,
    private val content: View? = null,
    private val positiveLabel: String,
    private val negativeLabel: String? = "取消",
    private val positiveColor: Int = Palette.Accent,
    private val onPositive: () -> Unit = {},
) : Dialog(host) {

    init {
        requestWindowFeature(Window.FEATURE_NO_TITLE)
        setCanceledOnTouchOutside(true)

        val card = PylonCard(host)
        val column = LinearLayout(host).apply { orientation = LinearLayout.VERTICAL }
        card.addView(column, FrameLayout.LayoutParams(MATCH, WRAP))

        column.addView(
            host.bodyText(title, 17f, Palette.TextPrimary, Fonts.medium),
            LinearLayout.LayoutParams(MATCH, WRAP),
        )

        message?.let {
            column.addView(
                host.bodyText(it, 12.5f, Palette.TextSecondary).apply {
                    setLineSpacing(host.dpf(4f), 1.2f)
                },
                LinearLayout.LayoutParams(MATCH, WRAP).apply { topMargin = host.dp(10) },
            )
        }

        content?.let {
            val params = it.layoutParams as? LinearLayout.LayoutParams
                ?: LinearLayout.LayoutParams(MATCH, WRAP)
            params.width = MATCH
            params.topMargin = host.dp(14)
            column.addView(it, params)
        }

        val actions = LinearLayout(host).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, host.dp(18), 0, 0)
        }
        actions.addView(Space(host), LinearLayout.LayoutParams(0, 1, 1f))
        negativeLabel?.let {
            actions.addView(dialogButton(it, Palette.TextSecondary) { dismiss() })
        }
        actions.addView(
            dialogButton(positiveLabel, positiveColor) {
                dismiss()
                onPositive()
            },
            LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = host.dp(6) },
        )
        column.addView(actions, LinearLayout.LayoutParams(MATCH, WRAP))

        setContentView(FrameLayout(host).apply { addView(card, FrameLayout.LayoutParams(MATCH, WRAP)) })

        window?.apply {
            setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
            addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND)
            setDimAmount(0.62f)
            val width = minOf((host.resources.displayMetrics.widthPixels * 0.9f).toInt(), host.dp(420))
            setLayout(width, WindowManager.LayoutParams.WRAP_CONTENT)
            setGravity(Gravity.CENTER)
        }
    }

    private fun dialogButton(label: String, color: Int, onClick: () -> Unit): TextView =
        host.bodyText(label, 14f, color, Fonts.medium).apply {
            gravity = Gravity.CENTER
            setPadding(host.dp(14), host.dp(11), host.dp(14), host.dp(11))
            minimumWidth = host.dp(64)
            isClickable = true
            isFocusable = true
            val radius = host.dpf(13f)
            val mask = GradientDrawable().apply {
                setColor(Color.WHITE)
                cornerRadius = radius
            }
            background = RippleDrawable(ColorStateList.valueOf(color.withAlpha(0.20f)), null, mask)
            setOnClickListener { onClick() }
        }

    private companion object {
        const val MATCH = LinearLayout.LayoutParams.MATCH_PARENT
        const val WRAP = LinearLayout.LayoutParams.WRAP_CONTENT
    }
}
