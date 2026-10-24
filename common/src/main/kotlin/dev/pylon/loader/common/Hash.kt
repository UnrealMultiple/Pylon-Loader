package dev.pylon.loader.common

import java.io.File
import java.security.MessageDigest

/**
 * 文件哈希工具：证明导入的运行时确实来自用户安装包原件——
 * 尤其 libil2cpp.so 与 global-metadata.dat 必须同构建（docs/01 硬约束）。
 */
object Hash {

    private const val BUFFER_SIZE = 64 * 1024

    /** 流式计算，避免把 57MB 的 libil2cpp.so 整个读进内存。 */
    fun sha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(BUFFER_SIZE)
            while (true) {
                val read = input.read(buffer)
                if (read <= 0) break
                digest.update(buffer, 0, read)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }
}
