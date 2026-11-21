package dev.pylon.loader

import java.io.File

/** 一项待校验的运行时文件：是否在、实际大小。 */
internal data class FileCheck(
    val name: String,
    val path: String,
    val exists: Boolean,
    val size: Long,
) {
    val matches: Boolean get() = exists

    /** 给界面用的一句话状态；通过时为 null。 */
    val problem: String? get() = if (exists) null else "缺失"
}

/**
 * 启动器一屏所需的全部事实，全部从磁盘/AssetManager 重新读，不缓存跨进程假设。
 * UI 层预先算"就绪"：用户点按钮前就知道内置运行时齐不齐，而不是启动后才看到失败页。
 */
internal data class LauncherState(
    val nativeChecks: List<FileCheck>,
    val dataChecks: List<FileCheck>,
    val crash: CrashInfo?,
    val modsDir: File,
    val mods: List<ModInfo>,
) {
    val enabledModCount: Int
        get() = mods.count { it.enabled }

    val checks: List<FileCheck>
        get() = nativeChecks + dataChecks

    val failedChecks: List<FileCheck>
        get() = checks.filterNot { it.matches }

    /** 可以启动：内置运行时的关键文件全部在位。 */
    val ready: Boolean
        get() = failedChecks.isEmpty()
}
