package dev.pylon.loader.game

import android.util.Log as AndroidLog
import dev.pylon.loader.common.RuntimeLayout

/** 宿主层日志，tag 与启动器层相同（:common 是纯 JVM 模块，不能依赖 android.util.Log）。 */
internal object Log {
    private const val TAG = RuntimeLayout.LOG_TAG

    fun i(message: String) = AndroidLog.i(TAG, message)
    fun w(message: String) = AndroidLog.w(TAG, message)
    fun w(message: String, t: Throwable) = AndroidLog.w(TAG, message, t)
    fun e(message: String) = AndroidLog.e(TAG, message)
    fun e(message: String, t: Throwable) = AndroidLog.e(TAG, message, t)
    fun stage(stage: String, message: String) = AndroidLog.i(TAG, "[$stage] $message")
}
