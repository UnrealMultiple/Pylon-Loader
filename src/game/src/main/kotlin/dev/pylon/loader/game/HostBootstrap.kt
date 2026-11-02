package dev.pylon.loader.game

import dev.pylon.loader.game.core.ManagedRuntime
import dev.pylon.loader.game.core.PylonCore
import dev.pylon.loader.game.core.RefSpecs

internal class HostBootstrap(
    private val activity: GameActivity,
    private val options: HostOptions,
    private val unityPlayer: () -> UnityPlayerRef?,
) {

    @Volatile
    private var cancelled = false

    private var watchThread: Thread? = null
    private var kernelAvailable = false

    @Volatile
    private var managedRuntime: ManagedRuntime? = null

    fun loadRuntime(): Boolean {
        val outcome = GameRuntimeLoader(activity).load(
            refSpecs = RefSpecs.resolve(customSpecs = options.refsProbe, bogus = options.bogusRef),
            probe = options.probe,
            gamePackage = options.gamePackage,
        )
        if (!outcome.loaded) {
            activity.showStartupFailure(outcome.reason ?: "未知原因")
            return false
        }
        kernelAvailable = outcome.kernelStarted
        if (kernelAvailable && options.coreClr) {
            managedRuntime = ManagedRuntime.start(activity)
            Log.stage("boot", "托管运行时准备已并行启动")
        }
        return true
    }

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
                if (options.modProbe) Log.stage("boot", PylonCore.testModProbe())
            }
        )
        if (!posted) {
            Log.e("无法投递到游戏线程：托管调用会被内核拒绝（不崩，但功能不生效）")
            return
        }
        if (options.coreClr) runManagedEntry()
    }

    private fun runManagedEntry() {
        val task = managedRuntime
        if (task == null) {
            Log.w("托管运行时没有开始准备，跳过 Mod 装载")
            return
        }
        if (task.await() == null) {
            Log.e("托管运行时准备失败，本次没有 Mod 能力")
            return
        }
        if (cancelled) return
        if (!PylonCore.runCoreClrEntry()) return

        val posted = unityPlayer()?.runOnGameThread(
            Runnable {
                Log.stage("mods", "在游戏线程上调托管 StartMods（isGameThread=${PylonCore.isGameThread()}）")
                PylonCore.callManagedEntry("StartMods")
            }
        ) ?: false
        if (!posted) Log.w("拿不到 UnityPlayer，Mod 加载跳过")
    }
}
