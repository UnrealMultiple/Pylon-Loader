package dev.pylon.loader

import android.util.Log as AndroidLog
import dev.pylon.loader.common.RuntimeLayout

/** 启动器层日志，tag 与宿主层相同，`adb logcat -s PylonLoader` 可同时看到两侧。 */
internal object Log {
    private const val TAG = RuntimeLayout.LOG_TAG

    fun i(message: String) = AndroidLog.i(TAG, message)
    fun w(message: String) = AndroidLog.w(TAG, message)
    fun e(message: String) = AndroidLog.e(TAG, message)
    fun e(message: String, t: Throwable) = AndroidLog.e(TAG, message, t)
    fun stage(stage: String, message: String) = AndroidLog.i(TAG, "[$stage] $message")
}
