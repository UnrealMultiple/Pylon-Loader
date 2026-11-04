package dev.pylon.loader.common

/**
 * 运行时目录布局：启动器层与宿主层共用的唯一契约。
 * 两端分属两个进程，路径不一致就是"游戏起不来"且日志无线索，所以共享一份定义。
 */
object RuntimeLayout {

    /** 目标游戏包名（docs/01：第一阶段只针对这一份构建）。 */
    const val GAME_PACKAGE: String = "com.and.games505.Terraria"

    /** 只认这条线（开发基线 1.4.5.8.6）：小版本放开，游戏打补丁不用改代码。 */
    const val GAME_VERSION_LINE: String = "1.4.5"

    fun isSupportedVersion(versionName: String?): Boolean =
        versionName != null && versionName.trim().split('.').take(3).joinToString(".") == GAME_VERSION_LINE

    /** 只保留 arm64-v8a。 */
    const val ABI: String = "arm64-v8a"

    const val APK_LIB_DIR: String = "lib/$ABI"
    const val APK_ASSETS_PREFIX: String = "assets/bin/Data"

    /** 加载顺序固定：libc++_shared 必须最先，否则后续 .so 会 UnsatisfiedLinkError。 */
    val NATIVE_LIB_LOAD_ORDER: List<String> = listOf(
        "libc++_shared.so",
        "libmain.so",
        "libunity.so",
        "libil2cpp.so",
    )

    const val IMPORT_DIR_NAME: String = "runtime"
    const val MANIFEST_FILE_NAME: String = "import-manifest.properties"
    const val NATIVE_SUBDIR: String = "lib"
    const val DATA_SUBDIR: String = "Data"
    /** 宿主自己写的崩溃日志（Java 未捕获异常、启动失败）。 */
    const val CRASH_LOG_NAME: String = "crash-last.txt"

    /** 启动器补记的进程异常退出（原生崩溃 / ANR），宿主来不及写日志时由 [ActivityManager] 的退出记录生成。 */
    const val CRASH_EXIT_REPORT_NAME: String = "crash-exit.txt"

    /** 宿主进程名后缀：GameActivity 在 :app manifest 里声明的是 android:process=":game"。 */
    const val GAME_PROCESS_SUFFIX: String = ":game"

    const val LOG_TAG: String = "PylonLoader"

    /** 归一化成 `/` 分隔路径：Windows 上 relativeTo 给 `Data\...`，不归一化会让清单键跨平台静默不一致。 */
    fun normalizeRelativePath(path: String): String = path.replace('\\', '/').trimStart('/')
}
