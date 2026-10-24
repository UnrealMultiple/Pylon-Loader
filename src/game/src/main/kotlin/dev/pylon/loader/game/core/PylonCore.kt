package dev.pylon.loader.game.core

import dev.pylon.loader.game.Log

internal object PylonCore {

    const val EXPECTED_VERSION_MAJOR = 0
    const val EXPECTED_VERSION_MINOR = 2

    const val DEFAULT_AWAIT_TIMEOUT_MS = 45_000

    var started = false
        private set

    private var libraryLoaded = false
    private var coreClrPrepared = false
    private var startFailure: String? = null

    fun start(refs: List<String>): Boolean {
        if (started) return true
        if (startFailure != null) {
            Log.w("内核本次运行已判定不可用，跳过重试：$startFailure")
            return false
        }
        return try {
            if (!libraryLoaded) {
                System.loadLibrary("pylon")
                libraryLoaded = true
            }
            val version = nativeVersion()
            Log.stage("core", "libpylon.so 已加载：内核版本 ${version shr 16}.${version and 0xFFFF}")
            if (nativeStart(refs.toTypedArray())) {
                started = true
                Log.stage("core", "nativeStart 成功（清单 ${refs.size} 条）")
                true
            } else {
                startFailure = "内核无法绑定 il2cpp 符号（nativeStart 返回 false）"
                Log.e("nativeStart 失败：$startFailure（Mod 将停用）")
                false
            }
        } catch (t: Throwable) {
            startFailure = "加载 libpylon.so 失败：${t.javaClass.simpleName}: ${t.message}"
            Log.e(startFailure!!, t)
            false
        }
    }

    fun awaitFailureReason(timeoutMs: Int = DEFAULT_AWAIT_TIMEOUT_MS): String? {
        if (!started) return startFailure ?: "内核未启动，Mod 停用"
        return try {
            nativeAwaitReport(timeoutMs)
        } catch (t: Throwable) {
            "等待内核结果时异常：${t.javaClass.simpleName}: ${t.message}"
        }
    }

    fun report(): String = if (started) runCatching { nativeReport() }.getOrDefault("") else ""

    fun testModProbe(): String =
        if (started) runCatching { nativeTestModProbe() }.getOrDefault("") else "内核未加载"

    fun markGameThread() {
        if (started) runCatching { nativeMarkGameThread() }
    }

    fun isGameThread(): Boolean = started && runCatching { nativeIsGameThread() }.getOrDefault(false)

    /// 起 CoreCLR 并解析入口，不调用它；与 IL2CPP 无关，可与引擎启动并行。
    fun prepareCoreClr(
        coreDir: String,
        dotnetDir: String,
        entryAssemblyPath: String,
        logDir: String,
        modsDir: String,
        interopDir: String,
        entryAssemblyName: String = "Pylon.Managed",
        entryType: String = "Pylon.Managed.Entrypoint",
        entryMethod: String = "Start",
    ): Boolean {
        if (coreClrPrepared) return true
        return try {
            System.loadLibrary("coreclr")
            Log.stage("dotnet", "libcoreclr.so 已加载，开始初始化 CoreCLR")
            coreClrPrepared = nativePrepareCoreClr(
                entryAssemblyPath, coreDir, dotnetDir,
                entryAssemblyName, entryType, entryMethod, logDir, modsDir, interopDir,
            )
            Log.stage("dotnet", "CoreCLR 初始化${if (coreClrPrepared) "完成" else "失败"}")
            coreClrPrepared
        } catch (t: Throwable) {
            Log.e("初始化 CoreCLR 失败：${t.javaClass.simpleName}: ${t.message}", t)
            false
        }
    }

    /// 执行托管入口；入口会建 interop，必须在内核就绪之后调用。
    fun runCoreClrEntry(): Boolean {
        if (!coreClrPrepared) {
            Log.e("CoreCLR 未初始化，跳过托管入口（Mod 不会加载）")
            return false
        }
        return try {
            nativeRunCoreClrEntry()
        } catch (t: Throwable) {
            Log.e("执行托管入口失败：${t.javaClass.simpleName}: ${t.message}", t)
            false
        }
    }

    fun callManagedEntry(method: String): Boolean {
        if (!coreClrPrepared) return false
        return try {
            nativeCallManagedEntry(method)
        } catch (t: Throwable) {
            Log.e("调用托管入口 $method 失败：${t.javaClass.simpleName}: ${t.message}", t)
            false
        }
    }

    fun phase(): Int = if (started) nativePhase() else PHASE_IDLE

    fun phaseName(): String = when (phase()) {
        PHASE_IDLE -> "Idle"
        PHASE_KERNEL_LOADED -> "KernelLoaded"
        PHASE_ENGINE_READY -> "EngineReady"
        PHASE_REFS_VALIDATED -> "RefsValidated"
        PHASE_FAILED -> "Failed"
        else -> "?"
    }

    fun shutdown() {
        if (started) runCatching { nativeShutdown() }
    }

    private external fun nativeVersion(): Long
    private external fun nativeStart(refs: Array<String>): Boolean
    private external fun nativeAwaitReport(timeoutMs: Int): String?
    private external fun nativeReport(): String
    private external fun nativeTestModProbe(): String
    private external fun nativeMarkGameThread()
    private external fun nativeIsGameThread(): Boolean
    private external fun nativePrepareCoreClr(
        entryAssemblyPath: String,
        coreDir: String,
        dotnetDir: String,
        entryAssemblyName: String,
        entryType: String,
        entryMethod: String,
        logDir: String,
        modsDir: String,
        interopDir: String,
    ): Boolean
    private external fun nativeRunCoreClrEntry(): Boolean
    private external fun nativeCallManagedEntry(method: String): Boolean
    private external fun nativePhase(): Int
    private external fun nativeShutdown()

    private const val PHASE_IDLE = 0
    private const val PHASE_KERNEL_LOADED = 1
    private const val PHASE_ENGINE_READY = 2
    private const val PHASE_REFS_VALIDATED = 3
    private const val PHASE_FAILED = 4
}
