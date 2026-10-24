package dev.pylon.loader.game

import android.content.Context
import android.os.Build
import java.io.File

/**
 * 宿主进程的会话轨迹，落文件供启动器在崩溃后展示（没有 adb 的用户也能提供现场）。
 *
 * 三路信息，对应三种死法：
 *  · pylon-boot.log   —— 宿主自己的启动/运行轨迹（[Log] 的文件槽写入）；Java 崩溃另有 crash-last.txt。
 *  · pylon-logcat.log —— 自进程 logcat 摘录：Unity abort / JNI Fatal / dlopen 失败的原因都打在
 *                        logcat 里、且发生在任何 try/catch 之外；App 可以无权限读自己的 logcat，
 *                        这比 tombstone（Android 12+ 是 protobuf 且常被截断）可靠得多。
 *  · crash-exit.txt   —— 原生崩溃/ANR 由启动器侧 [ExitWatch] 补记，不在这里管。
 * 托管侧（Mod 错误）另有 C# 的 SessionLog（logs/pylon_*.txt），也不归这里管。
 */
internal object PylonBootLog {

    private const val BOOT_FILE = "pylon-boot.log"
    private const val LOGCAT_FILE = "pylon-logcat.log"
    private const val LOGCAT_MAX_BYTES = 60_000L

    private fun bootFile(ctx: Context) = File(ctx.filesDir, BOOT_FILE)
    private fun logcatFile(ctx: Context) = File(ctx.filesDir, LOGCAT_FILE)

    /** 开新会话：清掉上一轮的 boot 轨迹与 logcat 摘录。 */
    fun reset(ctx: Context) {
        runCatching { bootFile(ctx).writeText("") }
        runCatching { logcatFile(ctx).writeText("") }
    }

    /** 给 [Log] 挂文件槽：宿主所有 i/w/e/stage 行同步落 boot 轨迹。 */
    fun attachBootSink(ctx: Context) {
        Log.attachSink(bootFile(ctx))
    }

    /** 记一次失败：解到根因（反射层会把异常包起来），带 6 帧栈。 */
    fun fail(ctx: Context, what: String, t: Throwable?) {
        val root = t?.let { generateSequence(it) { c -> c.cause }.takeWhile { it != it.cause }.lastOrNull() ?: it }
        val detail = root?.let { "${it.javaClass.simpleName}: ${it.message}" } ?: "无详情"
        Log.e("BOOT-FAIL: $what -> $detail")
        root?.stackTrace?.take(6)?.forEach { Log.e("  at $it") }
    }

    /**
     * 捕获本进程的 logcat 摘录（守护线程）。
     * 只挑错误相关行：我们的日志（PylonLoader）、Unity/IL2CPP、JNI、dlopen、Fatal 与各路错误——
     * 用户崩溃后要发的是"为什么死"，不是完整历史。60KB 封顶。
     * 排除掉 Unity 每次启动必刷的无害样板（serialized-layout 校验），别让它淹没真正的原因。
     */
    fun captureLogcat(ctx: Context) {
        Thread {
            runCatching {
                val pid = android.os.Process.myPid()
                val process = Runtime.getRuntime().exec(arrayOf("logcat", "-v", "brief", "--pid=$pid"))
                process.inputStream.bufferedReader().use { reader ->
                    val keep = listOf(
                        RuntimeLayoutTag, "Unity", "IL2CPP", "JNI", "dlopen",
                        "Fatal", "FATAL", "Error", "error", "libc", "art", "DEBUG",
                    )
                    val noise = listOf(
                        "serialization layout when loading", "Did you #ifdef UNITY_EDITOR",
                        "properties in any of your scripts",
                    )
                    val out = logcatFile(ctx)
                    reader.forEachLine { line ->
                        if (out.length() >= LOGCAT_MAX_BYTES) return@forEachLine
                        if (noise.any { line.contains(it) }) return@forEachLine
                        if (keep.any { line.contains(it) }) {
                            runCatching { out.appendText(line + "\n") }
                        }
                    }
                }
                process.destroy()
            }
        }.apply { isDaemon = true; name = "pylon-logcat-capture" }.start()
    }

    /** 汇总一份"发给开发者"的报告：设备信息 + 崩溃文件 + boot 轨迹 + logcat 摘录。 */
    fun report(ctx: Context, crashFiles: List<File>): String = buildString {
        appendLine("=== Pylon 宿主报告 ===")
        append("设备: ${Build.MANUFACTURER} ${Build.MODEL} · Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})\n")
        appendLine("生成: ${java.text.SimpleDateFormat("yyyy-MM-dd HH:mm", java.util.Locale.US).format(java.util.Date())}")
        crashFiles.forEach { f ->
            appendLine("--- ${f.name} ---")
            appendLine(runCatching { f.readText() }.getOrDefault("(读取失败)"))
        }
        appendLine("--- boot 轨迹 ---")
        appendLine(runCatching { bootFile(ctx).readText() }.getOrDefault("").ifBlank { "(空)" })
        appendLine("--- 本进程 logcat 摘录 ---")
        appendLine(runCatching { logcatFile(ctx).readText() }.getOrDefault("").ifBlank { "(空)" })
    }

    private const val RuntimeLayoutTag = "PylonLoader"
}
