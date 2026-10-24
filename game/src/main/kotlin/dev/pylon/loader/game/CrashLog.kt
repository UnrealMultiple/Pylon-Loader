package dev.pylon.loader.game

import android.content.Context
import dev.pylon.loader.common.RuntimeLayout
import java.io.File
import java.io.PrintWriter
import java.io.StringWriter
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * 把宿主进程的未捕获异常落盘（M1 通过标准：崩溃能留下日志文件）。
 * 写在 app 私有目录，启动器层可直接读取，不需要额外权限。
 */
internal object CrashLog {

    fun file(context: Context): File = File(context.filesDir, RuntimeLayout.CRASH_LOG_NAME)

    fun write(context: Context, thread: Thread, throwable: Throwable) {
        val target = file(context)
        try {
            val timestamp = SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US).format(Date())
            val stack = StringWriter().also { sw ->
                PrintWriter(sw).use { pw -> throwable.printStackTrace(pw) }
            }.toString()

            val text = buildString {
                appendLine("=== Pylon Loader 宿主崩溃 ===")
                appendLine("time      : $timestamp")
                appendLine("thread    : ${thread.name}")
                appendLine("exception : ${throwable.javaClass.name}")
                appendLine("message   : ${throwable.message}")
                appendLine()
                appendLine(stack)
            }

            // 崩溃处理器随时可能被系统收走：先写临时文件并 fsync 再原子改名，否则半截文件会毁掉这次的崩溃日志（同名）。
            val temp = File(target.parentFile, "${target.name}.tmp")
            temp.outputStream().use { out ->
                out.write(text.toByteArray())
                out.flush()
                (out as? java.io.FileOutputStream)?.fd?.sync()
            }
            if (!temp.renameTo(target)) {
                target.writeText(text)
                temp.delete()
            }
            Log.e("已写入崩溃日志：${target.absolutePath}")
        } catch (t: Throwable) {
            // 崩溃处理里再崩就真的没线索了，这里只记录，不抛出。
            Log.e("写崩溃日志失败", t)
        }
    }
}
