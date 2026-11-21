package dev.pylon.loader.game

import android.content.Context
import java.io.File
import java.io.IOException
import java.lang.reflect.Field
import java.lang.reflect.Method

/**
 * 把内置的游戏 dex 注入**系统创建的那个 classloader**。
 * 必须用它而非自建：只有系统 classloader 在 ART 里注册过 native loader namespace，自建
 * DexClassLoader 加载任何 .so 都会 `Check failed: class_loader_allocator != nullptr`（实测 tombstone）。
 * 必须注入：libmain.so 的 JNI_OnLoad 会对游戏类 com.unity3d.player.NativeLoader 调 RegisterNatives，
 * classpath 没有游戏 dex 就 ClassNotFoundException → SIGABRT（实测 tombstone）。
 * 注入方式按可用性依次尝试（都反射，非公开 API）：① addDexPath(String) ② 改 pathList.dexElements
 * 数组 ③ 改 pathList.dexPath；任一步成功即返回，全失败返回 null 由调用方给可读原因。
 */
internal object GameDexInjector {

    /**
     * @param dexPath 游戏 dex 的真实文件路径（RuntimeDeploy 从 assets/pylon-game/classes.dex 解出）。
     * 这里**只注入 dex**：.so 由 GameRuntimeLoader 从 APK 的 nativeLibraryDir 按序加载。
     */
    fun inject(context: Context, dexPath: String): ClassLoader? {
        val loader = context.classLoader
        Log.stage("host", "准备注入游戏 dex：loader=${loader.javaClass.name} dex=$dexPath")

        if (tryAddDexPath(loader, dexPath)) {
            Log.stage("host", "注入成功（ClassLoader.addDexPath）")
            return verify(loader)
        }
        if (tryInjectDexElements(context, loader, dexPath)) {
            Log.stage("host", "注入成功（pathList.dexElements）")
            return verify(loader)
        }
        if (tryPatchDexPath(loader, dexPath)) {
            Log.stage("host", "注入成功（pathList.dexPath）")
            return verify(loader)
        }

        Log.e("三种 dex 注入方式都失败，游戏类无法在宿主 classpath 上解析")
        return null
    }

    /** 用游戏包里必然存在的类验证注入是否真生效：不验证的话，注入"看似成功"但类找不到，问题会推迟到加载 .so 时以 SIGABRT 爆出来。 */
    private fun verify(loader: ClassLoader): ClassLoader? {
        val probe = "com.unity3d.player.NativeLoader"
        return try {
            Class.forName(probe, false, loader)
            Log.stage("host", "注入校验通过：$probe 已在宿主 classpath 上")
            loader
        } catch (t: Throwable) {
            Log.e("注入校验失败：仍然找不到 $probe（${t.javaClass.simpleName}）")
            null
        }
    }

    /** 方式 1：新 ART 上 ClassLoader 直接暴露 addDexPath。 */
    private fun tryAddDexPath(loader: ClassLoader, dexPath: String): Boolean = try {
        val method: Method = loader.javaClass.getMethod("addDexPath", String::class.java)
        method.invoke(loader, dexPath)
        true
    } catch (_: Throwable) {
        false
    }

    /** 方式 2：PathClassLoader.pathList.dexElements 是 Element[]，追加一个 dex 文件的元素。 */
    private fun tryInjectDexElements(context: Context, loader: ClassLoader, dexPath: String): Boolean {
        return try {
            val pathList = field(loader.javaClass, "pathList")?.get(loader) ?: return false
            val elementsField = field(pathList.javaClass, "dexElements") ?: return false
            elementsField.isAccessible = true
            val existing = elementsField.get(pathList) as? Array<*> ?: return false

            val elementClass = existing.javaClass.componentType ?: return false
            val makeDexElements = pathList.javaClass.declaredMethods.firstOrNull {
                it.name == "makeDexElements" || it.name == "makePathElements"
            } ?: return false
            makeDexElements.isAccessible = true

            val files: List<File> = listOf(File(dexPath))
            var listIndex = 0
            val args: Array<Any?> = makeDexElements.parameterTypes.map { type: Class<*> ->
                when {
                    type == ClassLoader::class.java -> loader as Any?
                    File::class.java.isAssignableFrom(type) -> context.codeCacheDir as Any?
                    List::class.java.isAssignableFrom(type) ->
                        (if (listIndex++ == 0) files else ArrayList<IOException>()) as Any?
                    else -> null
                }
            }.toTypedArray()
            Log.stage("host", "makeDexElements 形参：${makeDexElements.parameterTypes.joinToString { it.simpleName }}")

            val newElements = makeDexElements.invoke(pathList, *args) as? Array<*> ?: return false

            val merged = java.lang.reflect.Array.newInstance(elementClass, existing.size + newElements.size)
            existing.forEachIndexed { i, e -> java.lang.reflect.Array.set(merged, i, e) }
            newElements.forEachIndexed { i, e -> java.lang.reflect.Array.set(merged, existing.size + i, e) }
            elementsField.set(pathList, merged)
            true
        } catch (t: Throwable) {
            Log.w("pathList.dexElements 注入失败：${t.javaClass.simpleName}: ${t.message}", t)
            false
        }
    }

    /** 方式 3：把 dex 路径拼到 pathList.dexPath 字符串上（新 ART 可能忽略它）。 */
    private fun tryPatchDexPath(loader: ClassLoader, dexPath: String): Boolean {
        return try {
            val pathList = field(loader.javaClass, "pathList")?.get(loader) ?: return false
            val dexPathField = field(pathList.javaClass, "dexPath") ?: return false
            dexPathField.isAccessible = true
            val current = dexPathField.get(pathList) as? String ?: return false
            if (current.contains(dexPath)) return true
            dexPathField.set(pathList, "$current:$dexPath")
            true
        } catch (t: Throwable) {
            Log.w("pathList.dexPath 注入失败：${t.javaClass.simpleName}: ${t.message}")
            false
        }
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
