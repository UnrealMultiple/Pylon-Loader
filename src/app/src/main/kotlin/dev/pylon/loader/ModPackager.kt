package dev.pylon.loader

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Shader
import android.graphics.Typeface
import android.net.Uri
import android.provider.DocumentsContract
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.Locale
import java.util.UUID
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream

internal data class ModPackResult(
    val success: Boolean,
    val file: File?,
    val message: String,
)

/**
 * 把一个 Mod 文件夹打包成单个 `.pmod`（zip 容器），格式就是"Mod 目录的 zip"：
 *   manifest.json  必需（源目录没有时生成 id/name/version/1.0.0）
 *   mod.dll        主程序集（或同名 dll / 唯一 dll）
 *   icon.png       可选，显示在 Mod 管理页
 *   assets/ lib/   可选，原样保留
 * 输出到 `Mods/<id>.pmod`；宿主启动时解到内部缓存再交给托管加载器，与文件夹 Mod 并存、同一套选择逻辑。
 */
internal object ModPackager {

    fun pack(context: Context, treeUri: Uri, modsDir: File): ModPackResult {
        val folderName = treeDisplayName(treeUri)
        val entries = mutableListOf<TreeEntry>()
        return try {
            val rootId = DocumentsContract.getTreeDocumentId(treeUri)
            collect(context, treeUri, rootId, "", entries)
            if (entries.isEmpty()) {
                return ModPackResult(false, null, "所选文件夹是空的")
            }

            val manifestEntry = entries.firstOrNull { it.path == ModCatalog.MANIFEST_NAME }
            val manifest = if (manifestEntry != null) {
                val text = context.contentResolver.openInputStream(manifestEntry.uri)
                    ?.bufferedReader()?.use { it.readText() }
                    ?: throw IllegalStateException("无法读取 manifest.json")
                try {
                    JSONObject(text)
                } catch (t: Throwable) {
                    return ModPackResult(false, null, "manifest.json 不是合法 JSON：${t.message}")
                }
            } else {
                JSONObject()
            }

            val rawId = manifest.optString("id").takeIf { it.isNotBlank() } ?: folderName
            val id = sanitizeId(rawId)
            val name = manifest.optString("name").takeIf { it.isNotBlank() } ?: folderName
            val rootDlls = entries.filter {
                !it.isDirectory && !it.path.contains('/') && it.path.endsWith(".dll", ignoreCase = true)
            }.map { it.path }
            val mainDll = when {
                "mod.dll" in rootDlls -> "mod.dll"
                "$id.dll" in rootDlls -> "$id.dll"
                rootDlls.size == 1 -> rootDlls[0]
                else -> return ModPackResult(
                    false,
                    null,
                    "根目录没有主程序集：需要 mod.dll、$id.dll，或唯一的 dll",
                )
            }
            if (mainDll.isEmpty()) return ModPackResult(false, null, "主程序集为空")

            manifest.put("id", id)
            manifest.put("name", name)
            if (manifest.optString("version").isBlank()) manifest.put("version", "1.0.0")
            if (!manifest.has("enabled")) manifest.put("enabled", true)

            val manifestIcon = manifest.optString("icon").takeIf { it.isNotBlank() }
            val hasIcon = entries.any { entry ->
                !entry.isDirectory && (
                    entry.path == "icon.png" ||
                        entry.path == "assets/icon.png" ||
                        (manifestIcon != null && entry.path == manifestIcon)
                    )
            }

            val target = File(modsDir, "$id.${ModCatalog.PACKAGE_EXTENSION}")
            if (target.exists()) {
                return ModPackResult(false, null, "已存在 ${target.name}，先删除旧包再打包")
            }

            val tmpDir = File(File(modsDir, ".pylon"), "tmp").apply { mkdirs() }
            val tmp = File(tmpDir, "pack-" + UUID.randomUUID() + ".pmod")
            try {
                ZipOutputStream(tmp.outputStream().buffered()).use { zip ->
                    zip.putNextEntry(ZipEntry(ModCatalog.MANIFEST_NAME))
                    zip.write(manifest.toString(2).toByteArray(Charsets.UTF_8))
                    zip.closeEntry()

                    for (entry in entries) {
                        if (entry.isDirectory) continue
                        if (entry.path == ModCatalog.MANIFEST_NAME) continue
                        val input = context.contentResolver.openInputStream(entry.uri)
                            ?: throw IllegalStateException("无法读取 ${entry.path}")
                        input.use {
                            zip.putNextEntry(ZipEntry(entry.path))
                            it.copyTo(zip)
                            zip.closeEntry()
                        }
                    }

                    // 没有 icon.png / assets/icon.png 时生成占位图标，保证每个 .pmod 都有图标。
                    if (!hasIcon) {
                        zip.putNextEntry(ZipEntry("icon.png"))
                        zip.write(generateIcon(name))
                        zip.closeEntry()
                    }
                }

                ZipFile(tmp).use { zip ->
                    if (zip.getEntry(ModCatalog.MANIFEST_NAME) == null ||
                        zip.entries().asSequence().none {
                            !it.isDirectory && it.name.endsWith(".dll", ignoreCase = true)
                        }
                    ) {
                        throw IllegalStateException("生成的包校验失败")
                    }
                }

                if (!tmp.renameTo(target)) {
                    tmp.copyTo(target, overwrite = true)
                    tmp.delete()
                }
                ModPackResult(
                    true,
                    target,
                    "已打包 ${target.name}（${entries.size} 个文件）",
                )
            } finally {
                tmp.delete()
            }
        } catch (t: Throwable) {
            ModPackResult(false, null, "打包失败：${t.javaClass.simpleName}: ${t.message}")
        }
    }

    private fun generateIcon(name: String): ByteArray {
        val size = 128
        val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val background = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            shader = LinearGradient(
                0f, 0f, size.toFloat(), size.toFloat(),
                Color.rgb(34, 211, 238), Color.rgb(99, 102, 241),
                Shader.TileMode.CLAMP,
            )
        }
        canvas.drawRect(0f, 0f, size.toFloat(), size.toFloat(), background)

        val initial = name.trim().take(1).uppercase(Locale.getDefault()).ifBlank { "M" }
        val text = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.WHITE
            textSize = 66f
            typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
            textAlign = Paint.Align.CENTER
        }
        val metrics = text.fontMetrics
        val baseline = size / 2f - (metrics.ascent + metrics.descent) / 2f
        canvas.drawText(initial, size / 2f, baseline, text)

        return ByteArrayOutputStream().use { out ->
            bitmap.compress(Bitmap.CompressFormat.PNG, 100, out)
            bitmap.recycle()
            out.toByteArray()
        }
    }

    private data class TreeEntry(
        val path: String,
        val uri: Uri,
        val isDirectory: Boolean,
    )

    private fun collect(
        context: Context,
        treeUri: Uri,
        documentId: String,
        prefix: String,
        out: MutableList<TreeEntry>,
    ) {
        val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, documentId)
        val projection = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE,
        )
        context.contentResolver.query(childrenUri, projection, null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                val id = cursor.getString(0) ?: continue
                val rawName = cursor.getString(1) ?: continue
                val name = sanitizeEntryName(rawName)
                if (name == "__MACOSX" || name == ".git" || name == ".vs" || name == ".idea") continue
                val mime = cursor.getString(2)
                val path = if (prefix.isEmpty()) name else "$prefix/$name"
                val childUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, id)
                if (mime == DocumentsContract.Document.MIME_TYPE_DIR) {
                    out += TreeEntry(path, childUri, true)
                    collect(context, treeUri, id, path, out)
                } else {
                    out += TreeEntry(path, childUri, false)
                }
            }
        } ?: throw IllegalStateException("无法列出所选文件夹")
    }

    private fun treeDisplayName(treeUri: Uri): String {
        val id = DocumentsContract.getTreeDocumentId(treeUri)
        return id.substringAfterLast(':').substringAfterLast('/').ifBlank { "Mod" }
    }

    private fun sanitizeId(raw: String): String {
        val cleaned = raw.trim().replace(Regex("[^A-Za-z0-9._-]"), "_").trim('_', '.')
        return cleaned.take(64).ifBlank { "Mod" }
    }

    private fun sanitizeEntryName(raw: String): String {
        val name = raw.replace('\\', '/').substringAfterLast('/').trim()
        require(name.isNotEmpty() && name != "." && name != "..") { "非法的文件名：$raw" }
        return name
    }
}
