package dev.pylon.loader.game

import android.os.Process
import android.os.SystemClock
import android.util.Log as AndroidLog
import dev.pylon.loader.common.RuntimeLayout
import java.io.File

/// 宿主层日志，tag 与启动器层相同（:common 是纯 JVM 模块，不能依赖 android.util.Log）。
/// 可选文件槽（[attachSink]）：同样的行同步追加到 boot 轨迹文件，启动器崩溃后展示用。
internal object Log {
    private const val TAG = RuntimeLayout.LOG_TAG

    private val processStart = lazy { Process.getStartElapsedRealtime() }

    @Volatile
    private var sink: File? = null

    /// 挂上 boot 轨迹文件（挂载前的行不补写）。写入永不抛异常。
    fun attachSink(file: File) {
        sink = file
    }

    private fun toSink(message: String) {
        val file = sink ?: return
        runCatching { file.appendText(message + "\n") }
    }

    fun i(message: String) {
        AndroidLog.i(TAG, message); toSink(message)
    }

    fun w(message: String) {
        AndroidLog.w(TAG, message); toSink("W $message")
    }

    fun w(message: String, t: Throwable) {
        AndroidLog.w(TAG, message, t); toSink("W $message (${t.javaClass.simpleName}: ${t.message})")
    }

    fun e(message: String) {
        AndroidLog.e(TAG, message); toSink("E $message")
    }

    fun e(message: String, t: Throwable) {
        AndroidLog.e(TAG, message, t); toSink("E $message (${t.javaClass.simpleName}: ${t.message})")
    }

    /// 阶段日志带"启动后 +Nms"：启动时间线直接从 logcat 读，不用另做埋点。
    fun stage(stage: String, message: String) {
        val line = "[$stage] +${SystemClock.elapsedRealtime() - processStart.value}ms $message"
        AndroidLog.i(TAG, line); toSink(line)
    }
}
