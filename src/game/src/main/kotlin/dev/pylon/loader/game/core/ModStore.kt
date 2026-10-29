package dev.pylon.loader.game.core

import android.content.Context
import dev.pylon.loader.game.Log
import java.io.File

/**
 * 层：宿主层 / Mod 目录（:game 与 :app 共用同一套规则）
 *
 *   /storage/emulated/0/Android/data/dev.pylon.loader/Mods/
 *       <modId>/mod.dll + manifest.json (+ assets/)
 *       <任意名>.dll        也支持直接把 dll 丢在 Mods/ 下（文件名当 id）
 *
 * 放 app 专属外部目录：用户可直接丢文件、不需要任何权限（共享存储根在 Android 11+ 要权限）。
 */
internal object ModStore {

    const val DIR_NAME = "Mods"
    private const val README = "说明.txt"

    /** 外部存储不可用时退回内部目录，功能不缺失。 */
    fun resolve(context: Context): File {
        val externalRoot = context.getExternalFilesDir(null)?.parentFile
        if (externalRoot != null && externalRoot.canWrite()) return File(externalRoot, DIR_NAME)
        Log.w("外部存储不可用，Mod 目录退回内部：${context.filesDir}")
        return File(context.filesDir, DIR_NAME)
    }

    fun ensure(context: Context): File {
        val dir = resolve(context)
        if (!dir.isDirectory && !dir.mkdirs()) {
            Log.e("Mod 目录创建失败：$dir")
            return dir
        }
        val readme = File(dir, README)
        if (!readme.isFile) runCatching { readme.writeText(README_TEXT) }
        return dir
    }

    /** (子目录形式的 Mod, 直接丢在 Mods/ 下的 dll)，仅用于日志清点。 */
    fun inventory(dir: File): Pair<List<File>, List<File>> {
        if (!dir.isDirectory) return emptyList<File>() to emptyList()
        val folders = dir.listFiles { f -> f.isDirectory && findDll(f) != null }
            ?.sortedBy { it.name } ?: emptyList()
        val loose = dir.listFiles { f -> f.isFile && f.extension.equals("dll", ignoreCase = true) }
            ?.sortedBy { it.name } ?: emptyList()
        return folders to loose
    }

    /** 优先 `mod.dll`，否则取排序后第一个 `*.dll`。 */
    fun findDll(folder: File): File? {
        val preferred = File(folder, "mod.dll")
        if (preferred.isFile) return preferred
        return folder.listFiles { f -> f.isFile && f.extension.equals("dll", ignoreCase = true) }
            ?.sortedBy { it.name }
            ?.firstOrNull()
    }

    /**
     * 随包示例 Mod 已从构建移除（assets/pylon-samples 不再生成），这里不再自动部署：
     * 原来会在版本变化时把示例整目录重建进用户的 Mods/，那是用户自己的目录，加载器不该往里写
     * （会踩掉用户改过的文件、分不清哪些是自己装的）。示例源码留在 samples/，要试就自己放 dll。
     */

    private val README_TEXT = """
        Pylon Loader — Mod 目录
        =======================

        每个 Mod 一个文件夹（名字随意，建议用 Mod 的 id）：

            Mods/
              MyMod/
                MyMod.dll        主程序集：与目录同名，或叫 mod.dll
                manifest.json    可选：元数据（没有代码坐标，入口靠接口发现）
                lib/             可选：Mod 的私有依赖（宿主只共享自己的程序集）
                assets/          可选：Mod 自己的资源

        也可以把单个 dll 直接丢在 Mods/ 下面（用文件名当 id）。

        单文件包：把 .pmod 直接放进 Mods/（zip：manifest.json + mod.dll + 可选 icon.png/assets/lib）。
        宿主启动时会把 .pmod 解到内部缓存再交给同一个加载器，和文件夹 Mod 走同一套规则。
        Mod 图标：Mod 根目录 icon.png（或 assets/icon.png，或 manifest 的 icon 字段）。

        启动器里的「Mod 管理」开关会写 Mods/.pylon/selection.json：
        它优先于各 Mod 自己 manifest.json 的 enabled；没有记录的 Mod 默认启用。
        这个文件由启动器维护，不需要手改。

        Mod 写法：实现 Pylon.Mod.IPylonMod 的**公开类 + 公开无参构造函数**

            public sealed class Mod : Pylon.Mod.IPylonMod
            {
                public void Load(IModContext context)
                {
                    context.Log("hello");
                    context.Harmony.PatchAll(typeof(Mod).Assembly);  // 打补丁要自己显式打
                }
            }

        · 宿主反射发现实现者并建**实例**，所以 Mod 可以用字段存状态；
        · Load 在**游戏线程**上调用；构造函数里不要碰游戏对象；
        · 一个程序集只应有一个实现（多了会报错并列出来）；
        · 私有依赖放同目录或 lib/，宿主不探测它们（宿主的程序集永远共享，Mod 不能遮蔽）。

        manifest.json 字段（全部可选）：

            {
              "id": "MyMod",
              "version": "1.0.0",
              "dependencies": ["OtherMod"],  依赖的 Mod id，按依赖顺序加载
              "enabled": true                设 false 可临时停用
            }

        改完文件重启游戏生效。
    """.trimIndent()
}
