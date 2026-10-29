package dev.pylon.loader.ui

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.roundToInt

/**
 * 层：启动器 UI / 设计语言
 *
 * 颜色与字号只在这里定义。刻意不引 Material/AppCompat：少一个依赖就少一个"离线构建失败"来源，
 * 视觉规范靠这份调色板 + 自绘控件撑起来。
 * 基调：近黑深蓝底、低饱和描边、青到紫的单一强调渐变；状态色只用于状态本身
 * （绿=就绪、黄=需处理、红=错误），不参与装饰。
 */
internal object Palette {
    val BackgroundTop = Color.rgb(9, 13, 21)
    val BackgroundBottom = Color.rgb(4, 6, 10)

    val SurfaceTop = Color.rgb(18, 25, 37)
    val SurfaceBottom = Color.rgb(11, 17, 25)
    val SurfaceInput = Color.rgb(15, 22, 32)
    val Stroke = Color.rgb(30, 41, 56)
    val StrokeSoft = Color.rgb(23, 32, 45)

    val TextPrimary = Color.rgb(241, 245, 251)
    val TextSecondary = Color.rgb(151, 166, 184)
    val TextTertiary = Color.rgb(99, 115, 135)

    val Accent = Color.rgb(56, 189, 248)
    val AccentBright = Color.rgb(125, 231, 255)
    val AccentViolet = Color.rgb(129, 140, 248)

    val Success = Color.rgb(52, 211, 153)
    val Warning = Color.rgb(251, 191, 36)
    val Danger = Color.rgb(248, 113, 113)

    /** 主按钮的渐变方向是左→右，这是唯一允许使用双色渐变的地方。 */
    val PrimaryGradient = intArrayOf(Accent, AccentViolet)

    val CardGradient = intArrayOf(SurfaceTop, SurfaceBottom)
    val CrashCardGradient = intArrayOf(Color.rgb(27, 17, 21), Color.rgb(17, 12, 16))
}

internal object Fonts {
    val regular: Typeface = Typeface.create("sans-serif", Typeface.NORMAL)
    val medium: Typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    val light: Typeface = Typeface.create("sans-serif-light", Typeface.NORMAL)
    val mono: Typeface = Typeface.create("monospace", Typeface.NORMAL)
}

internal fun Context.dp(value: Int): Int = (value * resources.displayMetrics.density).roundToInt()

internal fun Context.dpf(value: Float): Float = value * resources.displayMetrics.density

/** 保留原色相、只改不透明度；用于"同色系的浅底 + 描边"的圆角块。 */
internal fun Int.withAlpha(alpha: Float): Int {
    val a = (Color.alpha(this) * alpha.coerceIn(0f, 1f)).roundToInt().coerceIn(0, 255)
    return Color.argb(a, Color.red(this), Color.green(this), Color.blue(this))
}

/** 把 overlay 按比例混进不透明的 base：用于"带一点状态色、但必须不透明"的面板（snackbar 要盖住底下文字，不能直接用 alpha 色）。 */
internal fun blend(base: Int, overlay: Int, ratio: Float): Int {
    val t = ratio.coerceIn(0f, 1f)
    fun channel(a: Int, b: Int) = (a * (1f - t) + b * t).roundToInt().coerceIn(0, 255)
    return Color.rgb(
        channel(Color.red(base), Color.red(overlay)),
        channel(Color.green(base), Color.green(overlay)),
        channel(Color.blue(base), Color.blue(overlay)),
    )
}

internal fun formatBytes(bytes: Long): String {
    if (bytes <= 0L) return "0 B"
    return when {
        bytes >= 1L shl 30 -> String.format(Locale.US, "%.2f GB", bytes / 1073741824.0)
        bytes >= 1L shl 20 -> String.format(Locale.US, "%.0f MB", bytes / 1048576.0)
        bytes >= 1024L -> String.format(Locale.US, "%.0f KB", bytes / 1024.0)
        else -> "$bytes B"
    }
}

internal fun formatTime(epochMs: Long): String {
    if (epochMs <= 0L) return "未知"
    return SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.getDefault()).format(Date(epochMs))
}
