package dev.pylon.loader.game.core

import android.content.Context
import dev.pylon.loader.game.Log
import java.io.File
import java.util.concurrent.CountDownLatch

/// 托管运行时准备：解包 assets、扫 Mod 目录、起 CoreCLR。不碰 IL2CPP，所以与引擎启动并行。
internal class ManagedRuntime private constructor() {

    data class Prepared(val layout: RuntimeDeploy.Layout, val modsRoots: String)

    private val latch = CountDownLatch(1)

    @Volatile
    private var prepared: Prepared? = null

    fun await(): Prepared? {
        latch.await()
        return prepared
    }

    private fun prepare(context: Context) {
        try {
            val layout = RuntimeDeploy.ensureDeployed(context)
            if (!layout.entryAssembly.isFile) {
                Log.e("托管入口不存在：${layout.entryAssembly}")
                return
            }

            val modsDir = ModStore.ensure(context)
            val (folders, loose) = ModStore.inventory(modsDir)
            Log.stage("mods", "Mod 目录：$modsDir（子目录 ${folders.size} 个，松散 dll ${loose.size} 个）")

            val packageCacheRoot = ModPackageCache.prepare(context, modsDir)
            val modsRoots = buildList {
                add(modsDir.absolutePath)
                packageCacheRoot?.let { add(it.absolutePath) }
            }.joinToString(File.pathSeparator)
            Log.stage("mods", "托管扫描根：$modsRoots")

            val started = PylonCore.prepareCoreClr(
                coreDir = layout.managedDir.absolutePath,
                dotnetDir = layout.dotnetDir.absolutePath,
                entryAssemblyPath = layout.entryAssembly.absolutePath,
                logDir = context.filesDir.absolutePath,
                modsDir = modsRoots,
                interopDir = layout.interopDir.absolutePath,
            )
            if (started) prepared = Prepared(layout, modsRoots)
        } catch (t: Throwable) {
            Log.e("准备托管运行时失败：${t.javaClass.simpleName}: ${t.message}", t)
        } finally {
            latch.countDown()
        }
    }

    companion object {
        fun start(context: Context): ManagedRuntime {
            val task = ManagedRuntime()
            Thread({ task.prepare(context) }, "pylon-managed-prepare").apply { isDaemon = true }.start()
            return task
        }
    }
}
