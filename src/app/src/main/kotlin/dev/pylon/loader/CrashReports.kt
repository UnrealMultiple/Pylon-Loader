package dev.pylon.loader

import android.content.Context
import dev.pylon.loader.common.RuntimeLayout
import dev.pylon.loader.ui.formatTime
import java.io.File
import java.io.RandomAccessFile

/**
 * 崩溃日志读取：crash-last.txt（宿主 Java 异常）与 crash-exit.txt（启动器补记的原生崩溃）。
 * 卡片只读头尾小段，完整内容按需读。
 */
internal enum class CrashKind(val label: String) {
    JAVA("Java 崩溃"),
    NATIVE("原生崩溃"),
    ANR("无响应"),
    STARTUP("启动失败"),
    UNKNOWN("异常退出");

    companion object {
        fun of(key: String?): CrashKind = when (key?.trim()?.lowercase()) {
            "java" -> JAVA
            "native" -> NATIVE
            "anr" -> ANR
            "startup" -> STARTUP
            else -> UNKNOWN
        }
    }
}

/** 卡片用的崩溃摘要（不整读文件）。 */
internal data class CrashInfo(
    val kind: CrashKind,
    val detail: String,
    val time: Long,
    val size: Long,
    val sources: String,
    val preview: String,
)

internal object CrashReports {

    const val MAX_TEXT_CHARS = 120_000

    private const val HEAD_BYTES = 2 * 1024
    private const val TAIL_BYTES = 3 * 1024
    private const val PREVIEW_LINES = 8
    private const val PREVIEW_LINE_CHARS = 120
    private const val DETAIL_CHARS = 160

    private val KEY_LINE =
        Regex("SIG[A-Z]+|SEGV_?|fault addr|null pointer|backtrace|#\\d{2} |::|UnityMain|Fatal signal|>>>")

    private fun isRegisterNoise(line: String): Boolean =
        Regex("^x\\d+·*$|^(lr|sp|pc|pst|esr|vg)·*$").matches(line.trim())

    fun hostFile(context: Context): File = File(context.filesDir, RuntimeLayout.CRASH_LOG_NAME)

    fun reportFile(context: Context): File = File(context.filesDir, RuntimeLayout.CRASH_EXIT_REPORT_NAME)

    fun files(context: Context): List<File> =
        listOf(reportFile(context), hostFile(context))
            .filter { it.isFile && it.length() > 0L }
            .sortedByDescending { it.lastModified() }

    fun inspect(context: Context): CrashInfo? {
        val files = files(context)
        val primary = files.firstOrNull() ?: return null
        val head = parseHead(readHead(primary, HEAD_BYTES))
        return CrashInfo(
            kind = kindOf(primary, head),
            detail = detailOf(primary, head),
            time = primary.lastModified(),
            size = files.sumOf { it.length() },
            sources = files.joinToString(" + ") { it.name },
            preview = previewOf(primary),
        )
    }

    fun readAll(context: Context): String {
        val files = files(context)
        if (files.isEmpty()) return ""
        val text = buildString {
            files.forEachIndexed { index, file ->
                if (index > 0) appendLine()
                appendLine("===== ${file.name} · ${formatTime(file.lastModified())} =====")
                appendLine(
                    runCatching { file.readText() }
                        .getOrElse { "（读取失败：${it.javaClass.simpleName}）" },
                )
            }
        }
        if (text.length <= MAX_TEXT_CHARS) return text
        val directory = files.first().parentFile?.absolutePath ?: ""
        return text.take(MAX_TEXT_CHARS) + "\n\n…（日志过长已截断；完整文件在 $directory）"
    }

    fun clear(context: Context): Boolean =
        files(context).fold(true) { ok, file -> file.delete() && ok }

    // ------------------------------------------------------------------
    // 解析与读取
    // ------------------------------------------------------------------

    private fun kindOf(file: File, head: Map<String, String>): CrashKind {
        val declared = CrashKind.of(head["kind"])
        if (declared != CrashKind.UNKNOWN) return declared
        return if (file.name == RuntimeLayout.CRASH_EXIT_REPORT_NAME) CrashKind.UNKNOWN else CrashKind.JAVA
    }

    private fun detailOf(file: File, head: Map<String, String>): String {
        head["detail"]?.takeIfUseful()?.let { return truncate(it, DETAIL_CHARS) }
        val exception = head["exception"]?.takeIfUseful()
        val message = head["message"]?.takeIfUseful()
        return when {
            exception != null && message != null -> truncate("$exception: $message", DETAIL_CHARS)
            exception != null -> truncate(exception, DETAIL_CHARS)
            head["reason"]?.takeIfUseful() != null -> truncate(head.getValue("reason"), DETAIL_CHARS)
            else -> file.name
        }
    }

    private fun previewOf(file: File): String {
        val bodyLines = readHead(file, HEAD_BYTES)
            .lineSequence()
            .dropWhile { it.isNotBlank() }
            .dropWhile { it.isBlank() }
            .filter { it.isNotBlank() && !it.startsWith("---") && !isRegisterNoise(it) }
            .map { line -> if (line.length <= PREVIEW_LINE_CHARS) line else line.take(PREVIEW_LINE_CHARS - 1) + "…" }
            .toList()
        val key = bodyLines.filter { KEY_LINE.containsMatchIn(it) }
        val body = (key + bodyLines).distinct().take(PREVIEW_LINES)
        if (body.isNotEmpty()) return body.joinToString("\n")

        val truncated = file.length() > TAIL_BYTES
        val tail = readTail(file, TAIL_BYTES)
            .lineSequence()
            .drop(if (truncated) 1 else 0)
            .map { it.trimEnd() }
            .filter { it.isNotBlank() }
            .toList()
        return tail.takeLast(PREVIEW_LINES).joinToString("\n")
    }

    private fun parseHead(text: String): Map<String, String> {
        val fields = LinkedHashMap<String, String>()
        for (raw in text.lineSequence()) {
            val line = raw.trimEnd()
            if (line.isBlank()) break
            if (line.startsWith("===") || line.startsWith("---")) continue
            val colon = line.indexOf(':')
            if (colon <= 0) continue
            val key = line.substring(0, colon).trim()
            if (key.isEmpty() || !key.all { it.isLetterOrDigit() || it == '_' }) continue
            fields[key.lowercase()] = line.substring(colon + 1).trim()
        }
        return fields
    }

    private fun readHead(file: File, bytes: Int): String = readRange(file, 0L, bytes.toLong())

    private fun readTail(file: File, bytes: Int): String {
        val start = (file.length() - bytes).coerceAtLeast(0L)
        return readRange(file, start, file.length() - start)
    }

    private fun readRange(file: File, offset: Long, count: Long): String = runCatching {
        if (count <= 0L) return@runCatching ""
        RandomAccessFile(file, "r").use { raf ->
            raf.seek(offset)
            val buffer = ByteArray(count.toInt())
            val read = raf.read(buffer)
            if (read <= 0) "" else String(buffer, 0, read, Charsets.UTF_8)
        }
    }.getOrDefault("")

    private fun String.takeIfUseful(): String? =
        takeIf { it.isNotBlank() && !it.equals("null", ignoreCase = true) }

    private fun truncate(text: String, max: Int): String {
        val oneLine = text.lineSequence().first().trim()
        return if (oneLine.length <= max) oneLine else oneLine.take(max - 1) + "…"
    }
}
