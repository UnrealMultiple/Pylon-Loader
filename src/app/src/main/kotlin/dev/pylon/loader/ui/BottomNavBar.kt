package dev.pylon.loader.ui

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.util.TypedValue
import android.view.Gravity
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import dev.pylon.loader.R

/**
 * 底部导航栏：主页 / Mod 两个入口。选中项用强调色药丸高亮，未选中次级灰。
 * 只负责外观与选中回调，页面切换由 MainActivity 完成。
 */
internal class BottomNavBar(context: Context, private val onSelect: (Int) -> Unit) : LinearLayout(context) {

    private class Item(
        val root: LinearLayout,
        val icon: ImageView,
        val label: TextView,
    )

    private val items = mutableListOf<Item>()

    var selected: Int = TAB_HOME
        private set

    init {
        orientation = LinearLayout.HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        elevation = context.dpf(10f)
        background = GradientDrawable().apply {
            cornerRadii = floatArrayOf(
                context.dpf(20f).toFloat(), context.dpf(20f).toFloat(),
                context.dpf(20f).toFloat(), context.dpf(20f).toFloat(),
                0f, 0f, 0f, 0f,
            )
            setColor(Palette.SurfaceBottom.withAlpha(0.96f))
            setStroke(context.dp(1), Palette.Stroke)
        }
        addTab(TAB_HOME, "主页", R.drawable.ic_nav_home)
        addTab(TAB_LOG, "日志", R.drawable.ic_info)
        addTab(TAB_MODS, "Mod", R.drawable.ic_nav_mods)
        render()
    }

    fun setSelected(index: Int) {
        if (selected == index) return
        selected = index
        render()
    }

    private fun addTab(index: Int, label: String, iconRes: Int) {
        val item = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            isClickable = true
            isFocusable = true
            setPadding(context.dp(18), context.dp(8), context.dp(18), context.dp(8))
            setOnClickListener {
                selected = index
                render()
                onSelect(index)
            }
        }
        val icon = ImageView(context).apply {
            setImageResource(iconRes)
            importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
        }
        item.addView(icon, LayoutParams(context.dp(20), context.dp(20)))
        val text = TextView(context).apply {
            this.text = label
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 10f)
            includeFontPadding = false
            setPadding(0, context.dp(4), 0, 0)
        }
        item.addView(text, LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT))
        addView(
            item,
            LayoutParams(0, LayoutParams.WRAP_CONTENT, 1f).apply { gravity = Gravity.CENTER },
        )
        items += Item(item, icon, text)
    }

    private fun render() {
        items.forEachIndexed { index, item ->
            val active = index == selected
            val color = if (active) Palette.Accent else Palette.TextTertiary
            item.icon.setColorFilter(color)
            item.label.setTextColor(color)
            item.label.typeface = if (active) Fonts.medium else Fonts.regular
            item.root.background = GradientDrawable().apply {
                cornerRadius = context.dpf(100f)
                setColor(if (active) Palette.Accent.withAlpha(0.13f) else Color.TRANSPARENT)
                if (active) setStroke(context.dp(1), Palette.Accent.withAlpha(0.30f))
            }
        }
    }

    companion object {
        const val TAB_HOME = 0
        const val TAB_LOG = 1
        const val TAB_MODS = 2
    }
}
