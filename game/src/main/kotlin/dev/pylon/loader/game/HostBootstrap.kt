package dev.pylon.loader.game

import dev.pylon.loader.game.core.ModPackageCache
import dev.pylon.loader.game.core.ModStore
import dev.pylon.loader.game.core.PylonCore
import dev.pylon.loader.game.core.RefSpecs
import dev.pylon.loader.game.core.RuntimeDeploy
import java.io.File

/**
 * 层：宿主层 / 启动编排
 *
 * 分两段以消除"后台线程读到的 UnityPlayer 还是 null"的次序问题：
 * ① [loadRuntime] 可在主线程外调用（要 dlopen 57MB 的 libil2cpp.so、处理游戏 dex，放主线程会卡住首帧甚至 ANR）；
 * ② [beginWatch] 必须在 UnityPlayer 创建之后由主线程调用，之后才可能投递到游戏线程。
 *
 * 之后：后台线程等内核跑完引用校验 → 投递游戏线程登记 → 起 CoreCLR → 再投递 StartMods。
 * 线程纪律见 docs/02 §8、docs/11。
 */
internal class HostBootstrap(
    private val activity: GameActivity,
    private val options: HostOptions,
    private val unityPlayer: () -> UnityPlayerRef?,
) {

    @Volatile
    private var cancelled = false

    private var watchThread: Thread? = null
    private var kernelAvailable = false

    /** ① 装载游戏运行时。失败返回 false（原因已展示并留痕）。可在任意线程调用。 */
    fun loadRuntime(): Boolean {
        val outcome = GameRuntimeLoader(activity).load(
            refSpecs = RefSpecs.resolve(customSpecs = options.refsProbe, bogus = options.bogusRef),
            settleMs = options.settleMs,
            probe = options.probe,
            gamePackage = options.gamePackage,
        )
        if (!outcome.loaded) {
            activity.showStartupFailure(outcome.reason ?: "未知原因")
            return false
        }
        kernelAvailable = outcome.kernelStarted
        return true
    }

    /** ② UnityPlayer 就绪后由主线程调用，开始等内核结果。 */
    fun beginWatch() {
        if (!kernelAvailable) {
            Log.w("内核未启动，本次运行没有 Mod 能力")
            return
        }
        watchThread = Thread({
            val failure = PylonCore.awaitFailureReason()
            if (cancelled) {
                Log.w("宿主已销毁，放弃本次 Mod 装载")
                return@Thread
            }

            val report = PylonCore.report()
            if (report.isNotBlank()) Log.stage("refs", "逐条结果：\n$report")
            if (failure != null) {
                // 引用校验失败 = 停用 Mod，游戏照常跑（M2 的验收标准）。
                Log.e("停用 Mod：$failure")
                Log.stage("boot", "阶段=${PylonCore.phaseName()}")
                return@Thread
            }
            Log.stage("boot", "引用校验通过，阶段=${PylonCore.phaseName()}（Mod 可用）")
            onKernelReady()
        }, "pylon-kernel-watch")
        watchThread!!.isDaemon = true
        watchThread!!.start()
    }

    /** 宿主销毁时调用：阻止后续投递与部署（内核等待最长 45s，不能白跑完还去动已销毁的 Activity）。 */
    fun cancel() {
        cancelled = true
        watchThread?.interrupt()
    }

    private fun onKernelReady() {
        val player = unityPlayer()
        if (player == null) {
            Log.e("UnityPlayer 不可用，放弃登记游戏线程与 Mod 装载")
            return
        }
        val posted = player.runOnGameThread(
            Runnable {
                PylonCore.markGameThread()
                Log.stage("boot", "内核位于游戏线程上：isGameThread=${PylonCore.isGameThread()}")
                if (options.bindTest) Log.stage("boot", PylonCore.bindSelfTest())
                if (options.modProbe) Log.stage("boot", PylonCore.testModProbe())
            }
        )
        if (!posted) {
            Log.e("无法投递到游戏线程：托管调用会被内核拒绝（不崩，但功能不生效）")
            return
        }
        if (options.coreClr) startManagedRuntime()
    }

    /** 重活（解运行时、CoreCLR 初始化）刻意留在本线程，不占游戏线程。 */
    private fun startManagedRuntime() {
        if (cancelled) return
        runCatching {
            val layout = RuntimeDeploy.ensureDeployed(activity)
            if (!layout.entryAssembly.isFile) {
                Log.e("托管入口不存在：${layout.entryAssembly}")
                return
            }

            val modsDir = ModStore.ensure(activity)
            // 不再把随包示例 Mod 写进 Mods/：那是用户自己的目录，加载器不该往里放东西。
            // 示例 Mod 的源码在 samples/，要试就自己把 dll 丢进 Mods/。
            val (folders, loose) = ModStore.inventory(modsDir)
            Log.stage("mods", "Mod 目录：$modsDir（子目录 ${folders.size} 个，松散 dll ${loose.size} 个）")

            // .pmod 单文件包：解到内部缓存，托管层会把它作为第二个 Mod 根扫描。
            val packageCacheRoot = ModPackageCache.prepare(activity, modsDir)
            val modsRoots = buildList {
                add(modsDir.absolutePath)
                packageCacheRoot?.let { add(it.absolutePath) }
            }.joinToString(File.pathSeparator)
            Log.stage("mods", "托管扫描根：$modsRoots")

            val started = PylonCore.startCoreClr(
                coreDir = layout.managedDir.absolutePath,
                dotnetDir = layout.dotnetDir.absolutePath,
                entryAssemblyPath = layout.entryAssembly.absolutePath,
                logDir = activity.filesDir.absolutePath,
                modsDir = modsRoots,
                interopDir = layout.interopDir.absolutePath,
            )
            if (!started || cancelled) return

            val posted = unityPlayer()?.runOnGameThread(
                Runnable {
                    Log.stage("mods", "在游戏线程上调托管 StartMods（isGameThread=${PylonCore.isGameThread()}）")
                    PylonCore.callManagedEntry("StartMods")
                }
            ) ?: false
            if (!posted) Log.w("拿不到 UnityPlayer，Mod 加载跳过")
        }.onFailure { Log.e("托管运行时启动失败：${it.javaClass.simpleName}: ${it.message}", it) }
    }
}
