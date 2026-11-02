package dev.pylon.loader.common

import java.io.File
import java.io.RandomAccessFile
import java.nio.ByteBuffer
import java.nio.ByteOrder

/// 读 ELF64 的 DT_NEEDED。判"带不带 PairIP"只能看链接关系：
/// APK 里存在 libpairipcore.so 不能作数（无校验构建也可能带着），libunity.so 依赖它才是。
object ElfDeps {

    const val PAIRIP_LIB = "libpairipcore.so"

    private const val PT_LOAD = 1
    private const val PT_DYNAMIC = 2
    private const val DT_NULL = 0L
    private const val DT_NEEDED = 1L
    private const val DT_STRTAB = 5L

    private class Load(val offset: Long, val vaddr: Long, val fileSize: Long)

    /// 不是可读 ELF64 就返回空表；调用方按"没有依赖"处理，不做推断。
    fun needed(file: File): List<String> = runCatching {
        if (!file.isFile) return emptyList()
        RandomAccessFile(file, "r").use { raf ->
            fun read(at: Long, size: Int): ByteBuffer {
                val bytes = ByteArray(size)
                raf.seek(at)
                raf.readFully(bytes)
                return ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
            }

            val header = read(0, 0x40)
            if (header.getInt(0) != 0x464C457F || header.get(4).toInt() != 2) return emptyList()
            val phoff = header.getLong(0x20)
            val phentsize = header.getShort(0x36).toInt() and 0xFFFF
            val phnum = header.getShort(0x38).toInt() and 0xFFFF
            if (phoff <= 0 || phentsize < 0x38 || phnum == 0) return emptyList()

            val ph = read(phoff, phentsize * phnum)
            val loads = ArrayList<Load>(8)
            var dynamicOffset = -1L
            var dynamicSize = 0L
            for (i in 0 until phnum) {
                val base = i * phentsize
                when (ph.getInt(base)) {
                    PT_LOAD -> loads += Load(ph.getLong(base + 8), ph.getLong(base + 16), ph.getLong(base + 0x20))
                    PT_DYNAMIC -> {
                        dynamicOffset = ph.getLong(base + 8)
                        dynamicSize = ph.getLong(base + 0x20)
                    }
                }
            }
            if (dynamicOffset < 0 || dynamicSize <= 0) return emptyList()

            // DT_STRTAB 可能排在 DT_NEEDED 之后，先扫一遍再取字符串。
            val dynamic = read(dynamicOffset, dynamicSize.toInt())
            var strtabVaddr = -1L
            val neededOffsets = ArrayList<Long>(8)
            var cursor = 0
            while (cursor + 16 <= dynamicSize) {
                val tag = dynamic.getLong(cursor)
                val value = dynamic.getLong(cursor + 8)
                if (tag == DT_NULL) break
                if (tag == DT_STRTAB) strtabVaddr = value
                if (tag == DT_NEEDED) neededOffsets += value
                cursor += 16
            }
            if (strtabVaddr < 0 || neededOffsets.isEmpty()) return emptyList()

            val segment = loads.firstOrNull { strtabVaddr >= it.vaddr && strtabVaddr < it.vaddr + it.fileSize }
                ?: return emptyList()
            val strtabOffset = segment.offset + (strtabVaddr - segment.vaddr)

            neededOffsets.mapNotNull { offset ->
                runCatching {
                    raf.seek(strtabOffset + offset)
                    buildString {
                        while (true) {
                            val byte = raf.read()
                            if (byte <= 0) break
                            append(byte.toChar())
                        }
                    }.ifEmpty { null }
                }.getOrNull()
            }
        }
    }.getOrDefault(emptyList())

    fun dependsOn(file: File, name: String): Boolean = needed(file).any { it == name }
}
