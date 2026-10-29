package dev.pylon.loader.common

import java.io.File
import java.util.Properties

/**
 * 导入清单：记录运行时来自哪个包/版本/ABI 及每个文件的 sha256，
 * 宿主层据此判断"导入是否完整、是否同一构建"，而不是猜文件是否存在。
 * 用 Properties 而非手写 JSON：无第三方依赖、自带转义，路径带空格也不会写坏。
 */
class ImportManifest(
    val gamePackage: String,
    val gameVersionCode: Long,
    val gameVersionName: String,
    val abi: String,
    val gameSourceDir: String,
    val importedAtEpochMs: Long,
    val nativeLibs: Map<String, FileEntry>,
    val dataFiles: Map<String, FileEntry>,
) {

    data class FileEntry(
        val relativePath: String,
        val size: Long,
        val sha256: String,
    )

    val totalBytes: Long
        get() = nativeLibs.values.sumOf { it.size } + dataFiles.values.sumOf { it.size }

    fun writeTo(manifestFile: File) {
        require(manifestFile.parentFile?.isDirectory == true || manifestFile.parentFile?.mkdirs() == true) {
            "无法创建清单目录：${manifestFile.parentFile}"
        }
        manifestFile.outputStream().use { toProperties().store(it, "Pylon Loader runtime import manifest") }
    }

    private fun toProperties(): Properties = Properties().apply {
        setProperty(KEY_FORMAT, FORMAT_VERSION.toString())
        setProperty(KEY_PACKAGE, gamePackage)
        setProperty(KEY_VERSION_CODE, gameVersionCode.toString())
        setProperty(KEY_VERSION_NAME, gameVersionName)
        setProperty(KEY_ABI, abi)
        setProperty(KEY_SOURCE_DIR, gameSourceDir)
        setProperty(KEY_IMPORTED_AT, importedAtEpochMs.toString())
        nativeLibs.forEach { (name, entry) -> setProperty("$NATIVE_PREFIX$name", entry.toValue()) }
        dataFiles.forEach { (path, entry) -> setProperty("$DATA_PREFIX$path", entry.toValue()) }
    }

    private fun FileEntry.toValue(): String = "$size|$sha256"

    companion object {
        const val FORMAT_VERSION = 1
        private const val KEY_FORMAT = "format"
        private const val KEY_PACKAGE = "game.package"
        private const val KEY_VERSION_CODE = "game.versionCode"
        private const val KEY_VERSION_NAME = "game.versionName"
        private const val KEY_ABI = "game.abi"
        private const val KEY_SOURCE_DIR = "game.sourceDir"
        private const val KEY_IMPORTED_AT = "imported.at"
        private const val NATIVE_PREFIX = "native."
        private const val DATA_PREFIX = "data."

        /** 任何缺失或格式问题都返回 null，不抛异常：宿主层的语义是"清单不可用就不放行"。 */
        fun readFrom(manifestFile: File): ImportManifest? {
            if (!manifestFile.isFile) return null
            val props = Properties()
            return try {
                manifestFile.inputStream().use { props.load(it) }
                val natives = LinkedHashMap<String, FileEntry>()
                val datas = LinkedHashMap<String, FileEntry>()
                for (key in props.stringPropertyNames()) {
                    val entry = parseEntry(key, props.getProperty(key) ?: continue) ?: continue
                    when {
                        key.startsWith(NATIVE_PREFIX) -> natives[key.removePrefix(NATIVE_PREFIX)] = entry
                        key.startsWith(DATA_PREFIX) -> datas[key.removePrefix(DATA_PREFIX)] = entry
                    }
                }
                ImportManifest(
                    gamePackage = props.getProperty(KEY_PACKAGE) ?: return null,
                    gameVersionCode = props.getProperty(KEY_VERSION_CODE)?.toLongOrNull() ?: return null,
                    gameVersionName = props.getProperty(KEY_VERSION_NAME) ?: "",
                    abi = props.getProperty(KEY_ABI) ?: "",
                    gameSourceDir = props.getProperty(KEY_SOURCE_DIR) ?: "",
                    importedAtEpochMs = props.getProperty(KEY_IMPORTED_AT)?.toLongOrNull() ?: 0L,
                    nativeLibs = natives,
                    dataFiles = datas,
                )
            } catch (t: Throwable) {
                null
            }
        }

        /** 按已知前缀整体切除，不能按 '.' 切：键在第一个点之后就是相对路径本身。 */
        private fun parseEntry(key: String, value: String): FileEntry? {
            val relativePath = when {
                key.startsWith(NATIVE_PREFIX) -> key.removePrefix(NATIVE_PREFIX)
                key.startsWith(DATA_PREFIX) -> key.removePrefix(DATA_PREFIX)
                else -> return null
            }
            if (relativePath.isEmpty()) return null
            val parts = value.split('|')
            if (parts.size != 2) return null
            val size = parts[0].toLongOrNull() ?: return null
            return FileEntry(relativePath = relativePath, size = size, sha256 = parts[1])
        }
    }
}
