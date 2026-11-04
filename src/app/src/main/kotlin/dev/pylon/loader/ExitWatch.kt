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
            if (hostModified > 0L && hostModified >= exit.timestamp - HOST_LOG_WINDOW_MS) {
                // 宿主已留下 Java 堆栈，不重复写。
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

    private fun writeReport(context: Context, exit: ApplicationExitInfo, kind: CrashKind): File? {
        val target = CrashReports.reportFile(context)
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
            val trace = sanitizeTrace(readTrace(exit))
            if (trace.isBlank()) {
                appendLine("（系统没有提供这次的 tombstone / 堆栈）")
            } else {
                appendLine("--- backtrace（符号帧） ---")
                val frames = extractFrames(trace)
                if (frames.isEmpty()) {
                    appendLine("（系统没有给出符号化帧：libil2cpp.so 等发行版 .so 不带符号表，崩溃线程只有地址没有函数名）")
                } else {
                    frames.take(MAX_BACKTRACE_FRAMES).forEachIndexed { i, f ->
                        appendLine("#${i.toString().padStart(2, '0')} $f")
                    }
                    if (frames.size > MAX_BACKTRACE_FRAMES) {
                        appendLine("…（共 ${frames.size} 帧，仅列前 $MAX_BACKTRACE_FRAMES）")
                    }
                }
                appendLine()
                appendLine("--- 原始 trace（已清洗二进制字节，前 8KB） ---")
                appendLine(trace.take(8 * 1024).trimEnd())
            }
            appendLine()
            appendLine("--- 说明 ---")
            appendLine("宿主进程被内核终止时 Java 层的未捕获异常处理器不会执行，这份记录来自系统的进程退出原因。")
            appendLine("完整 tombstone 在 /data/tombstones/（需要 root）；logcat 里搜 \"Fatal signal\" 也能看到同一段。")
        }
        return runCatching {
            writeAtomically(target, text)
            target
        }.onFailure {
            Log.e("写异常退出记录失败", it)
        }.getOrNull()
    }

    private fun readTrace(exit: ApplicationExitInfo): String = runCatching {
        exit.traceInputStream?.use { input -> readCapped(input, MAX_TRACE_BYTES) } ?: ""
    }.getOrElse { "（读取系统记录失败：${it.javaClass.simpleName}）" }

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

    private fun readCapped(input: InputStream, limit: Int): String {
        val out = ByteArrayOutputStream()
        val buffer = ByteArray(16 * 1024)
        while (out.size() < limit) {
            val read = input.read(buffer)
            if (read <= 0) break
            out.write(buffer, 0, read)
        }
        return out.toString("UTF-8")
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
