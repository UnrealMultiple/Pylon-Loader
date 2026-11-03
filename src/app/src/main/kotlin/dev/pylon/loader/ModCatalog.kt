package dev.pylon.loader

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import org.json.JSONObject
import java.io.File
import java.util.Locale

internal enum class ModKind { FOLDER, LOOSE_DLL, PACKAGE }

/**
 * 一个可加载的 Mod。字段与 `Pylon.Managed/ModLoader.cs` 的发现规则一致：
 * 主程序集优先同名 dll、目录里唯一 dll；根下松散 dll 用文件名当 id；
 * `.pmod` 是 zip 包，宿主启动时解到缓存目录。`icon` 仅供启动器列表展示，为空用文件夹图标。
 */
internal data class ModInfo(
    val id: String,
    val name: String,
    val version: String,
    val source: String,
    val kind: ModKind,
    val dll: File?,
    val packageFile: File?,
    val icon: Bitmap?,
    val enabled: Boolean,
    val manifestEnabled: Boolean,
    val error: String? = null,
)

/**
 * 层的桥：启动器（:app）读写 `Mods/.pylon/selection.json`，宿主（:game）在 `ModLoader.Prepare`
 * 里读同一份；不写各 Mod 自己的 manifest.json，避免踩掉用户文件的 id/依赖/版本。
 * 格式（C# 侧只认这两个字段）：{"version": 1, "enabled": {"FrameMod": true}}
 * `enabled` 未记录的 Mod 回落到 manifest.json 的 enabled（默认 true），新装的 Mod 默认启用。
 */
internal object ModCatalog {

    private const val SELECTION_DIR = ".pylon"
    private const val SELECTION_FILE = "selection.json"
    private const val FORMAT_VERSION = 1
    const val PACKAGE_EXTENSION = "pmod"
    const val MANIFEST_NAME = "manifest.json"

    fun scan(modsDir: File): List<ModInfo> {
        if (!modsDir.isDirectory) return emptyList()
        val selection = readSelection(modsDir)
        val result = mutableListOf<ModInfo>()

        modsDir.listFiles { file -> file.isDirectory && file.name != SELECTION_DIR }
            ?.sortedBy { it.name.lowercase(Locale.US) }
            ?.forEach { dir ->
                findMainDll(dir)?.let { dll ->
                    result += readMod(dir.name, dir.name, dll, ModKind.FOLDER, selection)
                }
            }

        modsDir.listFiles { file ->
            file.isFile && file.extension.equals("dll", ignoreCase = true)
        }
            ?.sortedBy { it.name.lowercase(Locale.US) }
            ?.forEach { dll ->
                result += readMod(dll.name, dll.nameWithoutExtension, dll, ModKind.LOOSE_DLL, selection)
            }

        modsDir.listFiles { file ->
            file.isFile && file.extension.equals(PACKAGE_EXTENSION, ignoreCase = true)
        }
            ?.sortedBy { it.name.lowercase(Locale.US) }
            ?.forEach { packageFile ->
                result += readPackage(packageFile, selection)
            }

        return result.sortedBy { it.id.lowercase(Locale.US) }
    }

    fun setEnabled(modsDir: File, id: String, enabled: Boolean) {
        val values = readSelection(modsDir).toMutableMap()
        values[id] = enabled
        writeSelection(modsDir, values)
    }

    /** 删除 Mod（目录、松散 dll 或 .pmod），并清掉它的选择记录。 */
    fun delete(modsDir: File, mod: ModInfo): Boolean {
        val target = when (mod.kind) {
            ModKind.PACKAGE -> mod.packageFile
            ModKind.LOOSE_DLL -> mod.dll
            ModKind.FOLDER -> mod.dll?.parentFile
        } ?: return false
        val removed = if (target.isDirectory) target.deleteRecursively() else target.delete()
        if (removed) forget(modsDir, mod.id)
        return removed
    }

    /**
     * 把 .pmod 解包为文件夹 Mod（v2 容器 → 真实目录）。成功后删除原包，返回目标目录名。
     * 解包是"解包功能"而不是加载必需步骤：加载永远走内存，这里只是给想改文件的用户一个出口。
     */
    fun unpack(modsDir: File, mod: ModInfo): String {
        val packageFile = mod.packageFile ?: throw IllegalArgumentException("不是单文件包")
        val target = File(modsDir, mod.id)
        if (target.exists()) throw IllegalStateException("已存在同名目录：${mod.id}")

        PmodArchive.Reader(packageFile).use { reader ->
            for ((path, entry) in reader.entries) {
                val dest = File(target, path)
                if (!dest.canonicalPath.startsWith(target.canonicalPath + File.separator)) {
                    throw IllegalArgumentException("包内包含越界路径：$path")
                }
                dest.parentFile?.mkdirs()
                dest.writeBytes(reader.read(path))
            }
        }
        if (!packageFile.delete()) {
            // 包删不掉（被占用等）：目录保留，提示用户手动删。
            throw IllegalStateException("已解包到 ${mod.id}/，但原包删除失败（可手动删除）")
        }
        forget(modsDir, mod.id)
        return mod.id
    }

    fun forget(modsDir: File, id: String) {
        val values = readSelection(modsDir).toMutableMap()
        if (values.remove(id) == null) return
        writeSelection(modsDir, values)
    }

    fun selectionFile(modsDir: File): File = File(File(modsDir, SELECTION_DIR), SELECTION_FILE)

    fun readSelection(modsDir: File): Map<String, Boolean> {
        val file = selectionFile(modsDir)
        if (!file.isFile) return emptyMap()
        return runCatching {
            val root = JSONObject(file.readText())
            val enabled = root.optJSONObject("enabled") ?: return emptyMap()
            buildMap {
                enabled.keys().forEach { key ->
                    put(key, enabled.optBoolean(key, true))
                }
            }
        }.getOrElse { t ->
            Log.w("读取 Mod 选择失败（按默认启用处理）：${t.javaClass.simpleName}")
            emptyMap()
        }
    }

    private fun writeSelection(modsDir: File, values: Map<String, Boolean>) {
        val dir = File(modsDir, SELECTION_DIR)
        if (!dir.isDirectory && !dir.mkdirs()) {
            throw IllegalStateException("无法创建 $dir")
        }
        val enabled = JSONObject()
        values.toSortedMap(String.CASE_INSENSITIVE_ORDER).forEach { (id, on) ->
            enabled.put(id, on)
        }
        val json = JSONObject()
            .put("version", FORMAT_VERSION)
            .put("enabled", enabled)
            .toString(2)

        val target = File(dir, SELECTION_FILE)
        val temp = File(dir, "$SELECTION_FILE.tmp")
        temp.writeText(json)
        if (!temp.renameTo(target)) {
            target.writeText(json)
            temp.delete()
        }
    }

    private fun readMod(
        source: String,
        fallbackId: String,
        dll: File,
        kind: ModKind,
        selection: Map<String, Boolean>,
    ): ModInfo {
        var id = fallbackId
        var name = fallbackId
        var version = "0.0.0"
        var manifestEnabled = true
        var iconPath: String? = null
        var error: String? = null

        val dir = dll.parentFile
        val manifest = File(dir, MANIFEST_NAME)
        if (manifest.isFile) {
            try {
                val root = JSONObject(manifest.readText())
                root.optString("id").takeIf { it.isNotBlank() }?.let { id = it }
                root.optString("name").takeIf { it.isNotBlank() }?.let { name = it }
                root.optString("version").takeIf { it.isNotBlank() }?.let { version = it }
                root.optString("icon").takeIf { it.isNotBlank() }?.let { iconPath = it }
                if (root.has("enabled")) manifestEnabled = root.optBoolean("enabled", true)
            } catch (t: Throwable) {
                error = "manifest.json 解析失败"
                Log.w("$fallbackId 的 manifest.json 解析失败：${t.javaClass.simpleName}: ${t.message}")
            }
        }

        return ModInfo(
            id = id,
            name = name,
            version = version,
            source = source,
            kind = kind,
            dll = dll,
            packageFile = null,
            icon = loadDirIcon(dir, iconPath),
            enabled = selection[id] ?: manifestEnabled,
            manifestEnabled = manifestEnabled,
            error = error,
        )
    }

    private fun readPackage(packageFile: File, selection: Map<String, Boolean>): ModInfo {
        val fallbackId = packageFile.nameWithoutExtension
        var id = fallbackId
        var name = fallbackId
        var version = "0.0.0"
        var manifestEnabled = true
        var iconPath: String? = null
        var icon: Bitmap? = null
        var error: String? = null

        try {
            if (!PmodArchive.isArchive(packageFile)) {
                error = "pmod 格式不正确（v2 起为自研容器，zip 不再支持）：请重新打包"
            } else {
                PmodArchive.Reader(packageFile).use { reader ->
                    val text = if (reader.contains(MANIFEST_NAME)) String(reader.read(MANIFEST_NAME), Charsets.UTF_8) else null
                    if (text != null) {
                        val root = JSONObject(text)
                        root.optString("id").takeIf { it.isNotBlank() }?.let { id = it }
                        root.optString("name").takeIf { it.isNotBlank() }?.let { name = it }
                        root.optString("version").takeIf { it.isNotBlank() }?.let { version = it }
                        root.optString("icon").takeIf { it.isNotBlank() }?.let { iconPath = it }
                        if (root.has("enabled")) manifestEnabled = root.optBoolean("enabled", true)
                    } else {
                        error = "包内缺少 manifest.json"
                    }

                    icon = loadPackageIcon(reader, iconPath, fallbackId)
                    if (findPackageDll(reader, fallbackId) == null) {
                        error = "包内没有主程序集（同名 dll / 唯一 dll）"
                    }
                }
            }
        } catch (t: Throwable) {
            error = "包解析失败：${t.javaClass.simpleName}"
            Log.w("${packageFile.name} 解析失败：${t.javaClass.simpleName}: ${t.message}")
        }

        return ModInfo(
            id = id,
            name = name,
            version = version,
            source = packageFile.name,
            kind = ModKind.PACKAGE,
            dll = null,
            packageFile = packageFile,
            icon = icon,
            enabled = selection[id] ?: manifestEnabled,
            manifestEnabled = manifestEnabled,
            error = error,
        )
    }

    /** 与 C# 侧 `FindModDll` 同一套优先级；导入/打包校验也用它。 */
    internal fun findMainDll(dir: File): File? {
        val named = File(dir, "${dir.name}.dll")
        if (named.isFile) return named

        val dlls = dir.listFiles { file ->
            file.isFile && file.extension.equals("dll", ignoreCase = true)
        }?.sortedBy { it.name.lowercase(Locale.US) } ?: emptyList()

        return when (dlls.size) {
            1 -> dlls[0]
            0 -> null
            else -> {
                Log.w("${dir.name} 里有 ${dlls.size} 个 dll，无法判断主程序集")
                null
            }
        }
    }

    internal fun findPackageDll(reader: PmodArchive.Reader, fallbackId: String): String? {
        val rootDlls = reader.entries.keys
            .filter { it.endsWith(".dll", ignoreCase = true) && !it.contains('/') }
        return when {
            "$fallbackId.dll" in rootDlls -> "$fallbackId.dll"
            rootDlls.size == 1 -> rootDlls[0]
            else -> null
        }
    }

    private fun loadDirIcon(dir: File, manifestIcon: String?): Bitmap? {
        val candidates = buildList {
            manifestIcon?.let { add(File(dir, it)) }
            add(File(dir, "icon.png"))
            add(File(dir, "assets/icon.png"))
        }
        return candidates.firstOrNull { it.isFile }?.let { file ->
            runCatching { BitmapFactory.decodeFile(file.absolutePath) }.getOrNull()
        }
    }

    private fun loadPackageIcon(reader: PmodArchive.Reader, manifestIcon: String?, fallbackId: String): Bitmap? {
        val candidates = buildList {
            manifestIcon?.let { add(it) }
            add("icon.png")
            add("assets/icon.png")
        }
        for (path in candidates) {
            if (!reader.contains(path)) continue
            runCatching {
                BitmapFactory.decodeByteArray(reader.read(path), 0, reader.entries.getValue(path).rawSize)
            }.getOrNull()?.let { return it }
        }
        return null
    }
}
