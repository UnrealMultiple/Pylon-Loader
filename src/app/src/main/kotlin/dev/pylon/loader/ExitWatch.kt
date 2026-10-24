package dev.pylon.loader

import android.app.ActivityManager
import android.app.ApplicationExitInfo
import android.content.Context
import android.content.SharedPreferences
import android.os.Build
import dev.pylon.loader.common.RuntimeLayout
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.InputStream
import java.io.BufferedReader
import java.io.InputStreamReader
import java.nio.charset.StandardCharsets
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** 新发现的一次异常退出。 */
internal data class ExitEvent(val kind: CrashKind, val detail: String)

/**
 * 崩溃检测：原生崩溃（SIGSEGV 等）时宿主来不及写日志，改问系统的进程退出记录（API 30+）。
 * 会读系统文件，**必须在后台线程调用**。
 */
internal object ExitWatch {

    private const val PREFS = "pylon-launcher"
    private const val KEY_CHECKPOINT = "exit-checkpoint"
    private const val KEY_HOST_LOG_SEEN = "crash-log-seen"
    private const val MAX_RECORDS = 20
    private const val MAX_TRACE_BYTES = 512 * 1024
    private const val MAX_BACKTRACE_FRAMES = 40
    private const val HOST_LOG_WINDOW_MS = 10_000L

    private val inspectLock = Any()

    /** @return 新崩溃；正常退出/系统回收返回 null。*/
    fun inspect(context: Context): ExitEvent? = synchronized(inspectLock) {
        doInspect(context)
    }

    private fun doInspect(context: Context): ExitEvent? {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val hostFile = CrashReports.hostFile(context)
        val hostModified = if (hostFile.isFile) hostFile.lastModified() else 0L

        if (!prefs.contains(KEY_HOST_LOG_SEEN)) prefs.edit().putLong(KEY_HOST_LOG_SEEN, hostModified).apply()
        val hostSeen = prefs.getLong(KEY_HOST_LOG_SEEN, hostModified)

        val exit = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) latestCrashExit(context, prefs) else null
        if (exit != null) {
            val kind = kindOf(exit.reason) ?: CrashKind.UNKNOWN
            if (hostModified > 0L && hostLogBelongsTo(exit, hostFile)) {
                // 只有同一个 PID、同一个退出时间附近的 Java 记录才可复用；旧日志不能遮住 native tombstone。
                prefs.edit().putLong(KEY_HOST_LOG_SEEN, hostModified).apply()
                val summary = CrashReports.inspect(context)
                return ExitEvent(kind, summary?.detail ?: kind.label)
            }
            val report = writeReport(context, exit, kind) ?: return null
            Log.stage("launcher", "检测到宿主异常退出：${describe(exit, kind)} → ${report.name}")
            return ExitEvent(kind, describe(exit, kind))
        }

        if (hostModified > hostSeen) {
            prefs.edit().putLong(KEY_HOST_LOG_SEEN, hostModified).apply()
            val summary = CrashReports.inspect(context)
            return ExitEvent(summary?.kind ?: CrashKind.JAVA, summary?.detail ?: "宿主进程异常结束")
        }
        return null
    }

    // ------------------------------------------------------------------
    // 系统退出记录
    // ------------------------------------------------------------------

    /** 取检查点后 :game 进程的崩溃记录，并把水位推进到最新。 */
    private fun latestCrashExit(context: Context, prefs: SharedPreferences): ApplicationExitInfo? {
        val manager = context.getSystemService(ActivityManager::class.java) ?: return null
        val records = runCatching { manager.getHistoricalProcessExitReasons(null, 0, MAX_RECORDS) }
            .onFailure { Log.w("读取进程退出记录失败：${it.javaClass.simpleName}: ${it.message}") }
            .getOrNull()
            ?: return null

        if (!prefs.contains(KEY_CHECKPOINT)) {
            prefs.edit().putLong(KEY_CHECKPOINT, System.currentTimeMillis()).apply()
            return null
        }
        val checkpoint = prefs.getLong(KEY_CHECKPOINT, 0L)
        val game = records.filter {
            it.timestamp > checkpoint && it.processName.endsWith(RuntimeLayout.GAME_PROCESS_SUFFIX)
        }
        if (game.isEmpty()) return null

        prefs.edit().putLong(KEY_CHECKPOINT, game.maxOf { it.timestamp }).apply()
        return game.filter { kindOf(it.reason) != null }.maxByOrNull { it.timestamp }
    }

    private fun kindOf(reason: Int): CrashKind? = when (reason) {
        ApplicationExitInfo.REASON_CRASH -> CrashKind.JAVA
        ApplicationExitInfo.REASON_CRASH_NATIVE -> CrashKind.NATIVE
        ApplicationExitInfo.REASON_ANR -> CrashKind.ANR
        ApplicationExitInfo.REASON_INITIALIZATION_FAILURE -> CrashKind.STARTUP
        else -> null
    }

    private fun reasonName(reason: Int): String = when (reason) {
        ApplicationExitInfo.REASON_CRASH -> "REASON_CRASH"
        ApplicationExitInfo.REASON_CRASH_NATIVE -> "REASON_CRASH_NATIVE"
        ApplicationExitInfo.REASON_ANR -> "REASON_ANR"
        ApplicationExitInfo.REASON_INITIALIZATION_FAILURE -> "REASON_INITIALIZATION_FAILURE"
        ApplicationExitInfo.REASON_LOW_MEMORY -> "REASON_LOW_MEMORY"
        ApplicationExitInfo.REASON_EXIT_SELF -> "REASON_EXIT_SELF"
        ApplicationExitInfo.REASON_SIGNALED -> "REASON_SIGNALED"
        else -> "REASON_$reason"
    }

    private fun describe(exit: ApplicationExitInfo, kind: CrashKind): String = when (kind) {
        CrashKind.NATIVE -> "原生崩溃（${signalName(exit.status)}）"
        CrashKind.ANR -> "无响应（ANR）"
        CrashKind.JAVA -> "Java 未捕获异常"
        CrashKind.STARTUP -> "宿主进程初始化失败"
        else -> kind.label
    }

    private fun signalName(status: Int): String = when (status) {
        4 -> "SIGILL 4"
        5 -> "SIGTRAP 5"
        6 -> "SIGABRT 6"
        7 -> "SIGBUS 7"
        8 -> "SIGFPE 8"
        11 -> "SIGSEGV 11"
        else -> "signal $status"
    }

    // ------------------------------------------------------------------
    // 落盘
    // ------------------------------------------------------------------

    private data class TraceRead(val bytes: ByteArray, val failure: String? = null)

    private fun writeReport(context: Context, exit: ApplicationExitInfo, kind: CrashKind): File? {
        val target = CrashReports.reportFile(context)
        val traceRead = if (kind == CrashKind.NATIVE) readTrace(exit) else TraceRead(ByteArray(0))
        val trace = if (kind == CrashKind.NATIVE) {
            decodeNativeTrace(traceRead.bytes, exit.pid).ifBlank { readCrashBuffer(exit.pid) }
        } else ""
        val text = buildString {
            appendLine("=== Pylon Loader 宿主异常退出 ===")
            appendLine(field("kind", kind.name.lowercase()))
            appendLine(field("detail", describe(exit, kind)))
            appendLine(field("reason", "${reasonName(exit.reason)} (${exit.reason})"))
            appendLine(field("signal", signalName(exit.status)))
            appendLine(field("time", timestamp(exit.timestamp)))
            appendLine(field("process", exit.processName))
            appendLine(field("pid", exit.pid.toString()))
            appendLine(field("description", exit.description?.lineSequence()?.firstOrNull().orEmpty()))
            appendLine(field("app", appVersion(context)))
            appendLine(field("device", device()))
            appendLine()
            if (trace.isBlank()) {
                when {
                    traceRead.failure != null -> appendLine("（读取 tombstone 失败：${traceRead.failure}）")
                    traceRead.bytes.isEmpty() -> appendLine("（系统未提供 tombstone 数据流）")
                    else -> appendLine(
                        "（已读取 ${traceRead.bytes.size} 字节 tombstone，但未能解析线程堆栈；protobuf 格式可能与当前 Android 版本不同）",
                    )
                }
            } else {
                appendLine("--- native crash backtrace ---")
                appendLine(trace)
            }
            appendLine()
            appendLine("--- 说明 ---")
            appendLine("宿主进程被内核终止时 Java 层的未捕获异常处理器不会执行，这份记录来自系统的进程退出原因。")
            appendLine("若系统未提供 tombstone，可在复现前连接电脑运行：adb logcat -c；崩溃后立即运行：adb logcat -b crash -d -v threadtime > pylon-crash.txt")
        }
        return runCatching {
            writeAtomically(target, text)
            target
        }.onFailure {
            Log.e("写异常退出记录失败", it)
        }.getOrNull()
    }

    private fun readTrace(exit: ApplicationExitInfo): TraceRead = try {
        val input = exit.traceInputStream
            ?: return TraceRead(ByteArray(0), "系统返回 null traceInputStream")
        input.use { TraceRead(readCapped(it, MAX_TRACE_BYTES)) }
    } catch (t: Throwable) {
        TraceRead(ByteArray(0), "${t.javaClass.simpleName}: ${t.message ?: "无详细信息"}")
    }

    /** OEM 有时不给 ApplicationExitInfo trace stream，但仍把 debuggerd 输出写入 crash log buffer。 */
    private fun readCrashBuffer(pid: Int): String {
        return try {
            val process = Runtime.getRuntime().exec(
                arrayOf("/system/bin/logcat", "-b", "crash", "-d", "-v", "threadtime"),
            )
            val output = process.inputStream.use { input ->
                BufferedReader(InputStreamReader(input, StandardCharsets.UTF_8)).readLines()
            }
            process.errorStream.close()
            if (process.waitFor() != 0) return ""
            val marker = Regex("\\bpid: $pid,.*\\btid:")
            val start = output.indexOfLast { marker.containsMatchIn(it) }
            if (start < 0) return ""
            val headerStart = output.subList(0, start).indexOfLast { it.contains("*** *** ***") }
                .let { if (it < 0) start else it }
            val end = (start + 1 until output.size).firstOrNull { index ->
                output[index].contains("*** *** ***") && index > headerStart
            } ?: output.size
            val section = output.subList(start, end)
            val frames = section.filter {
                it.contains(" backtrace:") || Regex("#\\d{2} pc ").containsMatchIn(it) ||
                    it.contains(" signal ") || it.contains("fault addr ")
            }
            frames.joinToString("\n") { it.substringAfter("F DEBUG:").trim() }
        } catch (_: Throwable) {
            ""
        }
    }

    /** trace 是二进制 protobuf，只保留文本字符，噪声压成单个 ·。 */
    private fun sanitizeTrace(raw: String): String {
        val out = StringBuilder(raw.length)
        var pendingDot = false
        for (ch in raw) {
            val printable = when {
                ch == '\n' || ch == '\r' || ch == '\t' -> true
                ch.code in 0x20..0x7E -> true
                ch.code in 0x3000..0x9FFF -> true
                Character.isLetterOrDigit(ch) || Character.isSpaceChar(ch) -> true
                else -> false
            }
            if (printable) {
                pendingDot = false
                out.append(ch)
            } else if (!pendingDot) {
                pendingDot = true
                out.append('·')
            }
        }
        return out.toString()
    }

    /** 从清洗后的 trace 里按出现顺序收集 C++ 符号帧（debuggerd 已解析的部分线程栈）。 */
    private fun extractFrames(clean: String): List<String> {
        val pattern = Regex("[A-Za-z_][A-Za-z0-9_:]*\\([^()]{0,150}\\)")
        val seen = LinkedHashSet<String>()
        for (match in pattern.findAll(clean)) {
            val frame = normalizeFrame(match.value)
            if (frame.contains("::") && frame.length <= 160) seen.add(frame)
        }
        return seen.toList()
    }

    /** 去掉 protobuf 长度字节粘连的前缀（如 "tart::…" → "art::…"）。 */
    private fun normalizeFrame(frame: String): String =
        frame.replaceFirst(Regex("^[A-Za-z](?=android::|art::|std::)"), "")

    /** Android 的 native trace 是 debuggerd 的 tombstone.proto，不是 UTF-8 文本。 */
    private fun decodeNativeTrace(raw: ByteArray, expectedPid: Int): String {
        if (raw.isEmpty()) return ""
        val tombstone = ProtoMessage.parse(raw) ?: return ""
        // AOSP tombstone.proto: fingerprint=1, pid=2, tid=3, process=4,
        // thread=5, signal=6, abort_message=7, threads=8; Thread backtraces=3/4.
        val pid = tombstone.varint(2)?.toInt()
        if (pid != null && pid != expectedPid) return ""
        val tid = tombstone.varint(3)?.toInt()
        val threads = tombstone.messages(8).mapNotNull { thread ->
            val id = thread.varint(1)?.toInt() ?: return@mapNotNull null
            id to thread
        }
        val thread = threads.firstOrNull { it.first == tid }?.second ?: threads.firstOrNull()?.second ?: return ""
        val frames = (thread.messages(3) + thread.message(4)?.messages(1).orEmpty())
            .distinctBy { it.varint(2) to it.string(4) }
            .mapIndexed { index, frame ->
                val relPc = frame.varint(1)
                val pc = frame.varint(2)
                val function = frame.string(4)?.takeIf { it.isNotBlank() } ?: "<unknown>"
                val offset = frame.varint(5)
                val file = frame.string(6)?.takeIf { it.isNotBlank() }
                val location = if (pc != null) "pc 0x${pc.toString(16)}" else "pc ?"
                val relative = relPc?.let { ", rel_pc 0x${it.toString(16)}" }.orEmpty()
                val source = file?.let { " $it" }.orEmpty()
                val suffix = offset?.let { "+0x${it.toString(16)}" }.orEmpty()
                "#${index.toString().padStart(2, '0')} $location$relative$source ($function$suffix)"
            }
        if (frames.isEmpty()) return ""
        return buildString {
            tombstone.message(6)?.let { signal ->
                val name = signal.string(2).orEmpty()
                val code = signal.string(4).orEmpty()
                val address = signal.varint(5)?.let { "0x${it.toString(16)}" }.orEmpty()
                append("signal ${signal.varint(1) ?: "?"} ($name), code ${signal.varint(3) ?: "?"} ($code)")
                if (address.isNotEmpty()) append(", fault addr $address")
                appendLine()
            }
            tombstone.string(7)?.takeIf { it.isNotBlank() }?.let { appendLine("abort message: $it") }
            tombstone.messages(9).firstOrNull()?.string(1)?.takeIf { it.isNotBlank() }?.let { appendLine("cause: $it") }
            append(frames.take(MAX_BACKTRACE_FRAMES).joinToString("\n"))
        }
    }

    private fun hostLogBelongsTo(exit: ApplicationExitInfo, hostFile: File): Boolean {
        if (exit.reason != ApplicationExitInfo.REASON_CRASH) return false
        val head = runCatching { hostFile.inputStream().use { String(readCapped(it, 4096), StandardCharsets.UTF_8) } }
            .getOrDefault("")
        val kind = Regex("(?m)^kind\\s*:\\s*(\\S+)").find(head)?.groupValues?.getOrNull(1)
        val pid = Regex("(?m)^pid\\s*:\\s*(\\d+)").find(head)?.groupValues?.getOrNull(1)?.toIntOrNull()
        val time = Regex("(?m)^time\\s*:\\s*(\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}\\.\\d{3})")
            .find(head)?.groupValues?.getOrNull(1)?.let { value ->
                runCatching { SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US).parse(value)?.time }.getOrNull()
            }
        return kind.equals("java", ignoreCase = true) && pid == exit.pid &&
            time != null && kotlin.math.abs(time - exit.timestamp) <= HOST_LOG_WINDOW_MS
    }

    private fun readCapped(input: InputStream, limit: Int): ByteArray {
        val out = ByteArrayOutputStream()
        val buffer = ByteArray(16 * 1024)
        while (out.size() < limit) {
            val read = input.read(buffer)
            if (read <= 0) break
            out.write(buffer, 0, read)
        }
        return out.toByteArray()
    }

    /** Minimal protobuf wire reader; only the tombstone fields needed for the crashing thread are decoded. */
    private class ProtoMessage private constructor(private val fields: List<Field>) {
        fun varint(number: Int): Long? = fields.firstOrNull { it.number == number && it.wire == 0 }?.value
        fun string(number: Int): String? = fields.firstOrNull { it.number == number && it.wire == 2 }
            ?.bytes?.let { String(it, StandardCharsets.UTF_8) }
        fun messages(number: Int): List<ProtoMessage> = fields.asSequence()
            .filter { it.number == number && it.wire == 2 }
            .mapNotNull { it.bytes?.let { bytes -> parse(bytes) } }
            .toList()
        fun message(number: Int): ProtoMessage? = messages(number).firstOrNull()

        private data class Field(val number: Int, val wire: Int, val value: Long = 0, val bytes: ByteArray? = null)

        companion object {
            fun parse(bytes: ByteArray): ProtoMessage? = runCatching {
                val fields = ArrayList<Field>()
                var offset = 0
                while (offset < bytes.size) {
                    val tagResult = readVarint(bytes, offset) ?: return null
                    val tag = tagResult.first
                    offset = tagResult.second
                    val number = (tag ushr 3).toInt()
                    val wire = (tag and 7).toInt()
                    if (number <= 0) return null
                    when (wire) {
                        0 -> {
                            val valueResult = readVarint(bytes, offset) ?: return null
                            fields += Field(number, wire, value = valueResult.first)
                            offset = valueResult.second
                        }
                        1 -> {
                            if (offset + 8 > bytes.size) return null
                            offset += 8
                        }
                        2 -> {
                            val lengthResult = readVarint(bytes, offset) ?: return null
                            val length = lengthResult.first
                            val start = lengthResult.second
                            if (length < 0 || length > bytes.size - start) return null
                            val end = start + length.toInt()
                            fields += Field(number, wire, bytes = bytes.copyOfRange(start, end))
                            offset = end
                        }
                        5 -> {
                            if (offset + 4 > bytes.size) return null
                            offset += 4
                        }
                        else -> return null
                    }
                }
                ProtoMessage(fields)
            }.getOrNull()

            private fun readVarint(bytes: ByteArray, start: Int): Pair<Long, Int>? {
                var value = 0L
                var shift = 0
                var offset = start
                while (offset < bytes.size && shift < 64) {
                    val byte = bytes[offset++].toInt() and 0xff
                    value = value or ((byte and 0x7f).toLong() shl shift)
                    if ((byte and 0x80) == 0) return value to offset
                    shift += 7
                }
                return null
            }
        }
    }

    private fun writeAtomically(target: File, text: String) {
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
    }

    private fun field(key: String, value: String): String = "${key.padEnd(11)}: $value"

    private fun timestamp(epochMs: Long): String =
        SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US).format(Date(epochMs))

    private fun appVersion(context: Context): String = runCatching {
        val info = context.packageManager.getPackageInfo(context.packageName, 0)
        "${info.versionName} (${info.longVersionCode})"
    }.getOrDefault("unknown")

    private fun device(): String =
        "${Build.MANUFACTURER} ${Build.MODEL} · Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})"
}
