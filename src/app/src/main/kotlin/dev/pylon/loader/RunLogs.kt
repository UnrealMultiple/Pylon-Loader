package dev.pylon.loader

import android.content.Context
import android.os.Build
import dev.pylon.loader.ui.formatTime
import java.io.File

/** 主页"运行日志"卡用的摘要：上一次游戏会话里托管层（框架 + Mod）报了什么。 */
internal data class RunLogInfo(
    val sessionName: String,
    val time: Long,
    val errorCount: Int,
    val warnCount: Int,
    val preview: String,
)

/**
 * 托管会话日志的读取端（写端在 C# SessionLog：logs/pylon_*.txt，与 Mods/ 同级）。
 *
 * 崩溃卡片只覆盖"进程怎么死的"（原生/Java/ANR）；Mod 报错、托管异常是另一路——
 * 它们不一定让进程死，但用户同样需要看到。参照 Bunny Loader 的做法：
 * 错误/警告行按 "HH:mm:ss.fff L [来源] 消息" 的固定格式解析（级别在 13 列）。
 */
internal object RunLogs {

    private const val LEVEL_COLUMN = 13
    private const val PREVIEW_LINES = 6
    private const val PROBLEM_LIMIT = 30
    private const val TAIL_LINES = 15
    private const val MOD_LINE_LIMIT = 200
    private const val PREVIEW_LINE_CHARS = 110

    fun logsDir(context: Context): File =
        File(context.getExternalFilesDir(null)?.parentFile, "logs")

    fun newestSession(context: Context): File? =
        logsDir(context).listFiles { f -> f.name.startsWith("pylon_") && f.name.endsWith(".txt") }
            .orEmpty().maxByOrNull { it.name }

    fun bootTrail(context: Context): File = File(context.filesDir, "pylon-boot.log")

    fun logcatCapture(context: Context): File = File(context.filesDir, "pylon-logcat.log")

    fun inspect(context: Context): RunLogInfo? {
        val session = newestSession(context) ?: return null
        val lines = runCatching { session.readLines() }.getOrDefault(emptyList())
        val problems = problemsOf(lines)
        val errors = problems.count { it[LEVEL_COLUMN] == 'E' }
        val warnings = problems.count { it[LEVEL_COLUMN] == 'W' }
        if (lines.isEmpty()) return null

        val preview = problems.filter { it[LEVEL_COLUMN] == 'E' }
            .takeLast(PREVIEW_LINES)
            .map { shorten(it) }
        return RunLogInfo(
            sessionName = session.name,
            time = session.lastModified(),
            errorCount = errors,
            warnCount = warnings,
            preview = if (preview.isEmpty()) "(没有错误行)" else preview.joinToString("\n"),
        )
    }

    /** 完整报告：现场信息 + 崩溃文件 + 会话错误/警告 + Mod 行尾 + boot 轨迹 + logcat 摘录。 */
    fun fullReport(context: Context): String = buildString {
        appendLine("=== Pylon 运行报告 ===")
        appendLine("app: ${context.packageName}")
        appendLine("device: ${Build.MANUFACTURER} ${Build.MODEL} · Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})")
        appendLine("time: ${formatTime(System.currentTimeMillis())}")
        appendLine()
        appendLine("--- 崩溃记录 ---")
        appendLine(CrashReports.readAll(context).ifBlank { "(无)" })
        appendLine("--- 上次会话：错误与警告 ---")
        appendLine(sessionProblems(context).ifBlank { "(没有会话日志)" })
        appendLine("--- 上次会话：Mod 行（末尾 $MOD_LINE_LIMIT 行） ---")
        appendLine(sessionModLines(context).ifBlank { "(没有会话日志)" })
        appendLine("--- boot 轨迹 ---")
        appendLine(readTail(bootTrail(context)).ifBlank { "(空)" })
        appendLine("--- 本进程 logcat 摘录 ---")
        appendLine(logcatExcerpt(context))
    }

    /**
     * logcat 摘录：重点行（abort/JNI/dlopen/Fatal 等）放最前——Unity abort 时 ART 的
     * 大堆栈紧跟在原因后面，从尾部看正好错过；然后才是摘录尾部。
     */
    private fun logcatExcerpt(context: Context): String {
        val lines = runCatching { logcatCapture(context).readLines() }.getOrDefault(emptyList())
        if (lines.isEmpty()) return "(空)"
        val causes = lines.filter { line ->
            line.contains("FatalError called") || line.contains("JNI DETECTED") ||
                line.contains("Abort message") || line.contains("Fatal signal") ||
                line.contains("UnsatisfiedLink") || line.contains("dlopen failed") ||
                line.contains("E/Unity") || line.contains("E Unity") ||
                line.contains("E PylonLoader") ||
                line.contains("SIGSEGV") || line.contains("SIGABRT")
        }.distinct().take(15)
        return buildString {
            if (causes.isNotEmpty()) {
                appendLine(">>> 重点：")
                causes.forEach { appendLine("  $it") }
                appendLine(">>> 尾部：")
            }
            lines.takeLast(25).forEach { appendLine(it) }
        }
    }

    /** 清掉会话日志与宿主轨迹（崩溃记录走 CrashReports.clear）。 */
    fun clear(context: Context): Boolean {
        val sessionFiles = logsDir(context).listFiles { f: File -> f.name.startsWith("pylon_") }.orEmpty().toList()
        val targets: List<File> = sessionFiles + listOf(bootTrail(context), logcatCapture(context))
        var ok = true
        for (file in targets) {
            if (file.isFile && !file.delete()) ok = false
        }
        return ok
    }

    // ------------------------------------------------------------------
    // 解析
    // ------------------------------------------------------------------

    /** 一条日志：首行带 "HH:mm:ss.fff L [来源] " 前缀，异常堆栈是后续无前缀的续行。 */
    private class Entry(val first: String) {
        val continuation = mutableListOf<String>()
        fun flatten(): List<String> = listOf(first) + continuation
    }

    private val entryStart = Regex("""^\d\d:\d\d:\d\d\.\d{3} [IWE] \[([^]]+)\]""")

    private fun entriesOf(lines: List<String>): List<Entry> {
        val out = ArrayList<Entry>()
        for (line in lines) {
            if (entryStart.matches(line) || out.isEmpty()) out.add(Entry(line))
            else out.last().continuation.add(line)
        }
        return out
    }

    private fun sourceOf(entry: Entry): String? = entryStart.find(entry.first)?.groupValues?.get(1)

    private fun sessionProblems(context: Context): String {
        val session = newestSession(context) ?: return ""
        val lines = runCatching { session.readLines() }.getOrDefault(emptyList())
        if (lines.isEmpty()) return ""
        val problems = entriesOf(lines).filter { entry ->
            entry.first.length > LEVEL_COLUMN && (entry.first[LEVEL_COLUMN] == 'E' || entry.first[LEVEL_COLUMN] == 'W')
        }
        return buildString {
            appendLine(session.name)
            if (problems.isEmpty()) appendLine("(没有错误或警告)")
            else problems.takeLast(PROBLEM_LIMIT).forEach { entry -> entry.flatten().forEach { appendLine(it) } }
            appendLine(">>> 日志末尾：")
            lines.takeLast(TAIL_LINES).forEach { appendLine(it) }
        }
    }

    /** 某个 Mod 自己的最近日志（来源标签 == modId，堆栈续行跟随首行），旧的在前。没有会话/没有输出返回 null。 */
    fun modRecentLines(context: Context, modId: String, limit: Int = 20): List<String>? {
        val session = newestSession(context) ?: return null
        val lines = runCatching { session.readLines() }.getOrDefault(emptyList())
        val tag = "[$modId]"
        val own = entriesOf(lines).filter { entry ->
            // 级别列在 13，来源标签紧跟其后（"[id] "），避免误匹配到消息文本里的同名片段。
            entry.first.length > LEVEL_COLUMN + tag.length &&
                entry.first[LEVEL_COLUMN] in "IWE" &&
                entry.first.regionMatches(LEVEL_COLUMN + 2, tag, 0, tag.length)
        }
        if (own.isEmpty()) return null
        return own.takeLast(limit).flatMap { it.flatten() }
    }

    private fun sessionModLines(context: Context): String {
        val session = newestSession(context) ?: return ""
        val lines = runCatching { session.readLines() }.getOrDefault(emptyList())
        // Mod 条目 = 来源不是 host/log 的（C# 侧来源是 Mod id）；续行（堆栈）跟着首行走。
        val modEntries = entriesOf(lines).filter { entry ->
            val source = sourceOf(entry)
            source != null && source != "host" && source != "log"
        }
        if (modEntries.isEmpty()) return "(无)"
        return modEntries.takeLast(MOD_LINE_LIMIT).joinToString("\n") { it.flatten().joinToString("\n") }
    }

    private fun problemsOf(lines: List<String>): List<String> =
        entriesOf(lines)
            .filter { it.first.length > LEVEL_COLUMN && (it.first[LEVEL_COLUMN] == 'E' || it.first[LEVEL_COLUMN] == 'W') }
            .map { it.first }

    private fun readTail(file: File): String {
        if (!file.isFile || file.length() == 0L) return ""
        return runCatching { file.readText() }.getOrElse { "" }
    }

    private fun shorten(line: String): String =
        if (line.length <= PREVIEW_LINE_CHARS) line else line.take(PREVIEW_LINE_CHARS - 1) + "…"
}
