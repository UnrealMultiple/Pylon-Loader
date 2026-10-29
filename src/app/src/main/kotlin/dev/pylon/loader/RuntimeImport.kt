package dev.pylon.loader

import android.content.Context
import android.content.pm.PackageManager
import dev.pylon.loader.common.ImportManifest
import dev.pylon.loader.common.RuntimeLayout
import java.io.File
import java.security.MessageDigest
import java.util.zip.ZipEntry
import java.util.zip.ZipFile

/**
 * 把已安装正版的运行时搬进 app 私有目录（docs/02 形态 B、docs/03 第 3 步）：
 * 从 APK 解出 lib/arm64-v8a 原生库与 assets/bin/Data 文件，逐文件 sha256 写清单。
 * 边界：不改游戏 APK、不加载 .so（宿主层的事）、不碰 Mod 目录。
 * 进度：边写边算哈希（单遍），按已写入字节 / 清单总字节回调，避免哈希阶段卡在 100% 前。
 */
object RuntimeImport {

    data class Result(
        val success: Boolean,
        val runtimeDir: File?,
        val manifest: ImportManifest?,
        val error: String? = null,
        /** 已有同版本清单、本次直接跳过。 */
        val skipped: Boolean = false,
    )

    /** 导入进度。bytesTotal <= 0 表示总大小未知，界面应显示不确定进度。 */
    data class Progress(
        val stage: String,
        val bytesDone: Long,
        val bytesTotal: Long,
    ) {
        val fraction: Float?
            get() = if (bytesTotal > 0L) {
                (bytesDone.toFloat() / bytesTotal.toFloat()).coerceIn(0f, 1f)
            } else {
                null
            }
    }

    fun runtimeDir(context: Context): File = File(context.filesDir, RuntimeLayout.IMPORT_DIR_NAME)

    fun manifestFile(context: Context): File =
        File(runtimeDir(context), RuntimeLayout.MANIFEST_FILE_NAME)

    fun nativeLibDir(context: Context): File =
        File(runtimeDir(context), RuntimeLayout.NATIVE_SUBDIR)

    /**
     * 可重复调用；已有同版本清单时跳过，避免每次启动重解 175MB。
     * @param onProgress 在调用线程（后台线程）回调，节流约 90ms 一次；不要在里面直接碰 View。
     */
    fun import(
        context: Context,
        force: Boolean = false,
        gamePackage: String = RuntimeLayout.GAME_PACKAGE,
        onProgress: ((Progress) -> Unit)? = null,
    ): Result {
        // 进程级重入保护：导入要跑几十秒，期间 Activity 可能被重建（每实例的 flag 挡不住），
        // 两个线程同时往同一棵临时树里写会互相踩。
        if (!importing.compareAndSet(false, true)) {
            Log.w("已有导入在进行，拒绝本次请求")
            return Result(false, null, null, "已有导入在进行")
        }
        try {
            return doImport(context, gamePackage, force, onProgress)
        } finally {
            importing.set(false)
        }
    }

    private val importing = java.util.concurrent.atomic.AtomicBoolean(false)

    private fun doImport(
        context: Context,
        gamePackage: String,
        force: Boolean,
        onProgress: ((Progress) -> Unit)?,
    ): Result {
        val pm = context.packageManager

        // API 30+ 需要 manifest 声明 <queries>，否则这里查不到，报错要写清包名以免误导排查。
        val pkgInfo = try {
            @Suppress("DEPRECATION")
            pm.getPackageInfo(gamePackage, 0)
        } catch (e: PackageManager.NameNotFoundException) {
            val msg = "未找到 $gamePackage（若确认已安装，检查 manifest 的 <queries> 声明）"
            Log.e(msg, e)
            return Result(false, null, null, msg)
        }

        val versionCode = pkgInfo.longVersionCode
        val sourceDir = pkgInfo.applicationInfo?.sourceDir
            ?: return fail("包 $gamePackage 没有 sourceDir")

        // 版本不符就拒绝：混版本不会立刻报错，只会损坏存档（docs/01 硬约束）。
        if (versionCode != RuntimeLayout.GAME_VERSION_CODE) {
            return fail(
                "安装包版本不匹配：需要 ${RuntimeLayout.GAME_VERSION_NAME}(${RuntimeLayout.GAME_VERSION_CODE})，" +
                    "实际 ${pkgInfo.versionName}($versionCode)"
            )
        }

        val runtimeDir = runtimeDir(context)
        val manifestFile = File(runtimeDir, RuntimeLayout.MANIFEST_FILE_NAME)

        if (!force) {
            val existing = ImportManifest.readFrom(manifestFile)
            if (existing != null &&
                existing.gamePackage == gamePackage &&
                existing.gameVersionCode == versionCode &&
                existing.abi == RuntimeLayout.ABI
            ) {
                Log.stage("import", "已有同版本运行时清单，跳过导入（force=true 可强制重导）")
                return Result(true, runtimeDir, existing, skipped = true)
            }
        }

        Log.stage("import", "开始导入：$gamePackage ${pkgInfo.versionName}($versionCode) → $runtimeDir")

        // 解到临时树，清单也在临时树里写完，最后整棵换入：中途失败/被杀时旧的有效导入不受影响。
        val stagingDir = File(context.filesDir, "${RuntimeLayout.IMPORT_DIR_NAME}.tmp")
        return try {
            if (stagingDir.exists() && !stagingDir.deleteRecursively()) {
                return fail("无法清空临时导入目录：$stagingDir")
            }
            val nativeDir = File(stagingDir, RuntimeLayout.NATIVE_SUBDIR).apply { mkdirs() }
            val dataDir = File(stagingDir, RuntimeLayout.DATA_SUBDIR).apply { mkdirs() }

            ZipFile(sourceDir).use { zip ->
                val scan = scanEntries(zip)
                val reporter = ProgressReporter(scan.totalBytes, onProgress)
                reporter.stageNow("读取安装包…")

                val nativeLibs = importNativeLibs(zip, nativeDir, reporter)
                if (nativeLibs.size != RuntimeLayout.NATIVE_LIB_LOAD_ORDER.size) {
                    return fail(
                        "原生库不完整：期望 ${RuntimeLayout.NATIVE_LIB_LOAD_ORDER.size} 个，" +
                            "实际 ${nativeLibs.size} 个（${nativeLibs.keys.joinToString()}）"
                    )
                }

                val dataFiles = importDataFiles(zip, dataDir, scan.dataFileCount, reporter)
                if (dataFiles.isEmpty()) {
                    return fail("未从 APK 解出任何 ${RuntimeLayout.APK_ASSETS_PREFIX} 下的资源")
                }

                reporter.stageNow("写入校验清单…")
                val manifest = ImportManifest(
                    gamePackage = gamePackage,
                    gameVersionCode = versionCode,
                    gameVersionName = pkgInfo.versionName ?: "",
                    abi = RuntimeLayout.ABI,
                    gameSourceDir = sourceDir,
                    importedAtEpochMs = System.currentTimeMillis(),
                    nativeLibs = nativeLibs,
                    dataFiles = dataFiles,
                )
                manifest.writeTo(File(stagingDir, RuntimeLayout.MANIFEST_FILE_NAME))

                reporter.stageNow("切换导入目录…")
                // 换入：先移除旧的，再改名（同一文件系统内 rename 是原子的）。
                if (runtimeDir.exists() && !runtimeDir.deleteRecursively()) {
                    return fail("无法移除旧导入目录：$runtimeDir")
                }
                if (!stagingDir.renameTo(runtimeDir)) {
                    return fail("无法把新导入换入 $runtimeDir")
                }

                reporter.complete()
                Log.stage(
                    "import",
                    "导入完成：原生库 ${nativeLibs.size} 个 + 资源 ${dataFiles.size} 个，" +
                        "共 ${manifest.totalBytes / 1024 / 1024} MB"
                )
                Result(true, runtimeDir, manifest)
            }
        } catch (t: Throwable) {
            Log.e("导入失败（旧导入保持不变）", t)
            Result(false, null, null, "导入失败：${t.javaClass.simpleName}: ${t.message}")
        } finally {
            // 成功时临时树已经改名走了；失败时清掉半成品，别让它白占 175MB。
            if (stagingDir.exists() && !stagingDir.deleteRecursively()) {
                Log.w("临时导入树清理失败：$stagingDir")
            }
        }
    }

    // ------------------------------------------------------------------
    // 扫描与解包
    // ------------------------------------------------------------------

    private data class Scan(
        /** 0 表示压缩包里存在 size 未知的条目，界面按不确定进度处理。 */
        val totalBytes: Long,
        val dataFileCount: Int,
    )

    /** 先扫中央目录拿总字节数与资源文件数，只读元数据不解压，进度条从第一秒就有确定分母。 */
    private fun scanEntries(zip: ZipFile): Scan {
        val libEntries = RuntimeLayout.NATIVE_LIB_LOAD_ORDER
            .mapTo(HashSet()) { "${RuntimeLayout.APK_LIB_DIR}/$it" }
        val dataPrefix = "${RuntimeLayout.APK_ASSETS_PREFIX}/"

        var total = 0L
        var dataCount = 0
        var unknownSize = false

        val entries = zip.entries()
        while (entries.hasMoreElements()) {
            val entry = entries.nextElement()
            if (entry.isDirectory) continue
            val isLib = entry.name in libEntries
            val isData = entry.name.startsWith(dataPrefix)
            if (!isLib && !isData) continue

            if (entry.size < 0L) {
                unknownSize = true
            } else {
                total += entry.size
            }
            if (isData) dataCount++
        }
        return Scan(if (unknownSize) 0L else total, dataCount)
    }

    private fun importNativeLibs(
        zip: ZipFile,
        nativeDir: File,
        reporter: ProgressReporter,
    ): Map<String, ImportManifest.FileEntry> {
        val result = LinkedHashMap<String, ImportManifest.FileEntry>()
        val total = RuntimeLayout.NATIVE_LIB_LOAD_ORDER.size
        RuntimeLayout.NATIVE_LIB_LOAD_ORDER.forEachIndexed { index, libName ->
            val entry = zip.getEntry("${RuntimeLayout.APK_LIB_DIR}/$libName")
            if (entry == null) {
                Log.e("APK 内缺少 ${RuntimeLayout.APK_LIB_DIR}/$libName")
                return@forEachIndexed
            }
            reporter.stageNow("解出原生库 ${index + 1}/$total · $libName")
            val dest = File(nativeDir, libName)
            val hash = copyEntry(zip, entry, dest, executable = true) { reporter.advance(it) }
            result[libName] = ImportManifest.FileEntry(
                relativePath = "${RuntimeLayout.NATIVE_SUBDIR}/$libName",
                size = dest.length(),
                sha256 = hash,
            )
            Log.stage("import", "  lib: $libName (${dest.length() / 1024} KB, sha256=${hash.take(12)}…)")
        }
        return result
    }

    /** 解出 assets/bin/Data 下全部文件，刻意不做大小过滤：data.unity3d/resources.resource 缺一即黑屏，且难从日志反推。 */
    private fun importDataFiles(
        zip: ZipFile,
        dataDir: File,
        expectedCount: Int,
        reporter: ProgressReporter,
    ): Map<String, ImportManifest.FileEntry> {
        val result = LinkedHashMap<String, ImportManifest.FileEntry>()
        val prefix = "${RuntimeLayout.APK_ASSETS_PREFIX}/"
        var index = 0
        val entries = zip.entries()
        while (entries.hasMoreElements()) {
            val entry = entries.nextElement()
            if (entry.isDirectory) continue
            if (!entry.name.startsWith(prefix)) continue

            val relativeToData = entry.name.removePrefix(prefix)
            if (relativeToData.isEmpty()) continue
            index++

            reporter.stage("解出游戏资源 $index/${expectedCount.coerceAtLeast(index)} · ${relativeToData.substringAfterLast('/')}")
            val dest = File(dataDir, relativeToData)
            dest.parentFile?.mkdirs()
            val hash = copyEntry(zip, entry, dest, executable = false) { reporter.advance(it) }

            val key = RuntimeLayout.normalizeRelativePath(relativeToData)
            result[key] = ImportManifest.FileEntry(
                relativePath = "${RuntimeLayout.DATA_SUBDIR}/$key",
                size = dest.length(),
                sha256 = hash,
            )
        }
        return result
    }

    /** 复制 + 算 SHA-256 一遍完成，返回 hex 摘要。旧实现先复制再 `Hash.sha256(file)` 读第二遍；
     * 单遍把 175MB IO 减半，进度也不会停在"哈希中"没有反馈。 */
    private fun copyEntry(
        zip: ZipFile,
        entry: ZipEntry,
        dest: File,
        executable: Boolean,
        onBytes: (Long) -> Unit,
    ): String {
        val digest = MessageDigest.getInstance("SHA-256")
        zip.getInputStream(entry).use { input ->
            dest.outputStream().use { output ->
                val buffer = ByteArray(COPY_BUFFER_SIZE)
                while (true) {
                    val read = input.read(buffer)
                    if (read <= 0) break
                    digest.update(buffer, 0, read)
                    output.write(buffer, 0, read)
                    onBytes(read.toLong())
                }
            }
        }
        if (executable) {
            dest.setReadable(true, false)
            dest.setExecutable(true, false)
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun fail(reason: String): Result {
        Log.e(reason)
        return Result(false, null, null, reason)
    }

    /** 进度节流器。回调在导入线程，界面自己 post 到主线程；这里只保证不每 8KB 跨线程发一次。 */
    private class ProgressReporter(
        private val totalBytes: Long,
        private val onProgress: ((Progress) -> Unit)?,
    ) {
        private var doneBytes = 0L
        private var lastEmitAt = 0L
        private var stage = "准备导入…"

        fun stage(text: String) {
            stage = text
            emit(force = false)
        }

        fun stageNow(text: String) {
            stage = text
            emit(force = true)
        }

        fun advance(bytes: Long) {
            doneBytes += bytes
            emit(force = false)
        }

        fun complete() {
            doneBytes = maxOf(doneBytes, totalBytes)
            stage = "完成"
            emit(force = true)
        }

        private fun emit(force: Boolean) {
            val sink = onProgress ?: return
            val now = System.nanoTime()
            if (!force && now - lastEmitAt < EMIT_INTERVAL_NS) return
            lastEmitAt = now
            sink(Progress(stage, doneBytes, totalBytes))
        }
    }

    private const val COPY_BUFFER_SIZE = 256 * 1024
    private const val EMIT_INTERVAL_NS = 90_000_000L
}
