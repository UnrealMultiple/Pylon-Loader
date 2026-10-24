package dev.pylon.loader.game

import android.content.Context
import dev.pylon.loader.common.RuntimeLayout
import dev.pylon.loader.game.core.PylonCore
import dev.pylon.loader.game.core.RuntimeDeploy
import java.io.File

/**
 * 把运行时接通到本进程：解出游戏 dex 并注入 → 按序 dlopen 内置游戏 .so → 内核进场。
 *
 * 顺序由三条实测结论决定，改任何一条都会退化成 native 崩溃（tombstone 见 docs/07）：
 * ① 必须注入**系统给我们的那个** PathClassLoader：只有它注册过 ART 的 native loader namespace，
 *    自建 DexClassLoader 加载任何 .so 都会 `Check failed: class_loader_allocator != nullptr`；
 * ② 必须带游戏 dex：libmain.so 的 JNI_OnLoad 会对游戏的 com.unity3d.player.NativeLoader
 *    调 RegisterNatives，找不到就 SIGABRT；
 * ③ 运行时来源：游戏 .so 随 APK 内置（jniLibs → nativeLibraryDir）、游戏 assets 随 APK 内置
 *    （bin/Data）、游戏 dex 内置于 assets/pylon-game/classes.dex（启动时解到 filesDir 再注入）。
 */
internal class GameRuntimeLoader(private val context: Context) {

    data class Outcome(
        val loaded: Boolean,
        val classLoader: ClassLoader? = null,
        /** 内核（libpylon.so）是否已就绪启动。false 不影响游戏启动，只表示本次没有 Mod 能力。 */
        val kernelStarted: Boolean = false,
        val reason: String? = null,
    )

    fun load(
        refSpecs: List<String> = emptyList(),
        probe: Boolean = false,
    ): Outcome {
        // dex 注入必须先于 .so：libmain 的 JNI_OnLoad 要在 classpath 上找游戏类。
        val layout = try {
            RuntimeDeploy.ensureDeployed(context)
        } catch (t: Throwable) {
            return fail("内置运行时解包失败：${t.javaClass.simpleName}: ${t.message}", t)
        }
        if (!layout.gameDex.isFile) {
            return fail("游戏 dex 未随包提供：${layout.gameDex}（检查 assets/pylon-game/classes.dex）")
        }
        val classLoader = GameDexInjector.inject(context, layout.gameDex.absolutePath)
            ?: return fail("无法把游戏 dex 注入系统 classloader")

        // 游戏 .so 随 APK 内置：安装时已解到 nativeLibraryDir，按序绝对路径加载。
        val nativeDir = File(context.applicationInfo.nativeLibraryDir)
        Log.stage("host", "内置原生库目录=${nativeDir.absolutePath}")
        RuntimeLayout.NATIVE_LIB_LOAD_ORDER.forEachIndexed { index, libName ->
            val file = File(nativeDir, libName)
            if (!file.isFile) {
                return fail("本构建的 APK 内没有 $libName（构建未内置完整运行时，检查 refs/lib/arm64-v8a）")
            }
            try {
                System.load(file.absolutePath)
                Log.stage(
                    "host",
                    "已加载 $libName（第 ${index + 1} 个，${file.length() / 1024} KB）",
                )
            } catch (t: Throwable) {
                return fail(
                    "加载 $libName 失败（第 ${index + 1} 个，共 ${RuntimeLayout.NATIVE_LIB_LOAD_ORDER.size} 个）：" +
                        "${t.javaClass.simpleName}: ${t.message}",
                    t,
                )
            }
        }

        // 内核进场：游戏 .so 已按序加载完、Unity 还没起；内核只绑定 il2cpp 符号并起探测线程。
        val kernelStarted = probe && PylonCore.start(refSpecs)
        if (!probe) Log.w("本次不启动内核探测（HostOptions.probe=false）：Mod 能力关闭，游戏照常跑")
        if (!kernelStarted) {
            // 内核没起来不算宿主失败：游戏照常跑，只是没有 Mod 能力（M2 的语义是"停用"）。
            Log.w("libpylon.so 未就绪，本次运行没有 Mod 能力")
        } else {
            // 测试 Mod 只在带有该文件的变体中加载：缺文件是预期情况，文件存在但加载失败必须保留警告。
            val testMod = File(nativeDir, "libpylontestmod.so")
            if (testMod.isFile) {
                runCatching { System.load(testMod.absolutePath) }
                    .onSuccess { Log.stage("host", "测试 Mod libpylontestmod.so 已加载（APK nativeLibraryDir）") }
                    .onFailure { Log.w("测试 Mod 已提供但加载失败：${it.message}", it) }
            } else {
                Log.i("测试 Mod 未随包提供（当前变体跳过）")
            }
        }

        Log.stage("host", "运行时装载完成，classLoader=${classLoader.javaClass.name}")
        return Outcome(true, classLoader, kernelStarted)
    }

    private fun fail(reason: String, cause: Throwable? = null): Outcome {
        if (cause != null) Log.e(reason, cause) else Log.e(reason)
        return Outcome(false, reason = reason)
    }
}
