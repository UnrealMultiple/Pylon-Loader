package dev.pylon.loader.game

import java.io.File
import java.lang.reflect.Array as ReflectArray
import java.lang.reflect.Field

/**
 * 层：宿主层 / native 搜索路径
 *
 * Unity 内部会 `System.loadLibrary("il2cpp")`，而 loadLibrary 只查 classloader 自带的
 * native 目录（APK 的 nativeLibraryDir），导入目录不在其中，即使已 `System.load(绝对路径)`
 * 预加载，Unity 按名字再取仍会失败。
 *
 * 这里把导入目录追加进 `DexPathList` 的两套字段：
 *   · nativeLibraryDirectories / systemNativeLibraryDirectories（File[] 或 List<File>）
 *   · nativeLibraryPathElements（NativeLibraryElement[]，loadLibrary 实际查的）
 * 全是反射，失败只记日志；debug 变体仍有 APK 内副本兜底。
 */
internal object NativeLibraryPath {

    fun add(classLoader: ClassLoader, dir: File): Boolean {
        val pathList = field(classLoader.javaClass, "pathList")?.get(classLoader) ?: return false
        var changed = false

        for (name in arrayOf("nativeLibraryDirectories", "systemNativeLibraryDirectories")) {
            val f = field(pathList.javaClass, name) ?: continue
            runCatching {
                when (val value = f.get(pathList)) {
                    is Array<*> -> {
                        if (value.any { (it as? File)?.absolutePath == dir.absolutePath }) return@runCatching
                        val component = value.javaClass.componentType
                        val grown = ReflectArray.newInstance(component, value.size + 1)
                        for (i in value.indices) ReflectArray.set(grown, i, value[i])
                        ReflectArray.set(grown, value.size, dir)
                        f.set(pathList, grown)
                        changed = true
                    }
                    is MutableList<*> -> {
                        @Suppress("UNCHECKED_CAST")
                        val list = value as MutableList<Any?>
                        if (list.none { (it as? File)?.absolutePath == dir.absolutePath }) {
                            list.add(dir)
                            changed = true
                        }
                    }
                }
            }.onFailure { Log.w("追加 $name 失败：${it.javaClass.simpleName}: ${it.message}") }
        }

        val elementsField = field(pathList.javaClass, "nativeLibraryPathElements")
        if (elementsField != null) {
            runCatching {
                val elements = elementsField.get(pathList) as? Array<*> ?: return@runCatching
                val component = elements.javaClass.componentType

                @Suppress("UNCHECKED_CAST")
                val ctor = component.constructors.firstOrNull { candidate ->
                    candidate.parameterTypes.size == 1 && candidate.parameterTypes[0] == File::class.java
                } as? java.lang.reflect.Constructor<Any>
                if (ctor == null) {
                    Log.w("nativeLibraryPathElements 元素没有 (File) 构造函数，跳过")
                    return@runCatching
                }
                ctor.isAccessible = true
                val element = ctor.newInstance(dir)
                val grown = ReflectArray.newInstance(component, elements.size + 1)
                for (i in elements.indices) ReflectArray.set(grown, i, elements[i])
                ReflectArray.set(grown, elements.size, element)
                elementsField.set(pathList, grown)
                changed = true
            }.onFailure {
                Log.w("追加 nativeLibraryPathElements 失败：${it.javaClass.simpleName}: ${it.message}")
            }
        }

        if (changed) Log.stage("host", "已把 ${dir.absolutePath} 追加到 classloader 的 native 搜索路径")
        return changed
    }

    private fun field(clazz: Class<*>, name: String): Field? {
        var current: Class<*>? = clazz
        while (current != null && current != Any::class.java) {
            try {
                return current.getDeclaredField(name).apply { isAccessible = true }
            } catch (_: NoSuchFieldException) {
                current = current.superclass
            }
        }
        return null
    }
}
