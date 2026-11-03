package dev.pylon.loader

import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.File
import java.io.InputStream
import java.io.RandomAccessFile
import java.util.zip.Deflater
import java.util.zip.Inflater

/**
 * .pmod v2 自研容器（PYPM）：与托管侧 PylonArchive 同一布局。
 *
 *   magic "PYPM"(4) | version u32=2 | TOC | 数据区
 *   TOC: count u32 + 每条 [pathLen u16 + flags u8(bit0=deflate) + path(utf8) + rawSize u32 + dataOff u64(包内绝对) + storedLen u32]
 *
 * 条目达到阈值且压缩有收益才 deflate（nowrap 原始 deflate 流，.NET DeflateStream 直接可解）；
 * png 等已压缩内容保持 stored。
 */
internal object PmodArchive {
    const val MAGIC: Int = 0x4D505950 // "PYPM" LE
    const val VERSION: Int = 2
    const val EXTENSION = "pmod"

    private const val FLAG_DEFLATE = 0x01
    private const val DEFLATE_THRESHOLD = 32 * 1024

    data class Entry(val path: String, val flags: Byte, val rawSize: Int, val dataOffset: Long, val storedSize: Int)

    fun isArchive(file: File): Boolean =
        runCatching { RandomAccessFile(file, "r").use { it.readInt() == MAGIC } }.getOrDefault(false)

    /** 只读解析器。按 TOC 偏移随机直读，不解整包；用完 close。 */
    class Reader(file: File) : Closeable {
        private val raf = RandomAccessFile(file, "r")
        val entries: Map<String, Entry>

        init {
            try {
                if (raf.readInt() != MAGIC) throw IllegalArgumentException("pmod 格式不正确（v2 起为自研容器，zip 不再支持）：请重新打包")
                val version = raf.readInt()
                if (version != VERSION) throw IllegalArgumentException("pmod 版本不支持：" + version + "（当前 " + VERSION + "）")
                val count = raf.readInt()
                if (count < 0 || count > 4096) throw IllegalArgumentException("TOC 条目异常：" + count)
                val map = LinkedHashMap<String, Entry>(count)
                repeat(count) {
                    val pathLen = raf.readShort().toInt() and 0xFFFF
                    val flags = raf.readByte()
                    val bytes = ByteArray(pathLen)
                    raf.readFully(bytes)
                    val rawSize = raf.readInt()
                    val dataOffset = raf.readLong()
                    val storedSize = raf.readInt()
                    val path = String(bytes, Charsets.UTF_8)
                    if (path.isEmpty() || path.startsWith('/') || path.contains('\\') || path.contains("//") ||
                        path.split('/').any { it == "." || it == ".." }) {
                        throw IllegalArgumentException("TOC 路径非法：" + path)
                    }
                    map[path] = Entry(path, flags, rawSize, dataOffset, storedSize)
                }
                entries = map
            } catch (t: Throwable) {
                runCatching { raf.close() }
                throw t
            }
        }

        fun contains(path: String): Boolean = entries.containsKey(path)

        /** 读出完整内容（按需解压）。 */
        fun read(path: String): ByteArray {
            val entry = entries[path] ?: throw IllegalArgumentException("包内没有该条目：" + path)
            val stored = ByteArray(entry.storedSize)
            synchronized(raf) {
                raf.seek(entry.dataOffset)
                raf.readFully(stored)
            }
            if (entry.flags.toInt() and FLAG_DEFLATE == 0) return stored
            val inflater = Inflater(true) // nowrap：原始 deflate 流
            inflater.setInput(stored)
            val output = ByteArray(entry.rawSize)
            var produced = 0
            while (produced < entry.rawSize && !inflater.finished()) {
                val n = inflater.inflate(output, produced, entry.rawSize - produced)
                if (n == 0 && inflater.needsInput()) throw IllegalArgumentException("条目解压不完整：" + path)
                produced += n
            }
            inflater.end()
            if (produced != entry.rawSize) throw IllegalArgumentException("条目解压不完整：" + path)
            return output
        }

        override fun close() = raf.close()
    }

    /** 一次性把全部条目写成 v2 包；items 顺序即 TOC 顺序。返回文件字节数。 */
    fun write(output: File, items: List<Pair<String, () -> InputStream>>): Long {
        data class Planned(val path: String, val pathBytes: ByteArray, val flags: Byte, val raw: Int, val stored: ByteArray)

        val planned = items.map { (path, open) ->
            val content = open().use { it.readBytes() }
            var flags: Byte = 0
            var bytes = content
            if (content.size >= DEFLATE_THRESHOLD) {
                val deflated = deflate(content)
                if (deflated.size < content.size * 15 / 16) {
                    flags = FLAG_DEFLATE.toByte()
                    bytes = deflated
                }
            }
            val pathBytes = path.toByteArray(Charsets.UTF_8)
            if (pathBytes.size > 0xFFFF) throw IllegalArgumentException("路径过长：" + path)
            Planned(path, pathBytes, flags, content.size, bytes)
        }

        // 头部 8 字节（magic + version）+ TOC，数据区紧随其后；TOC 大小先算出来，偏移才能一次写对。
        val tocSize = 4 + planned.sumOf { 2 + 1 + it.pathBytes.size + 4 + 8 + 4 }
        val dataStart = 8L + tocSize

        val toc = ByteArrayOutputStream(tocSize)
        writeInt(toc, planned.size)
        var runningOffset = 0L
        for (e in planned) {
            writeShort(toc, e.pathBytes.size)
            toc.write(e.flags.toInt())
            toc.write(e.pathBytes)
            writeInt(toc, e.raw)
            writeLong(toc, dataStart + runningOffset)
            writeInt(toc, e.stored.size)
            runningOffset += e.stored.size
        }

        val out = output.outputStream().buffered()
        out.write(intToBytes(MAGIC))
        out.write(intToBytes(VERSION))
        out.write(toc.toByteArray())
        for (e in planned) out.write(e.stored)
        out.flush()
        out.close()
        return dataStart + runningOffset
    }

    private fun deflate(content: ByteArray): ByteArray {
        val deflater = Deflater(Deflater.BEST_SPEED, true) // nowrap=true → 原始 deflate 流，与 .NET DeflateStream 一致
        deflater.setInput(content)
        deflater.finish()
        val buffer = ByteArray(64 * 1024)
        val out = ByteArrayOutputStream(content.size / 2 + 64)
        while (!deflater.finished()) {
            val n = deflater.deflate(buffer)
            out.write(buffer, 0, n)
        }
        deflater.end()
        return out.toByteArray()
    }

    private fun writeInt(target: ByteArrayOutputStream, v: Int) {
        target.write(v); target.write(v ushr 8); target.write(v ushr 16); target.write(v ushr 24)
    }

    private fun writeShort(target: ByteArrayOutputStream, v: Int) {
        target.write(v and 0xFF); target.write((v ushr 8) and 0xFF)
    }

    private fun intToBytes(v: Int) = byteArrayOf(
        (v and 0xFF).toByte(),
        ((v ushr 8) and 0xFF).toByte(),
        ((v ushr 16) and 0xFF).toByte(),
        ((v ushr 24) and 0xFF).toByte(),
    )

    private fun writeLong(target: ByteArrayOutputStream, v: Long) {
        repeat(8) { i -> target.write(((v ushr (8 * i)) and 0xFF).toInt()) }
    }
}