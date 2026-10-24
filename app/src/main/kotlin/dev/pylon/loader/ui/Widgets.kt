package dev.pylon.loader.ui

import android.animation.ValueAnimator
import android.content.Context
import android.content.res.ColorStateList
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Outline
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewOutlineProvider
import android.view.accessibility.AccessibilityNodeInfo
import android.view.animation.DecelerateInterpolator
import android.view.animation.LinearInterpolator
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import dev.pylon.loader.R

/**
 * 层：启动器 UI / 基础控件
 *
 * 全部是平台 View 的自绘/薄封装，不依赖 Material；控件只负责外观，文案与状态由 LauncherScreen 决定。
 */

/** 22dp 圆角的面板：上亮下暗的微渐变 + 1dp 描边。子 View 由调用方添加。 */
internal class PylonCard @JvmOverloads constructor(
    context: Context,
    fill: IntArray = Palette.CardGradient,
    border: Int = Palette.Stroke,
) : FrameLayout(context) {

    private val radius = context.dpf(22f)

    init {
        setPadding(context.dp(20), context.dp(18), context.dp(20), context.dp(18))
        background = GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, fill).apply {
            cornerRadius = radius
            setStroke(context.dp(1), border)
        }
        elevation = context.dpf(2f)
        outlineProvider = object : ViewOutlineProvider() {
            override fun getOutline(view: View, outline: Outline) {
                outline.setRoundRect(0, 0, view.width, view.height, radius)
            }
        }
    }
}

/**
 * 主/次操作按钮：主按钮渐变胶囊、次按钮描边胶囊，都带平台 ripple。
 * 用 LinearLayout 而非 TextView + compound drawable：后者在整行宽按钮里图标靠左、文字居中，
 * 像两个不相关元素；这里图标和文字作为一个整体居中。
 */
internal class ActionButton @JvmOverloads constructor(
    context: Context,
    private val kind: Kind = Kind.PRIMARY,
) : LinearLayout(context) {

    enum class Kind { PRIMARY, GHOST }

    private val iconView = ImageView(context)
    private val labelView = TextView(context)

    var text: CharSequence
        get() = labelView.text
        set(value) {
            labelView.text = value
            contentDescription = value
        }

    init {
        orientation = HORIZONTAL
        gravity = Gravity.CENTER
        isBaselineAligned = false
        isClickable = true
        isFocusable = true
        setPadding(context.dp(18), 0, context.dp(18), 0)
        minimumWidth = 0
        minimumHeight = context.dp(if (kind == Kind.PRIMARY) 58 else 50)

        iconView.visibility = GONE
        addView(iconView, LayoutParams(context.dp(20), context.dp(20)))

        labelView.apply {
            includeFontPadding = false
            typeface = Fonts.medium
            letterSpacing = 0.01f
            setTextSize(TypedValue.COMPLEX_UNIT_SP, if (kind == Kind.PRIMARY) 16f else 13.5f)
            setTextColor(if (kind == Kind.PRIMARY) Color.WHITE else Palette.TextPrimary)
            isClickable = false
            isFocusable = false
            importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
        }
        addView(
            labelView,
            LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT).apply {
                marginStart = context.dp(9)
            },
        )

        background = buildRipple()
    }

    override fun getAccessibilityClassName(): CharSequence = "android.widget.Button"

    override fun setEnabled(enabled: Boolean) {
        super.setEnabled(enabled)
        alpha = if (enabled) 1f else 0.4f
        isClickable = enabled
    }

    /** 图标统一染色：主按钮白、次按钮次级灰。传 0 表示去掉图标。 */
    fun setIcon(resId: Int) {
        if (resId == 0) {
            iconView.visibility = GONE
            return
        }
        iconView.visibility = VISIBLE
        iconView.setImageResource(resId)
        iconView.setColorFilter(if (kind == Kind.PRIMARY) Color.WHITE else Palette.TextSecondary)
    }

    private fun buildBackground(): GradientDrawable {
        val radius = context.dpf(18f)
        return GradientDrawable(
            if (kind == Kind.PRIMARY) GradientDrawable.Orientation.LEFT_RIGHT
            else GradientDrawable.Orientation.TOP_BOTTOM,
            if (kind == Kind.PRIMARY) Palette.PrimaryGradient
            else intArrayOf(Color.argb(20, 255, 255, 255), Color.argb(10, 255, 255, 255)),
        ).apply {
            cornerRadius = radius
            if (kind == Kind.PRIMARY) {
                setStroke(context.dp(1), Color.argb(46, 255, 255, 255))
            } else {
                setStroke(context.dp(1), Palette.Stroke)
            }
        }
    }

    private fun buildRipple(): RippleDrawable {
        val mask = GradientDrawable().apply {
            setColor(Color.WHITE)
            cornerRadius = context.dpf(18f)
        }
        return RippleDrawable(
            ColorStateList.valueOf(
                if (kind == Kind.PRIMARY) Color.argb(56, 255, 255, 255) else Color.argb(30, 255, 255, 255)
            ),
            buildBackground(),
            mask,
        )
    }
}

/** 状态圆点：就绪时向外扩散一圈光晕，未就绪时是安静的单点。 */
internal class PulseDotView(context: Context) : View(context) {

    private val corePaint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val ringPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = context.dpf(1.2f)
    }
    private var active = false
    private var color = Palette.Success
    private var phase = 0f

    private val pulse = ValueAnimator.ofFloat(0f, 1f).apply {
        duration = 1_900L
        repeatCount = ValueAnimator.INFINITE
        interpolator = DecelerateInterpolator()
        addUpdateListener {
            phase = it.animatedFraction
            invalidate()
        }
    }

    init {
        corePaint.color = color
        ringPaint.color = color
    }

    fun configure(active: Boolean, color: Int) {
        this.active = active
        this.color = color
        corePaint.color = color
        ringPaint.color = color
        if (!active) phase = 0f
        updatePulse()
        invalidate()
    }

    private fun updatePulse() {
        val shouldRun = active && isAttachedToWindow && windowVisibility == VISIBLE
        if (shouldRun) {
            if (!pulse.isRunning) pulse.start()
        } else if (pulse.isRunning) {
            pulse.cancel()
        }
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val size = context.dp(16)
        setMeasuredDimension(resolveSize(size, widthMeasureSpec), resolveSize(size, heightMeasureSpec))
    }

    override fun onDraw(canvas: Canvas) {
        val cx = width / 2f
        val cy = height / 2f
        val coreRadius = context.dpf(3.6f)

        if (active) {
            ringPaint.alpha = ((1f - phase) * 150f).toInt().coerceIn(0, 255)
            canvas.drawCircle(cx, cy, coreRadius + phase * context.dpf(7f), ringPaint)
        }

        corePaint.alpha = 255
        canvas.drawCircle(cx, cy, coreRadius, corePaint)
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        updatePulse()
    }

    override fun onWindowVisibilityChanged(visibility: Int) {
        super.onWindowVisibilityChanged(visibility)
        updatePulse()
    }

    override fun onDetachedFromWindow() {
        pulse.cancel()
        super.onDetachedFromWindow()
    }
}

/** 导入进度条。fraction == null 表示阶段未知（扫描安装包），显示滑动片段；
 * 有值按百分比填充，260ms 补间平滑追上去，避免 100ms 一次的回调一跳一跳。 */
internal class ImportProgressBar(context: Context) : View(context) {

    private val trackPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Palette.StrokeSoft
    }
    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val trackPath = Path()
    private val rect = RectF()
    private var fraction: Float? = null
    private var displayed = 0f
    private var shimmer = 0f
    private var fillShader: LinearGradient? = null
    private var valueAnimator: ValueAnimator? = null

    private val shimmerAnimator = ValueAnimator.ofFloat(0f, 1f).apply {
        duration = 1_200L
        repeatCount = ValueAnimator.INFINITE
        interpolator = LinearInterpolator()
        addUpdateListener {
            shimmer = it.animatedFraction
            invalidate()
        }
    }

    private val heightDp = context.dpf(8f)
    private val radius = heightDp / 2f

    fun setFraction(value: Float?) {
        val target = value?.coerceIn(0f, 1f)
        if (target == null) {
            fraction = null
            updateShimmer()
        } else {
            fraction = target
            shimmerAnimator.cancel()
            valueAnimator?.cancel()
            valueAnimator = ValueAnimator.ofFloat(displayed, target).apply {
                duration = 260L
                interpolator = DecelerateInterpolator()
                addUpdateListener {
                    displayed = it.animatedValue as Float
                    invalidate()
                }
                start()
            }
        }
        invalidate()
    }

    /** 只在真正可见且处于不确定阶段时跑 shimmer，避免隐藏的进度条在后台空转。 */
    private fun updateShimmer() {
        val shouldRun = fraction == null && isShown
        if (shouldRun) {
            if (!shimmerAnimator.isRunning) shimmerAnimator.start()
        } else if (shimmerAnimator.isRunning) {
            shimmerAnimator.cancel()
        }
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        setMeasuredDimension(getDefaultSize(suggestedMinimumWidth, widthMeasureSpec), heightDp.toInt())
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        if (w > 0) {
            fillShader = LinearGradient(
                0f, 0f, w.toFloat(), 0f,
                Palette.PrimaryGradient, null,
                Shader.TileMode.CLAMP,
            )
        }
    }

    override fun onDraw(canvas: Canvas) {
        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 0f || h <= 0f) return

        rect.set(0f, 0f, w, h)
        trackPath.reset()
        trackPath.addRoundRect(rect, radius, radius, Path.Direction.CW)
        canvas.drawPath(trackPath, trackPaint)

        fillPaint.shader = fillShader
        canvas.save()
        canvas.clipPath(trackPath)
        val value = fraction
        if (value == null) {
            val segment = w * 0.30f
            val x = -segment + (w + segment) * shimmer
            canvas.drawRect(x, 0f, x + segment, h, fillPaint)
        } else {
            canvas.drawRect(0f, 0f, w * displayed, h, fillPaint)
        }
        canvas.restore()
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        updateShimmer()
    }

    override fun onVisibilityChanged(changedView: View, visibility: Int) {
        super.onVisibilityChanged(changedView, visibility)
        updateShimmer()
    }

    override fun onWindowVisibilityChanged(visibility: Int) {
        super.onWindowVisibilityChanged(visibility)
        updateShimmer()
    }

    override fun onDetachedFromWindow() {
        shimmerAnimator.cancel()
        valueAnimator?.cancel()
        super.onDetachedFromWindow()
    }
}

/** 轻量 snackbar：从底部浮起，自动消失；用于导入结果、复制成功等一次性反馈。 */
internal class SnackbarView(context: Context) : LinearLayout(context) {

    enum class Kind { SUCCESS, ERROR, INFO }

    private val iconView = ImageView(context)
    private val messageView = TextView(context)
    private var hideTask: Runnable? = null

    init {
        orientation = HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        setPadding(context.dp(14), context.dp(12), context.dp(16), context.dp(12))
        elevation = context.dpf(12f)
        visibility = GONE
        alpha = 0f
        isClickable = true
        setOnClickListener { dismiss() }

        addView(
            iconView,
            LayoutParams(context.dp(20), context.dp(20)),
        )
        messageView.apply {
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
            typeface = Fonts.medium
            includeFontPadding = false
            setTextColor(Palette.TextPrimary)
            setLineSpacing(0f, 1.15f)
        }
        addView(
            messageView,
            LayoutParams(0, LayoutParams.WRAP_CONTENT, 1f).apply { marginStart = context.dp(10) },
        )
    }

    fun show(message: String, kind: Kind, durationMs: Long = 4_600L) {
        hideTask?.let { removeCallbacks(it) }
        val accent = when (kind) {
            Kind.SUCCESS -> Palette.Success
            Kind.ERROR -> Palette.Danger
            Kind.INFO -> Palette.Accent
        }
        val icon = when (kind) {
            Kind.SUCCESS -> R.drawable.ic_check_circle
            Kind.ERROR -> R.drawable.ic_alert
            Kind.INFO -> R.drawable.ic_info
        }

        background = GradientDrawable(
            GradientDrawable.Orientation.TOP_BOTTOM,
            intArrayOf(blend(Palette.SurfaceTop, accent, 0.20f), Palette.SurfaceBottom),
        ).apply {
            cornerRadius = context.dpf(16f)
            setStroke(context.dp(1), accent.withAlpha(0.38f))
        }
        iconView.setImageResource(icon)
        iconView.setColorFilter(accent)
        messageView.text = message

        animate().cancel()
        visibility = VISIBLE
        alpha = 0f
        translationY = context.dpf(18f)
        animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(240L)
            .setInterpolator(DecelerateInterpolator())
            .start()

        hideTask = Runnable { dismiss() }.also { postDelayed(it, durationMs) }
    }

    fun dismiss() {
        animate().cancel()
        animate()
            .alpha(0f)
            .translationY(context.dpf(12f))
            .setDuration(180L)
            .withEndAction { visibility = GONE }
            .start()
    }
}

/** 深色界面用的开关：46×28dp 胶囊 + 白色滑块，开态从描边色过渡到强调色。 */
internal class ToggleSwitch(context: Context) : View(context) {

    var isChecked: Boolean = false
        private set

    var onCheckedChange: ((Boolean) -> Unit)? = null

    private val trackPaint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val thumbPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE }
    private var progress = 0f
    private var animator: ValueAnimator? = null

    private val trackWidth = context.dpf(46f)
    private val trackHeight = context.dpf(28f)
    private val thumbRadius = context.dpf(10f)
    private val inset = context.dpf(4f)

    init {
        isClickable = true
        isFocusable = true
    }

    fun setChecked(value: Boolean, animate: Boolean = true) {
        if (isChecked == value && animator?.isRunning != true) {
            progress = if (value) 1f else 0f
            invalidate()
            return
        }
        isChecked = value
        val target = if (value) 1f else 0f
        animator?.cancel()
        if (animate) {
            animator = ValueAnimator.ofFloat(progress, target).apply {
                duration = 180L
                interpolator = DecelerateInterpolator()
                addUpdateListener {
                    progress = it.animatedValue as Float
                    invalidate()
                }
                start()
            }
        } else {
            progress = target
            invalidate()
        }
    }

    override fun performClick(): Boolean {
        super.performClick()
        setChecked(!isChecked)
        onCheckedChange?.invoke(isChecked)
        return true
    }

    override fun getAccessibilityClassName(): CharSequence = "android.widget.Switch"

    @Suppress("DEPRECATION")
    override fun onInitializeAccessibilityNodeInfo(info: AccessibilityNodeInfo) {
        super.onInitializeAccessibilityNodeInfo(info)
        info.isCheckable = true
        info.isChecked = isChecked
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        setMeasuredDimension(trackWidth.toInt(), trackHeight.toInt())
    }

    override fun onDraw(canvas: Canvas) {
        val w = width.toFloat()
        val h = height.toFloat()
        val radius = h / 2f
        trackPaint.color = blend(Palette.Stroke, Palette.Accent, progress)
        canvas.drawRoundRect(0f, 0f, w, h, radius, radius, trackPaint)

        val travel = w - inset * 2f - thumbRadius * 2f
        val cx = inset + thumbRadius + travel * progress
        canvas.drawCircle(cx, h / 2f, thumbRadius, thumbPaint)
    }

    override fun onDetachedFromWindow() {
        animator?.cancel()
        super.onDetachedFromWindow()
    }
}

/** 小工具：正文 TextView，统一去掉额外的字体留白。 */
internal fun Context.bodyText(
    text: CharSequence,
    sizeSp: Float,
    color: Int,
    typeface: Typeface = Fonts.regular,
): TextView = TextView(this).apply {
    this.text = text
    setTextSize(TypedValue.COMPLEX_UNIT_SP, sizeSp)
    setTextColor(color)
    this.typeface = typeface
    includeFontPadding = false
}
