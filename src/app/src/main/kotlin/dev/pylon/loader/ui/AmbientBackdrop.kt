package dev.pylon.loader.ui

import android.animation.ValueAnimator
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.RadialGradient
import android.graphics.Shader
import android.view.View
import android.view.animation.LinearInterpolator

/**
 * 层：启动器 UI / 背景
 *
 * 纯自绘：深蓝黑渐变 + 极淡的青/紫辉光（9 秒呼吸动画，只变明暗不移动，避免抢注意力）+ 26dp 点阵，
 * 让无图片资源的整页不至于是一块死板的纯黑。
 * 静态底图不接收点击（在 ScrollView 下面，触摸自然落到滚动内容上）。
 */
internal class AmbientBackdrop(context: Context) : View(context) {

    private val basePaint = Paint()
    private val glowPaint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val dotPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.argb(16, 255, 255, 255)
    }
    private val fadePaint = Paint()

    private var baseShader: LinearGradient? = null
    private var cyanGlow: RadialGradient? = null
    private var violetGlow: RadialGradient? = null
    private var bottomFade: LinearGradient? = null

    private var phase = 0f
    private val breathe = ValueAnimator.ofFloat(0f, 1f).apply {
        duration = 9_000L
        repeatMode = ValueAnimator.REVERSE
        repeatCount = ValueAnimator.INFINITE
        interpolator = LinearInterpolator()
        addUpdateListener {
            phase = it.animatedFraction
            invalidate()
        }
    }

    private val dotGap = context.dpf(26f)
    private val dotRadius = context.dpf(0.75f)

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        if (w <= 0 || h <= 0) return

        val width = w.toFloat()
        val height = h.toFloat()
        baseShader = LinearGradient(
            0f, 0f, 0f, height,
            Palette.BackgroundTop, Palette.BackgroundBottom,
            Shader.TileMode.CLAMP,
        )
        cyanGlow = RadialGradient(
            width * 0.14f, -height * 0.04f, width * 0.95f,
            intArrayOf(Palette.Accent, Palette.Accent, Color.TRANSPARENT),
            floatArrayOf(0f, 0.42f, 1f),
            Shader.TileMode.CLAMP,
        )
        violetGlow = RadialGradient(
            width * 1.04f, height * 0.36f, width * 0.78f,
            intArrayOf(Palette.AccentViolet, Palette.AccentViolet, Color.TRANSPARENT),
            floatArrayOf(0f, 0.4f, 1f),
            Shader.TileMode.CLAMP,
        )
        bottomFade = LinearGradient(
            0f, height * 0.70f, 0f, height,
            Color.TRANSPARENT, Palette.BackgroundBottom,
            Shader.TileMode.CLAMP,
        )
    }

    override fun onDraw(canvas: Canvas) {
        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 0f || h <= 0f) return

        basePaint.shader = baseShader
        canvas.drawRect(0f, 0f, w, h, basePaint)

        // 两块辉光各自呼吸：错开相位，避免整屏一起闪。
        cyanGlow?.let {
            glowPaint.shader = it
            glowPaint.alpha = (40 + phase * 22f).toInt()
            canvas.drawRect(0f, 0f, w, h, glowPaint)
        }
        violetGlow?.let {
            glowPaint.shader = it
            glowPaint.alpha = (34 + (1f - phase) * 20f).toInt()
            canvas.drawRect(0f, 0f, w, h, glowPaint)
        }

        var y = dotGap / 2f
        while (y < h) {
            var x = dotGap / 2f
            while (x < w) {
                canvas.drawCircle(x, y, dotRadius, dotPaint)
                x += dotGap
            }
            y += dotGap
        }

        fadePaint.shader = bottomFade
        canvas.drawRect(0f, h * 0.70f, w, h, fadePaint)
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        updateAnimation()
    }

    override fun onWindowVisibilityChanged(visibility: Int) {
        super.onWindowVisibilityChanged(visibility)
        updateAnimation()
    }

    override fun onDetachedFromWindow() {
        breathe.cancel()
        super.onDetachedFromWindow()
    }

    private fun updateAnimation() {
        val shouldRun = isAttachedToWindow && windowVisibility == VISIBLE
        if (shouldRun) {
            if (!breathe.isStarted) breathe.start()
        } else if (breathe.isStarted) {
            breathe.cancel()
        }
    }
}
