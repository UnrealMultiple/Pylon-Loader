package dev.pylon.loader.common

/**
 * 运行时契约：启动器层与宿主层共用的常量。
 * 两端分属两个进程，路径/名字不一致就是"游戏起不来"且日志无线索，所以共享一份定义。
 * 运行时（游戏 .so、bin/Data、classes.dex）随构建内置在宿主 APK 里，不再有"用户导入"。
 */
object RuntimeLayout {

    /** 内置运行时对应的游戏（信息展示与日志用）。 */
    const val GAME_PACKAGE: String = "com.and.games505.Terraria"

    /** 内置运行时的游戏版本线（信息展示用）。 */
    const val GAME_VERSION_LINE: String = "1.4.6"

    /** 只保留 arm64-v8a。 */
    const val ABI: String = "arm64-v8a"

    /** 游戏数据在 APK assets 里的前缀。 */
    const val GAME_DATA_ASSET_PREFIX: String = "bin/Data"

    /** 游戏 dex 在 APK assets 里的位置；宿主启动时解到 filesDir 再注入 classloader。 */
    const val GAME_DEX_ASSET: String = "pylon-game/classes.dex"

    /** 游戏dex 解包落盘目录（filesDir 下）。 */
    const val GAME_DEX_DIR_NAME: String = "pylon-game"

    /** 内置游戏 .so 的加载顺序：libmain 的 JNI_OnLoad 要先于 Unity/il2cpp 进场。 */
    val NATIVE_LIB_LOAD_ORDER: List<String> = listOf(
        "libc++_shared.so",
        "libmain.so",
        "libunity.so",
        "libGNS_Wrapper.so",
        "libil2cpp.so",
    )

    val OPTIONAL_NATIVE_LIBS: Set<String> = setOf("libc++_shared.so", "libGNS_Wrapper.so")

    /** 宿主自己写的崩溃日志（Java 未捕获异常、启动失败）。 */
    const val CRASH_LOG_NAME: String = "crash-last.txt"

    /** 启动器补记的进程异常退出（原生崩溃 / ANR），宿主来不及写日志时由 [ActivityManager] 的退出记录生成。 */
    const val CRASH_EXIT_REPORT_NAME: String = "crash-exit.txt"

    /** 宿主进程名后缀：GameActivity 在 :app manifest 里声明的是 android:process=":game"。 */
    const val GAME_PROCESS_SUFFIX: String = ":game"

    const val LOG_TAG: String = "PylonLoader"
}
