package dev.pylon.loader.game.core

import android.content.Context
import dev.pylon.loader.game.Log
import org.json.JSONObject
import java.io.File
import java.util.Locale
import java.util.UUID
import java.util.zip.ZipFile

/**
 * 层：宿主层 / `.pmod` 解包缓存
 *
 * `.pmod` 就是"Mod 目录的 zip"。托管加载器只扫目录，所以启动 CoreCLR 前先把包解到
 * `filesDir/mod-cache/pkg-<name>-<len>-<mtime>/`，把缓存根作为**第二个 Mod 根**交给托管层；
 * 缓存按包大小与 mtime 失效，包被删后对应缓存也清掉，避免加载幽灵 Mod。
 * 这一步在后台线程（CoreCLR 启动前）执行，不占游戏线程。
 */
internal object ModPackageCache {

    private const val PACKAGE_EXTENSION = "pmod"
    private const val MANIFEST = "manifest.json"
    private const val MARKER = ".ok"

    /** @return 缓存根（没有 .pmod 时返回 null）；托管层会把它和用户 Mods 目录一起扫描。 */
    fun prepare(context: Context, modsDir: File): File? {
        val cacheRoot = File(context.filesDir, "mod-cache").apply { mkdirs() }
        val packages = modsDir.listFiles { file ->
            file.isFile && file.extension.equals(PACKAGE_EXTENSION, ignoreCase = true)
        }?.sortedBy { it.name.lowercase(Locale.US) } ?: emptyList()

        val keep = mutableSetOf<String>()
        for (pkg in packages) {
            val key = "pkg-${sanitize(pkg.nameWithoutExtension)}-${pkg.length()}-${pkg.lastModified()}"
            val target = File(cacheRoot, key)
            if (File(target, MARKER).isFile) {
                keep += key
                continue
            }

            target.deleteRecursively()
            val temp = File(cacheRoot, "tmp-" + UUID.randomUUID())
            try {
                extract(pkg, temp)
                val root = flattenRoot(temp)
                ensureManifest(root, pkg)
                if (findMainDll(root, pkg.nameWithoutExtension) == null) {
                    throw IllegalStateException("包内没有主程序集（mod.dll / 同名 dll / 唯一 dll）")
                }
                if (!root.renameTo(target)) {
                    throw IllegalStateException("无法把解包缓存换入 $target")
                }
                File(target, MARKER).writeText("ok")
                keep += key
                temp.deleteRecursively()
                Log.stage("mods", "已解包 ${pkg.name} → $target")
            } catch (t: Throwable) {
                Log.e("解包 ${pkg.name} 失败（该包本次跳过）：${t.javaClass.simpleName}: ${t.message}")
                temp.deleteRecursively()
                target.deleteRecursively()
            }
        }

        cacheRoot.listFiles { file ->
            file.isDirectory &&
                (file.name.startsWith("pkg-") && file.name !in keep || file.name.startsWith("tmp-"))
        }?.forEach { stale ->
            if (!stale.deleteRecursively()) Log.w("清理解包缓存失败：$stale")
        }

        return if (keep.isEmpty()) null else cacheRoot
    }

    private fun extract(packageFile: File, dest: File) {
        dest.mkdirs()
        val canonicalRoot = dest.canonicalFile
        ZipFile(packageFile).use { zip ->
            for (entry in zip.entries()) {
                if (entry.isDirectory) continue
                val name = entry.name.replace('\\', '/')
                if (name.startsWith("__MACOSX/")) continue
                val parts = name.split('/').filter { it.isNotEmpty() && it != "." && it != ".." }
                if (parts.isEmpty()) continue
                val target = File(canonicalRoot, parts.joinToString(File.separator))
                if (!target.canonicalPath.startsWith(canonicalRoot.path + File.separator)) {
                    throw IllegalArgumentException("包内包含越界路径：${entry.name}")
                }
                target.parentFile?.mkdirs()
                zip.getInputStream(entry).use { input ->
                    target.outputStream().use { output -> input.copyTo(output) }
                }
            }
        }
    }

    /** 允许包里再套一层目录；只认"根下唯一目录且根下没有文件"的情况。 */
    private fun flattenRoot(temp: File): File {
        val children = temp.listFiles() ?: return temp
        if (children.size == 1 && children[0].isDirectory) return children[0]
        return temp
    }

    private fun ensureManifest(root: File, packageFile: File) {
        val id = packageFile.nameWithoutExtension.ifBlank { "Mod" }
        val manifest = File(root, MANIFEST)
        if (!manifest.isFile) {
            val json = JSONObject()
                .put("id", id)
                .put("name", id)
                .put("version", "1.0.0")
                .put("enabled", true)
            manifest.writeText(json.toString(2))
            return
        }

        // 缺 id 时补一份规范化清单：否则托管层会拿缓存目录名当 id，选择文件对不上。
        try {
            val json = JSONObject(manifest.readText())
            if (json.optString("id").isBlank()) {
                json.put("id", id)
                if (json.optString("name").isBlank()) json.put("name", id)
                if (json.optString("version").isBlank()) json.put("version", "1.0.0")
                manifest.writeText(json.toString(2))
            }
        } catch (t: Throwable) {
            throw IllegalStateException("manifest.json 解析失败：${t.message}")
        }
    }

    private fun findMainDll(dir: File, fallbackId: String): File? {
        val preferred = File(dir, "mod.dll")
        if (preferred.isFile) return preferred
        val named = File(dir, "$fallbackId.dll")
        if (named.isFile) return named
        // manifest 里的 id 可能与包文件名不同。
        val manifest = File(dir, MANIFEST)
        val manifestId = runCatching { JSONObject(manifest.readText()).optString("id") }.getOrDefault("")
        if (manifestId.isNotBlank()) {
            val byManifest = File(dir, "$manifestId.dll")
            if (byManifest.isFile) return byManifest
        }
        val dlls = dir.listFiles { file ->
            file.isFile && file.extension.equals("dll", ignoreCase = true)
        }?.sortedBy { it.name.lowercase(Locale.US) } ?: emptyList()
        return if (dlls.size == 1) dlls[0] else null
    }

    private fun sanitize(raw: String): String {
        val cleaned = raw.trim().replace(Regex("[^A-Za-z0-9._-]"), "_").trim('_', '.')
        return cleaned.take(48).ifBlank { "Mod" }
    }
}
