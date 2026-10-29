package dev.pylon.loader

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import android.provider.OpenableColumns
import java.io.File
import java.io.InputStream
import java.util.UUID
import java.util.zip.ZipFile
import java.util.zip.ZipInputStream

internal data class ModImportResult(
    val success: Boolean,
    val name: String?,
    val message: String,
)

/**
 * SAF 导入：单个 `.dll`、`.zip` 或 `ACTION_OPEN_DOCUMENT_TREE` 选的整个文件夹。
 * 先解到 `Mods/.pylon/tmp/`，校验出主程序集后再换入正式目录；失败不留半成品。
 * 不申请存储权限：SAF 读权限本次导入内有效，导入后数据已是 app 自己的文件。
 */
internal object ModImporter {

    fun importDocument(context: Context, modsDir: File, uri: Uri): ModImportResult {
        val displayName = queryDisplayName(context, uri) ?: uri.lastPathSegment ?: "Mod"
        val safeName = sanitizeName(displayName)
        return try {
            modsDir.mkdirs()
            when {
                displayName.endsWith(".pmod", ignoreCase = true) ->
                    importPackage(context, modsDir, uri, safeName)
                displayName.endsWith(".zip", ignoreCase = true) -> importZip(context, modsDir, uri, safeName)
                displayName.endsWith(".dll", ignoreCase = true) -> importDll(context, modsDir, uri, safeName)
                else -> ModImportResult(false, null, "只支持 .dll、.zip 或 .pmod 文件")
            }
        } catch (t: Throwable) {
            ModImportResult(false, null, "导入失败：${t.javaClass.simpleName}: ${t.message}")
        }
    }

    fun importTree(context: Context, modsDir: File, treeUri: Uri): ModImportResult {
        val name = treeDisplayName(treeUri)
        val safeName = sanitizeName(name)
        return try {
            modsDir.mkdirs()
            val target = File(modsDir, safeName)
            if (target.exists()) {
                return ModImportResult(false, safeName, "已存在同名目录：$safeName（先删除或改名）")
            }
            target.mkdirs()
            try {
                val rootId = DocumentsContract.getTreeDocumentId(treeUri)
                copyTree(context, treeUri, rootId, target)
                validateAndFinish(modsDir, target, safeName)
            } catch (t: Throwable) {
                target.deleteRecursively()
                throw t
            }
        } catch (t: Throwable) {
            ModImportResult(false, null, "导入文件夹失败：${t.javaClass.simpleName}: ${t.message}")
        }
    }

    private fun importPackage(
        context: Context,
        modsDir: File,
        uri: Uri,
        displayName: String,
    ): ModImportResult {
        val base = sanitizeName(displayName.substringBeforeLast('.', displayName))
        val target = File(modsDir, "$base.pmod")
        if (target.exists()) {
            return ModImportResult(false, base, "已存在同名包：${target.name}（先删除或改名）")
        }

        val tmpDir = File(File(modsDir, ".pylon"), "tmp").apply { mkdirs() }
        val tmp = File(tmpDir, "pkg-" + UUID.randomUUID() + ".pmod")
        try {
            context.contentResolver.openInputStream(uri)?.use { input ->
                tmp.outputStream().use { output -> input.copyTo(output) }
            } ?: throw IllegalStateException("无法打开所选文件")

            ZipFile(tmp).use { zip ->
                if (zip.getEntry(ModCatalog.MANIFEST_NAME) == null) {
                    throw IllegalArgumentException("包内缺少 manifest.json")
                }
                if (ModCatalog.findPackageDll(zip, base) == null) {
                    throw IllegalArgumentException("包内没有主程序集（mod.dll / 同名 dll / 唯一 dll）")
                }
            }

            if (!tmp.renameTo(target)) {
                tmp.copyTo(target, overwrite = true)
                tmp.delete()
            }
            return ModImportResult(true, base, "已导入 $base.pmod")
        } catch (t: Throwable) {
            tmp.delete()
            throw t
        }
    }

    private fun importDll(context: Context, modsDir: File, uri: Uri, displayName: String): ModImportResult {
        val base = sanitizeName(displayName.substringBeforeLast('.', displayName))
        val target = File(modsDir, base)
        if (target.exists()) {
            return ModImportResult(false, base, "已存在同名目录：$base（先删除或改名）")
        }
        target.mkdirs()
        try {
            context.contentResolver.openInputStream(uri)?.use { input ->
                File(target, "mod.dll").outputStream().use { output -> input.copyTo(output) }
            } ?: throw IllegalStateException("无法打开所选文件")
            return validateAndFinish(modsDir, target, base)
        } catch (t: Throwable) {
            target.deleteRecursively()
            throw t
        }
    }

    private fun importZip(context: Context, modsDir: File, uri: Uri, displayName: String): ModImportResult {
        val base = sanitizeName(displayName.substringBeforeLast('.', displayName))
        val target = File(modsDir, base)
        if (target.exists()) {
            return ModImportResult(false, base, "已存在同名目录：$base（先删除或改名）")
        }

        val tmpRoot = File(File(modsDir, ".pylon"), "tmp").apply { mkdirs() }
        val extracted = File(tmpRoot, "zip-" + UUID.randomUUID())
        try {
            context.contentResolver.openInputStream(uri)?.use { input ->
                extractZip(input, extracted)
            } ?: throw IllegalStateException("无法打开所选压缩包")

            val children = extracted.listFiles()?.filterNot { it.name.startsWith("__MACOSX") } ?: emptyList()
            val root = if (children.size == 1 && children[0].isDirectory) children[0] else extracted
            target.mkdirs()
            copyContents(root, target)
            return validateAndFinish(modsDir, target, base)
        } catch (t: Throwable) {
            target.deleteRecursively()
            throw t
        } finally {
            extracted.deleteRecursively()
        }
    }

    private fun validateAndFinish(modsDir: File, target: File, name: String): ModImportResult {
        if (ModCatalog.findMainDll(target) == null) {
            target.deleteRecursively()
            return ModImportResult(
                false,
                name,
                "没有找到主程序集：需要 mod.dll、与目录同名的 dll，或目录里唯一的 dll",
            )
        }
        return ModImportResult(true, name, "已导入 $name")
    }

    private fun extractZip(input: InputStream, outputDir: File) {
        val canonicalRoot = outputDir.canonicalFile
        outputDir.mkdirs()
        ZipInputStream(input).use { zip ->
            while (true) {
                val entry = zip.nextEntry ?: break
                val parts = entry.name.replace('\\', '/')
                    .split('/')
                    .filter { it.isNotEmpty() && it != "." && it != ".." }
                if (parts.isEmpty()) {
                    zip.closeEntry()
                    continue
                }
                val dest = File(canonicalRoot, parts.joinToString(File.separator))
                if (!dest.canonicalPath.startsWith(canonicalRoot.path + File.separator)) {
                    throw IllegalArgumentException("压缩包包含越界路径：${entry.name}")
                }
                if (entry.isDirectory) {
                    dest.mkdirs()
                } else {
                    dest.parentFile?.mkdirs()
                    dest.outputStream().use { output -> zip.copyTo(output) }
                }
                zip.closeEntry()
            }
        }
    }

    private fun copyTree(context: Context, treeUri: Uri, documentId: String, dest: File) {
        val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, documentId)
        val projection = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE,
        )
        context.contentResolver.query(childrenUri, projection, null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                val id = cursor.getString(0) ?: continue
                val name = sanitizeEntryName(cursor.getString(1) ?: continue)
                val mime = cursor.getString(2)
                val childDest = File(dest, name)
                if (mime == DocumentsContract.Document.MIME_TYPE_DIR) {
                    childDest.mkdirs()
                    copyTree(context, treeUri, id, childDest)
                } else {
                    val childUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, id)
                    childDest.parentFile?.mkdirs()
                    context.contentResolver.openInputStream(childUri)?.use { input ->
                        childDest.outputStream().use { output -> input.copyTo(output) }
                    } ?: throw IllegalStateException("无法读取 $name")
                }
            }
        } ?: throw IllegalStateException("无法列出所选文件夹")
    }

    private fun copyContents(source: File, target: File) {
        val children = source.listFiles() ?: return
        for (child in children) {
            val dest = File(target, child.name)
            if (child.isDirectory) {
                dest.mkdirs()
                copyContents(child, dest)
            } else {
                child.copyTo(dest, overwrite = true)
            }
        }
    }

    private fun queryDisplayName(context: Context, uri: Uri): String? {
        return context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)
            ?.use { cursor ->
                if (cursor.moveToFirst()) cursor.getString(0) else null
            }
    }

    private fun treeDisplayName(treeUri: Uri): String {
        val id = DocumentsContract.getTreeDocumentId(treeUri)
        val name = id.substringAfterLast(':').substringAfterLast('/')
        return name.ifBlank { "Mod" }
    }

    private fun sanitizeName(raw: String): String {
        val cleaned = raw.trim().replace(Regex("[\\\\/:*?\"<>|]"), "_")
        return cleaned.take(64).ifBlank { "Mod" }
    }

    private fun sanitizeEntryName(raw: String): String {
        val name = raw.replace('\\', '/').substringAfterLast('/').trim()
        require(name.isNotEmpty() && name != "." && name != "..") { "非法的文件名：$raw" }
        return name
    }
}
