package dev.pylon.loader.game

import android.content.Context
import android.content.pm.ApplicationInfo
import dev.pylon.loader.common.ImportManifest
import dev.pylon.loader.common.RuntimeLayout
import dev.pylon.loader.game.core.PylonCore
import java.io.File

/**
 * 把运行时接通到本进程：注入游戏 dex → 按序 dlopen 四个 .so → 内核进场。
 *
 * 顺序由三条实测结论决定，改任何一条都会退化成 native 崩溃（tombstone 见 docs/07）：
 * ① 必须注入**系统给我们的那个** PathClassLoader：只有它注册过 ART 的 native loader namespace，
 *    自建 DexClassLoader 加载任何 .so 都会 `Check failed: class_loader_allocator != nullptr`；
 * ② 必须带游戏 dex：libmain.so 的 JNI_OnLoad 会对游戏的 com.unity3d.player.NativeLoader
 *    调 RegisterNatives，找不到就 SIGABRT；
 * ③ `loadLibrary` 传**库名**而非文件名，否则去找 `liblibc++_shared.so.so`。
 *
 * 运行时来源（M5 用户导入形态）：游戏 .so 优先从导入目录（filesDir/runtime/lib）绝对路径 System.load，
 * 并把目录追加进 classloader 的 native 搜索路径供 Unity 自己 loadLibrary("il2cpp")；游戏 assets 由
 * GameAssets 挂载所选游戏的 base.apk；宿主 APK 只留内核/CoreCLR，APK 内副本仅作 -PpylonStageRefs=true
 * 的本地回退；导入清单仍逐文件核大小，防止半导入/混了另一个构建。
 */
internal class GameRuntimeLoader(private val context: Context) {

    data class Outcome(
        val loaded: Boolean,
        val classLoader: ClassLoader? = null,
        val gameApplicationInfo: ApplicationInfo? = null,
        /** 内核（libpylon.so）是否已就绪启动。false 不影响游戏启动，只表示本次没有 Mod 能力。 */
        val kernelStarted: Boolean = false,
        val reason: String? = null,
    )

    fun load(
        refSpecs: List<String> = emptyList(),
        settleMs: Int = PylonCore.DEFAULT_SETTLE_MS,
        probe: Boolean = false,
        gamePackage: String = RuntimeLayout.GAME_PACKAGE,
    ): Outcome {
        val runtimeDir = File(context.filesDir, RuntimeLayout.IMPORT_DIR_NAME)
        val manifest = ImportManifest.readFrom(File(runtimeDir, RuntimeLayout.MANIFEST_FILE_NAME))
            ?: return fail("没有可用的导入清单：$runtimeDir/${RuntimeLayout.MANIFEST_FILE_NAME}")

        if (manifest.gameVersionCode != RuntimeLayout.GAME_VERSION_CODE) {
            return fail("导入的运行时版本不符：清单 ${manifest.gameVersionCode}，需要 ${RuntimeLayout.GAME_VERSION_CODE}")
        }
        if (manifest.gamePackage.isNotEmpty() && manifest.gamePackage != gamePackage) {
            return fail("导入的运行时来自 ${manifest.gamePackage}，当前选择的是 $gamePackage（请重新导入）")
        }

        // 清单只证明"导入那一刻成功过"，所以这里按清单核一遍文件是否还在、大小是否一致。
        verifyImportedTree(manifest, runtimeDir)?.let { return fail(it) }

        val gameInfo = try {
            context.packageManager.getPackageInfo(gamePackage, 0).applicationInfo
        } catch (t: Throwable) {
            return fail("取 $gamePackage 的 ApplicationInfo 失败：${t.message}", t)
        } ?: return fail("$gamePackage 没有 ApplicationInfo")
        Log.stage("host", "游戏包可访问：${gameInfo.packageName}(${manifest.gameVersionName})")

        // 把所选游戏 APK 的 assets 挂到同一个 AssetManager 上；debug 预置资源仍在时作为回退。
        GameAssets.mountGameApk(context, gameInfo)

        val classLoader = GameDexInjector.inject(context, gameInfo)
            ?: return fail("无法把游戏 dex 注入系统 classloader")

        // 导入目录里的 .so 优先进本进程；失败再回退到宿主编进 APK 的那份。
        val importedLibDir = File(runtimeDir, RuntimeLayout.NATIVE_SUBDIR)
        Log.stage("host", "导入原生库目录=${importedLibDir.absolutePath}")
        if (importedLibDir.isDirectory) {
            NativeLibraryPath.add(classLoader, importedLibDir)
        }

        RuntimeLayout.NATIVE_LIB_LOAD_ORDER.forEachIndexed { index, libName ->
            val imported = File(importedLibDir, libName)
            if (manifest.nativeLibs[libName] != null && imported.isFile) {
                try {
                    // 绝对路径加载：不经过 classloader 的 nativeLibraryDir 搜索路径。
                    System.load(imported.absolutePath)
                    Log.stage(
                        "host",
                        "已从导入目录加载 $libName（第 ${index + 1} 个，${imported.length() / 1024} KB）",
                    )
                    return@forEachIndexed
                } catch (t: Throwable) {
                    Log.w(
                        "导入目录加载 $libName 失败，回退宿主 APK：${t.javaClass.simpleName}: ${t.message}",
                    )
                }
            }

            // 回退路径：宿主 APK 的 nativeLibraryDir（系统 classloader 的 namespace 内）。
            val nativeDir = File(context.applicationInfo.nativeLibraryDir)
            val hostCopy = File(nativeDir, libName)
            if (!hostCopy.isFile) {
                return fail(
                    "本构建的 APK 内没有 $libName，导入目录里也没有可用副本" +
                        "（release 形态需要用户导入的运行时进加载路径，见 docs/07）"
                )
            }
            // 宿主内的库与导入的那份不同构建时，混用会出现偶发 SIGSEGV，这里留证据。
            manifest.nativeLibs[libName]?.let { imported ->
                if (imported.size != hostCopy.length()) {
                    Log.w(
                        "注意：$libName 与导入的游戏库可能不是同一构建" +
                            "（APK ${hostCopy.length()} B ≠ 清单 ${imported.size} B）"
                    )
                }
            }
            try {
                // 传"库名"而非文件名：loadLibrary0 会自己拼 lib<name>.so。
                classLoader.loadLibrary(libName.toLibraryName())
                Log.stage("host", "已加载 $libName（第 ${index + 1} 个，${hostCopy.length() / 1024} KB）")
            } catch (t: Throwable) {
                return fail(
                    "加载 $libName 失败（第 ${index + 1} 个，共 ${RuntimeLayout.NATIVE_LIB_LOAD_ORDER.size} 个）：" +
                        "${t.javaClass.simpleName}: ${t.message}",
                    t,
                )
            }
        }

        // 内核进场：游戏 .so 已按序加载完、Unity 还没起；内核只绑定 il2cpp 符号并起探测线程。
        // probe=false 不启动探测：时机靠固定静置猜测，落在 il2cpp_init 窗口内会 SIGSEGV（docs/15）。
        val kernelStarted = probe && PylonCore.start(refSpecs, settleMs)
        if (!probe) Log.w("本次不启动内核探测（HostOptions.probe=false）：Mod 能力关闭，游戏照常跑")
        if (!kernelStarted) {
            // 内核没起来不算宿主失败：游戏照常跑，只是没有 Mod 能力（M2 的语义是"停用"）。
            Log.w("libpylon.so 未就绪，本次运行没有 Mod 能力")
        } else {
            // 测试 Mod（tools/testmod）只在 debug 变体里，加载失败属正常。
            runCatching { classLoader.loadLibrary("pylontestmod") }
                .onSuccess { Log.stage("host", "测试 Mod libpylontestmod.so 已加载（debug 变体）") }
                .onFailure { Log.w("测试 Mod 未随包提供（release 变体属正常）：${it.message}") }
        }

        Log.stage("host", "运行时装载完成，classLoader=${classLoader.javaClass.name}")
        // 让 Unity 自己的 loadLibrary/路径解析也优先看导入目录（如果它读 ApplicationInfo 的字段）。
        if (RuntimeLayout.NATIVE_LIB_LOAD_ORDER.all { File(importedLibDir, it).isFile }) {
            context.applicationInfo.nativeLibraryDir = importedLibDir.absolutePath
            Log.stage("host", "ApplicationInfo.nativeLibraryDir → ${importedLibDir.absolutePath}")
        }
        return Outcome(true, classLoader, gameInfo, kernelStarted)
    }

    /** @return null = 校验通过；否则是给用户看的原因。 */
    private fun verifyImportedTree(manifest: ImportManifest, runtimeDir: File): String? {
        val libDir = File(runtimeDir, RuntimeLayout.NATIVE_SUBDIR)
        for (lib in RuntimeLayout.NATIVE_LIB_LOAD_ORDER) {
            val file = File(libDir, lib)
            if (!file.isFile) return "导入不完整：缺少 $lib（请重新导入）"
            val expected = manifest.nativeLibs[lib] ?: continue
            if (file.length() != expected.size) {
                return "导入不完整：$lib 大小 ${file.length()} B ≠ 清单 ${expected.size} B（请重新导入）"
            }
        }
        val metadata = File(runtimeDir, "${RuntimeLayout.DATA_SUBDIR}/Managed/Metadata/global-metadata.dat")
        if (!metadata.isFile) return "导入不完整：缺少 global-metadata.dat（请重新导入）"
        return null
    }

    /** 通过反射调用 ClassLoader.loadLibrary：它不是公开 API（实现在 java.lang.Runtime 里）。 */
    private fun ClassLoader.loadLibrary(libName: String) {
        val method = Class.forName("java.lang.Runtime")
            .getDeclaredMethod("loadLibrary0", ClassLoader::class.java, String::class.java)
        method.isAccessible = true
        method.invoke(Runtime.getRuntime(), this, libName)
    }

    private fun String.toLibraryName(): String = removePrefix("lib").removeSuffix(".so")

    private fun fail(reason: String, cause: Throwable? = null): Outcome {
        if (cause != null) Log.e(reason, cause) else Log.e(reason)
        return Outcome(false, reason = reason)
    }
}
