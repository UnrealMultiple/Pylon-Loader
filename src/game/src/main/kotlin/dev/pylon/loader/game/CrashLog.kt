package dev.pylon.loader.game

import android.content.Context
import android.os.Build
import dev.pylon.loader.common.RuntimeLayout
import java.io.File
import java.io.PrintWriter
import java.io.StringWriter
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * 宿主未捕获异常落盘。文件头是启动器要解析的固定格式（`key : value`）：`kind` 决定卡片标题，
 * `detail` 是卡片上那一行原因；改格式要同步启动器侧的 [dev.pylon.loader.CrashReports]。
 *
 * 只兜 Java 层异常；原生崩溃（SIGSEGV 等）时进程被内核直接杀掉，本处理器不会执行，
 * 那种情况由启动器侧 ExitWatch 补记。
 */
internal object CrashLog {

    fun file(context: Context): File = File(context.filesDir, RuntimeLayout.CRASH_LOG_NAME)

    fun write(
        context: Context,
        thread: Thread,
        throwable: Throwable,
        kind: String = "java",
        detail: String? = null,
    ) {
        val target = file(context)
        try {
            val timestamp = SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US).format(Date())
            val stack = StringWriter().also { sw ->
                PrintWriter(sw).use { pw -> throwable.printStackTrace(pw) }
            }.toString()
            val summary = detail ?: throwable.message?.takeIf { it.isNotBlank() }
                ?.let { "${throwable.javaClass.name}: $it" }
                ?: throwable.javaClass.name

            val text = buildString {
                appendLine("=== Pylon Loader 宿主崩溃 ===")
                appendLine(field("kind", kind))
                appendLine(field("detail", summary.lineSequence().first()))
                appendLine(field("time", timestamp))
                appendLine(field("thread", thread.name))
                appendLine(field("exception", throwable.javaClass.name))
                appendLine(field("message", (throwable.message ?: "null").lineSequence().first()))
                appendLine(field("process", "${context.packageName}${RuntimeLayout.GAME_PROCESS_SUFFIX}"))
                appendLine(field("pid", android.os.Process.myPid().toString()))
                appendLine(field("app", appVersion(context)))
                appendLine(field("device", device()))
                appendLine()
                appendLine(stack)
            }

            // 先临时文件 fsync 再原子改名：崩溃处理器随时会被系统收走，半截文件会毁掉上次的日志。
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
            Log.e("写崩溃日志失败", t)
        }
    }

    private fun field(key: String, value: String): String =
        "${key.padEnd(10)}: $value"

    private fun appVersion(context: Context): String = runCatching {
        val info = context.packageManager.getPackageInfo(context.packageName, 0)
        "${info.versionName} (${info.longVersionCode})"
    }.getOrDefault("unknown")

    private fun device(): String =
        "${Build.MANUFACTURER} ${Build.MODEL} · Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})"
}
