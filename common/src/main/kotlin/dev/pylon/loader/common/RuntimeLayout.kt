package dev.pylon.loader.common

/**
 * 运行时目录布局：启动器层与宿主层共用的唯一契约。
 * 两端分属两个进程，路径不一致就是"游戏起不来"且日志无线索，所以共享一份定义。
 */
object RuntimeLayout {

    /** 目标游戏包名（docs/01：第一阶段只针对这一份构建）。 */
    const val GAME_PACKAGE: String = "com.and.games505.Terraria"

    /** 目标版本；libil2cpp.so 与 global-metadata.dat 必须同构建，版本不符直接拒绝导入。 */
    const val GAME_VERSION_CODE: Long = 301729L
    const val GAME_VERSION_NAME: String = "1.4.5.8.6"

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
    const val CRASH_LOG_NAME: String = "crash-last.txt"
    const val LOG_TAG: String = "PylonLoader"

    /** 归一化成 `/` 分隔路径：Windows 上 relativeTo 给 `Data\...`，不归一化会让清单键跨平台静默不一致。 */
    fun normalizeRelativePath(path: String): String = path.replace('\\', '/').trimStart('/')
}
