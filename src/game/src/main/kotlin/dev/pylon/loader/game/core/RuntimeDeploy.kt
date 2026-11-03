package dev.pylon.loader.game.core

import android.content.Context
import android.content.res.AssetManager
import dev.pylon.loader.game.Log
import java.io.File
import java.io.FileNotFoundException

/// 把随 APK 走的运行时解到 app 私有目录（native 除外：lib/arm64-v8a 才允许 dlopen）。
/// assets 只随 APK 变化，所以"本 APK 安装时间戳没变"时不做任何扫描。
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
    const val NATIVE_API_DLL = "Pylon.NaviteAPI.dll"

    /// 改了 assets 里的东西就让版本号 +1，整树重解。
    private const val DEPLOY_VERSION = 26

    private const val DEPTH_LIMIT = 8

    data class Layout(
        val dotnetDir: File,
        val managedDir: File,
        val interopDir: File,
        val entryAssembly: File,
    )

    fun ensureDeployed(context: Context): Layout {
        val managedDir = File(context.filesDir, DIR_MANAGED)
        val layout = Layout(
            dotnetDir = File(context.filesDir, DIR_DOTNET),
            managedDir = managedDir,
            interopDir = File(context.filesDir, DIR_INTEROP),
            entryAssembly = File(managedDir, MANAGED_DLL),
        )
        val markerFile = File(context.filesDir, MARKER)
        val marker = Marker.read(markerFile)
        val apkStamp = installStamp(context)
        val versionChanged = marker.version != DEPLOY_VERSION
        val sameApk = apkStamp != 0L && !versionChanged && marker.apkStamp == apkStamp
        if (sameApk && marker.matchesDeployed(layout) && layout.hasEntry()) return layout

        val assets = context.assets
        requireAssetTree(assets, ASSET_DOTNET)
        requireAssetTree(assets, ASSET_MANAGED)
        requireAssetTree(assets, ASSET_INTEROP)

        val startedAt = System.currentTimeMillis()
        val bclFiles = countAssets(assets, ASSET_DOTNET)
        val interopFiles = countAssets(assets, ASSET_INTEROP)
        val managedFingerprint =
            if (sameApk) marker.managedFingerprint else fingerprint(assets, ASSET_MANAGED)

        val deployedBcl = countFiles(layout.dotnetDir)
        val deployedInterop = countFiles(layout.interopDir)
        val redeployCore = versionChanged || deployedBcl != bclFiles || deployedInterop != interopFiles
        if (redeployCore) {
            Log.stage(
                "dotnet",
                "部署 BCL/代理（设备 $deployedBcl/$bclFiles、$deployedInterop/$interopFiles，版本 ${marker.version} → $DEPLOY_VERSION）",
            )
            replaceTree(context, ASSET_DOTNET, layout.dotnetDir)
            replaceTree(context, ASSET_INTEROP, layout.interopDir)
        }

        val redeployManaged = versionChanged || !layout.hasEntry() ||
            (!sameApk && managedFingerprint != marker.managedFingerprint)
        if (redeployManaged) replaceTree(context, ASSET_MANAGED, managedDir)

        if (redeployCore || redeployManaged) {
            Log.stage(
                "dotnet",
                "部署完成：BCL $bclFiles 个、代理 $interopFiles 个，耗时 ${System.currentTimeMillis() - startedAt}ms",
            )
        }

        val updated = Marker(DEPLOY_VERSION, apkStamp, bclFiles, interopFiles, managedFingerprint)
        if (updated != marker) markerFile.writeText(updated.encode())
        return layout
    }

    private data class Marker(
        val version: Int = 0,
        val apkStamp: Long = 0L,
        val bclFiles: Int = -1,
        val interopFiles: Int = -1,
        val managedFingerprint: String = "",
    ) {
        fun encode(): String =
            "version=$version apk=$apkStamp bcl=$bclFiles interop=$interopFiles managed=$managedFingerprint"

        fun matchesDeployed(layout: Layout): Boolean =
            countFiles(layout.dotnetDir) == bclFiles && countFiles(layout.interopDir) == interopFiles

        companion object {
            private val PATTERN =
                Regex("""version=(\d+) apk=(\d+) bcl=(\d+) interop=(\d+) managed=(\S+)""")

            fun read(file: File): Marker {
                val text = runCatching { file.readText() }.getOrDefault("")
                val groups = PATTERN.find(text)?.groupValues ?: return Marker()
                return Marker(
                    version = groups[1].toIntOrNull() ?: 0,
                    apkStamp = groups[2].toLongOrNull() ?: 0L,
                    bclFiles = groups[3].toIntOrNull() ?: -1,
                    interopFiles = groups[4].toIntOrNull() ?: -1,
                    managedFingerprint = groups[5],
                )
            }
        }
    }

    private fun installStamp(context: Context): Long = runCatching {
        context.packageManager.getPackageInfo(context.packageName, 0).lastUpdateTime
    }.getOrDefault(0L)

    private fun Layout.hasEntry(): Boolean =
        entryAssembly.isFile &&
            File(managedDir, CONTRACT_DLL).isFile &&
            File(managedDir, FRAMEWORK_DLL).isFile &&
            File(managedDir, NATIVE_API_DLL).isFile

    private fun fingerprint(assets: AssetManager, assetPath: String, depth: Int = 0): String {
        if (depth > DEPTH_LIMIT) return "0:0"
        val children = runCatching { assets.list(assetPath) }.getOrNull() ?: return "0:0"
        if (children.isEmpty()) return "1:${crcOf(assets, assetPath)}"

        var files = 0
        var crc = 0L
        for (child in children) {
            val parts = fingerprint(assets, "$assetPath/$child", depth + 1).split(':')
            files += parts[0].toIntOrNull() ?: 0
            crc = (crc * 31 + (parts[1].toLongOrNull() ?: 0L)) and 0x7FFFFFFF
        }
        return "$files:$crc"
    }

    private fun crcOf(assets: AssetManager, assetPath: String): Long = runCatching {
        val crc = java.util.zip.CRC32()
        assets.open(assetPath).use { input ->
            val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
            while (true) {
                val read = input.read(buffer)
                if (read <= 0) break
                crc.update(buffer, 0, read)
            }
        }
        crc.value
    }.getOrDefault(0L)

    private fun requireAssetTree(assets: AssetManager, assetPath: String) {
        val children = runCatching { assets.list(assetPath) }.getOrNull()
        if (children.isNullOrEmpty()) {
            throw IllegalStateException("本构建未内置运行时（assets/$assetPath 不存在）；release 形态应走用户导入")
        }
    }

    private fun replaceTree(context: Context, assetPath: String, dest: File) {
        if (!dest.deleteRecursively() && dest.exists()) Log.w("清理旧目录失败：$dest")
        copyAssetTree(context, assetPath, dest)
    }

    private fun copyAssetTree(context: Context, assetPath: String, dest: File) {
        val children = context.assets.list(assetPath) ?: emptyArray()
        if (children.isEmpty()) {
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
}
