package dev.pylon.loader.game.core

import android.content.Context
import android.content.res.AssetManager
import dev.pylon.loader.game.Log
import java.io.File
import java.io.FileNotFoundException

/**
 * 层：宿主层 / 运行时部署（:game）
 *
 * 把随 APK 走的运行时解到 app 私有目录（首次运行或版本变化时）：
 *   assets/pylon-dotnet/   → filesDir/pylon-dotnet/    BCL
 *   assets/pylon-managed/  → filesDir/pylon-managed/   托管入口程序集
 *   assets/pylon-interop/  → filesDir/pylon-interop/   代理程序集 + Il2CppInterop + Harmony 栈
 *
 * native（libcoreclr.so 等）不在这里解：它们必须在 APK 的 lib/arm64-v8a 里，Android 才允许 dlopen。
 */
internal object RuntimeDeploy {

    private const val ASSET_DOTNET = "pylon-dotnet"
    private const val ASSET_MANAGED = "pylon-managed"
    private const val ASSET_INTEROP = "pylon-interop"
    private const val DIR_DOTNET = "pylon-dotnet"
    private const val DIR_MANAGED = "pylon-managed"
    private const val DIR_INTEROP = "pylon-interop"
    private const val MARKER = ".deployed"
    const val MANAGED_DLL = "Pylon.Managed.dll"
    const val CONTRACT_DLL = "Pylon.Mod.Abstractions.dll"
    const val FRAMEWORK_DLL = "Pylon.ModLoader.dll"

    /**
     * 改动 assets 里的 BCL / 托管入口 / 代理程序集后必须 +1：标记里存的是版本号 + 两棵树的
     * 文件数，对不上就重解（只比版本会漏掉"忘了 +1"，只比个数会漏掉"换了个同名 dll"）。
     */
    private const val DEPLOY_VERSION = 22

    /** 随包内容（示例 Mod 等）按它判断"加载器升级了"。 */
    const val DEPLOY_VERSION_TAG = "version=$DEPLOY_VERSION"

    data class Layout(
        val dotnetDir: File,
        val managedDir: File,
        val interopDir: File,
        val entryAssembly: File,
    )

    /** 幂等：托管入口只在首次或版本变化时覆盖，免得踩掉开发时推进去的 DLL（见 tools/run-test.ps1）。 */
    fun ensureDeployed(context: Context): Layout {
        val dotnetDir = File(context.filesDir, DIR_DOTNET)
        val managedDir = File(context.filesDir, DIR_MANAGED)
        val interopDir = File(context.filesDir, DIR_INTEROP)
        val entry = File(managedDir, MANAGED_DLL)
        val contract = File(managedDir, CONTRACT_DLL)
        val framework = File(managedDir, FRAMEWORK_DLL)
        // 标记放 filesDir 根：它曾被放进 pylon-dotnet，而该目录的文件数正是判据 —— 计数永远差 1，
        // 于是每次启动都整树重解（把 tools/run-test.ps1 推进去的 DLL 也一起踩掉）。
        val marker = File(context.filesDir, MARKER)

        val assets = context.assets
        requireAssetTree(assets, ASSET_DOTNET)
        requireAssetTree(assets, ASSET_MANAGED)
        requireAssetTree(assets, ASSET_INTEROP)

        val expectedDotnet = countAssets(assets, ASSET_DOTNET)
        val expectedInterop = countAssets(assets, ASSET_INTEROP)
        val expectedManaged = fingerprint(assets, ASSET_MANAGED)
        val deployed = runCatching { marker.readText().trim() }.getOrDefault("")

        // BCL/代理靠版本号 + 文件数；托管三件套靠**指纹**（文件数+总字节）：只比版本时"改了框架忘了
        // +1"会一直跑设备上的旧 DLL（踩过两次）。代理目录**不做**字节校验：开发时常直接推 DLL 进
        // files/pylon-interop 做免重装测试（tools/run-test.ps1 -PushManaged 同思路），字节一变就重解会把推的盖掉。
        val coreReady = deployed.startsWith(DEPLOY_VERSION_TAG) &&
            deployed.contains("bcl=$expectedDotnet interop=$expectedInterop") &&
            countFiles(dotnetDir) == expectedDotnet &&
            countFiles(interopDir) == expectedInterop
        val managedReady = deployed.contains("managed=$expectedManaged") &&
            entry.isFile && contract.isFile && framework.isFile

        if (!coreReady) {
            Log.stage("dotnet", "部署运行时（$deployed → $DEPLOY_VERSION_TAG bcl=$expectedDotnet interop=$expectedInterop）")
            val startedAt = System.currentTimeMillis()
            // 先清空再解：只覆盖不删除的话，assets 里删掉的文件会永远留在设备上并被当作 TPA 成员加载。
            replaceTree(context, ASSET_DOTNET, dotnetDir)
            replaceTree(context, ASSET_INTEROP, interopDir)
            Log.stage(
                "dotnet",
                "BCL ${countFiles(dotnetDir)} 个文件、代理 ${countFiles(interopDir)} 个，" +
                    "耗时 ${(System.currentTimeMillis() - startedAt) / 1000}s"
            )
        }

        if (!managedReady) replaceTree(context, ASSET_MANAGED, managedDir)
        marker.writeText("$DEPLOY_VERSION_TAG bcl=$expectedDotnet interop=$expectedInterop managed=$expectedManaged\n")
        return Layout(dotnetDir, managedDir, interopDir, entry)
    }
    /** 资产树的指纹：文件数 + 总字节。字节必须**读流**统计 —— openFd().length 对压缩 asset 会抛异常，吞掉就恒为 0。 */
    private fun fingerprint(assets: AssetManager, assetPath: String, depth: Int = 0): String {
        if (depth > DEPTH_LIMIT) return "deep"
        val children = runCatching { assets.list(assetPath) }.getOrNull() ?: return "0:0"
        if (children.isEmpty()) return "1:${sizeOf(assets, assetPath)}"

        var files = 0
        var bytes = 0L
        for (child in children) {
            val parts = fingerprint(assets, "$assetPath/$child", depth + 1).split(':')
            files += parts[0].toIntOrNull() ?: 0
            bytes += parts[1].toLongOrNull() ?: 0L
        }
        return "$files:$bytes"
    }

    private fun sizeOf(assets: AssetManager, assetPath: String): Long = runCatching {
        assets.open(assetPath).use { input ->
            val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
            var total = 0L
            while (true) {
                val read = input.read(buffer)
                if (read <= 0) break
                total += read
            }
            total
        }
    }.getOrDefault(0L)

    /** assets 里没有这三棵树时给可读原因：只有 debug 变体预置，release 应由用户导入目录提供（docs/07）。 */
    private fun requireAssetTree(assets: AssetManager, assetPath: String) {
        val children = runCatching { assets.list(assetPath) }.getOrNull()
        if (children.isNullOrEmpty()) {
            throw IllegalStateException("本构建未内置运行时（assets/$assetPath 不存在）；release 形态应走用户导入")
        }
    }

    private fun replaceTree(context: Context, assetPath: String, dest: File) {
        if (!dest.deleteRecursively() && dest.exists()) {
            Log.w("清理旧目录失败：$dest")
        }
        copyAssetTree(context, assetPath, dest)
    }

    /** 递归拷贝 assets 目录。 */
    private fun copyAssetTree(context: Context, assetPath: String, dest: File) {
        val children = context.assets.list(assetPath) ?: emptyArray()
        if (children.isEmpty()) {
            // list 为空有歧义（文件 / 空目录 / 路径不存在），用 open 失败来区分并给出真实原因。
            dest.parentFile?.mkdirs()
            try {
                context.assets.open(assetPath).use { input ->
                    dest.outputStream().use { output -> input.copyTo(output, DEFAULT_BUFFER_SIZE) }
                }
            } catch (e: FileNotFoundException) {
                throw IllegalStateException("assets/$assetPath 既不是文件也不是目录", e)
            }
            return
        }
        dest.mkdirs()
        for (child in children) copyAssetTree(context, "$assetPath/$child", File(dest, child))
    }

    private fun countAssets(assets: AssetManager, assetPath: String, depth: Int = 0): Int {
        if (depth > DEPTH_LIMIT) return 0
        val children = runCatching { assets.list(assetPath) }.getOrNull() ?: return 0
        if (children.isEmpty()) return 1
        return children.sumOf { countAssets(assets, "$assetPath/$it", depth + 1) }
    }

    private fun countFiles(dir: File, depth: Int = 0): Int {
        if (depth > DEPTH_LIMIT) return 0
        val children = dir.listFiles() ?: return 0
        return children.sumOf { if (it.isDirectory) countFiles(it, depth + 1) else 1 }
    }

    /** 这三棵树都是平铺的，深度上限只是防御性上限。 */
    private const val DEPTH_LIMIT = 8
}
