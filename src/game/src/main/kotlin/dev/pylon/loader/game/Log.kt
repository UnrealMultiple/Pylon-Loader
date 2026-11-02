package dev.pylon.loader.game

import android.os.Process
import android.os.SystemClock
import android.util.Log as AndroidLog
import dev.pylon.loader.common.RuntimeLayout

/// 宿主层日志，tag 与启动器层相同（:common 是纯 JVM 模块，不能依赖 android.util.Log）。
internal object Log {
    private const val TAG = RuntimeLayout.LOG_TAG

    private val processStart = lazy { Process.getStartElapsedRealtime() }

    fun i(message: String) = AndroidLog.i(TAG, message)
    fun w(message: String) = AndroidLog.w(TAG, message)
    fun w(message: String, t: Throwable) = AndroidLog.w(TAG, message, t)
    fun e(message: String) = AndroidLog.e(TAG, message)
    fun e(message: String, t: Throwable) = AndroidLog.e(TAG, message, t)

    /// 阶段日志带"启动后 +Nms"：启动时间线直接从 logcat 读，不用另做埋点。
    fun stage(stage: String, message: String) =
        AndroidLog.i(TAG, "[$stage] +${SystemClock.elapsedRealtime() - processStart.value}ms $message")
}
