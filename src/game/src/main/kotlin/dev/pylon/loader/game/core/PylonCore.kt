package dev.pylon.loader.game.core

import dev.pylon.loader.game.Log

/**
 * 层：宿主层 / 内核桥（:game）
 *
 * 只做两件事：① 把"要校验哪些名字"（数据）交给内核并启动它；② 之后取结果
 * （通过 / 停用原因 / 逐条报告）。
 * "什么时候可以碰 IL2CPP"不在此层决定：内核探测线程按"先静置、再稀疏轮询
 * `il2cpp_domain_get()`"判定（docs/08 §3）——`il2cpp_init` 执行期间调 API 会 SIGSEGV，
 * 这个约束越少的调用方知道越好。
 */
internal object PylonCore {

    /** 内核版本 0.2 = M2。与 src/core/src/jni/PylonJni.cpp 的 kVersionMinor 保持一致。 */
    const val EXPECTED_VERSION_MAJOR = 0
    const val EXPECTED_VERSION_MINOR = 2

    /**
     * 内核探测的静置时间。上游实测值 10s（"游戏到达主菜单"的量级）是安全默认；
     * 调小只用于开发期快速迭代——它直接决定"多早开始碰 IL2CPP"，越小越危险。
     */
    const val DEFAULT_SETTLE_MS = 25_000

    /** 等结果的上限：静置 10s + 12 次 × 2.5s ≈ 40s，再留一点余量。 */
    const val DEFAULT_AWAIT_TIMEOUT_MS = 45_000

    /** libpylon.so 是否已加载（只说明 .so 在位，不代表探测已启动）。 */
    private var libraryLoaded = false

    /** nativeStart 是否成功。只有它为 true，本对象其余接口才有意义。 */
    var started = false
        private set

    /** 首次启动失败的原因，后续 start() 直接复用（别让第二次调用谎报成功）。 */
    private var startFailure: String? = null

    /**
     * 由 GameRuntimeLoader 在游戏 .so 之后、UnityPlayer 创建之前调用。
     * @param refs 引用清单（见 RefSpecs）；内核不理解里面的名字，只是照着解析。
     */
    fun start(refs: List<String>, settleMs: Int = DEFAULT_SETTLE_MS): Boolean {
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
            if (nativeStart(refs.toTypedArray(), settleMs)) {
                started = true
                Log.stage(
                    "core",
                    "nativeStart 成功：符号已绑定，探测已启动（静置 ${settleMs}ms；清单 ${refs.size} 条）"
                )
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

    /**
     * 阻塞等结果。**只应由后台线程调用**（内核探测最多要 40s）。
     * @return null = 引用校验全部通过；非 null = 停用 Mod 的原因
     */
    fun awaitFailureReason(timeoutMs: Int = DEFAULT_AWAIT_TIMEOUT_MS): String? {
        if (!started) return startFailure ?: "内核未启动，Mod 停用"
        return try {
            nativeAwaitReport(timeoutMs)
        } catch (t: Throwable) {
            "等待内核结果时异常：${t.javaClass.simpleName}: ${t.message}"
        }
    }

    /** 逐条结果文本，用于日志。 */
    fun report(): String = if (started) runCatching { nativeReport() }.getOrDefault("") else ""

    /**
     * C# 方法绑定自检：真的调几个托管方法（值类型返回、字符串参数与返回、故意抛异常），
     * 拼成多行文本。**必须在引用校验通过之后调**（那时才 attach 过）。
     * 理由："能解析到方法"和"能把方法调起来、把返回值拿回来"是两件事，后者要单独证明，
     * 后面 M3/M4 才敢直接往上盖。
     */
    fun bindSelfTest(): String =
        if (started) runCatching { nativeBindSelfTest() }.getOrDefault("") else "内核未加载"

    /**
     * 测试 Mod 探针：调独立 .so（tools/testmod）的入口，它用**导出 ABI** 反过来调内核——
     * "Mod → C++"方向的端到端证据（动态库 → 动态链接器解析符号 → IL2CPP）。
     * **只能在游戏线程上调**（托管方法/字符串的限制）。
     */
    fun testModProbe(): String =
        if (started) runCatching { nativeTestModProbe() }.getOrDefault("") else "内核未加载"

    /**
     * 登记"当前线程就是游戏线程（UnityMain）"。
     * 由宿主用一个投递到 UnityMain 的小任务调用（`UnityPlayer.queueGLThreadEvent`）。
     * 在那之前内核拒绝一切托管调用（宁可功能不生效，也不要把游戏搞崩）。
     */
    fun markGameThread() {
        if (started) runCatching { nativeMarkGameThread() }
    }

    fun isGameThread(): Boolean = started && runCatching { nativeIsGameThread() }.getOrDefault(false)

    /**
     * 起 CoreCLR 并调用托管入口。必须在引用校验通过、内核 attach 之后调用；
     * **不要求游戏线程**：托管入口由内核放到独立 worker 线程执行（避开 UnityMain 的栈上限），
     * 真正需要游戏线程的是之后加载 Mod（callManagedEntry("StartMods")）。
     * 注意加载顺序：先 System.loadLibrary("coreclr")，让 libcoreclr.so 落在本 app 的
     * nativeLibraryDir 命名空间里（内核只做 dlopen RTLD_NOLOAD，不替系统决定从哪加载）。
     *
     * @param coreDir   我们自己的托管程序集目录（进 TPA，也是 APP_CONTEXT_BASE_DIRECTORY）
     * @param dotnetDir BCL 目录（SYSTEM_CORELIB_DIRECTORY）
     */
    fun startCoreClr(
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
        if (!started) {
            Log.e("内核未加载，无法启动 CoreCLR")
            return false
        }
        return try {
            System.loadLibrary("coreclr")
            Log.stage("dotnet", "libcoreclr.so 已加载，开始启动 CoreCLR")
            val ok = nativeStartCoreClr(
                entryAssemblyPath, coreDir, dotnetDir,
                entryAssemblyName, entryType, entryMethod, logDir, modsDir, interopDir,
            )
            Log.stage("dotnet", "nativeStartCoreClr=$ok")
            ok
        } catch (t: Throwable) {
            Log.e("启动 CoreCLR 失败：${t.javaClass.simpleName}: ${t.message}", t)
            false
        }
    }

    /**
     * 在**当前线程**上调用托管入口类型上的静态无参方法。
     * 调用方负责在游戏线程上（Mod 的 Load() 会碰 IL2CPP）。
     */
    fun callManagedEntry(method: String): Boolean {
        if (!started) return false
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

    // --- JNI（名字与 src/core/src/jni/PylonJni.cpp 一一对应） ---
    private external fun nativeVersion(): Long
    private external fun nativeStart(refs: Array<String>, settleMs: Int): Boolean
    private external fun nativeAwaitReport(timeoutMs: Int): String?
    private external fun nativeReport(): String
    private external fun nativeBindSelfTest(): String
    private external fun nativeTestModProbe(): String
    private external fun nativeMarkGameThread()
    private external fun nativeIsGameThread(): Boolean
    private external fun nativeStartCoreClr(
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

    private external fun nativeCallManagedEntry(method: String): Boolean
    private external fun nativePhase(): Int
    private external fun nativeShutdown()

    /** 与 src/core/src/boot/Boot.h 的 Phase 枚举一一对应。 */
    private const val PHASE_IDLE = 0
    private const val PHASE_KERNEL_LOADED = 1
    private const val PHASE_ENGINE_READY = 2
    private const val PHASE_REFS_VALIDATED = 3
    private const val PHASE_FAILED = 4
}