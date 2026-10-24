package dev.pylon.loader

import dev.pylon.loader.common.ImportManifest
import dev.pylon.loader.common.RuntimeLayout
import java.io.File

/** 一个待校验的文件：是否在、实际大小、清单里记录的大小。 */
internal data class FileCheck(
    val name: String,
    val path: String,
    val exists: Boolean,
    val size: Long,
    val expectedSize: Long? = null,
) {
    /** 存在且（清单有记录时）大小一致。 */
    val matches: Boolean
        get() = exists && (expectedSize == null || size == expectedSize)

    /** 给界面用的一句话状态；通过时为 null。 */
    val problem: String?
        get() = when {
            !exists -> "缺失"
            expectedSize != null && size != expectedSize -> "大小不符"
            else -> null
        }
}

internal data class CrashInfo(
    val file: File,
    val size: Long,
    val modifiedAt: Long,
)

/**
 * 启动器一屏所需的全部事实，全部从磁盘/包管理器重新读，不缓存跨进程假设。
 * UI 层预先算"就绪"：用户点按钮前就知道文件齐不齐，而不是启动后才看到失败页。
 */
internal data class LauncherState(
    val selectedPackage: String,
    val gameLabel: String?,
    val gameInstalled: Boolean,
    val gameVersionName: String?,
    val gameVersionCode: Long?,
    val manifest: ImportManifest?,
    val nativeChecks: List<FileCheck>,
    val dataChecks: List<FileCheck>,
    val crash: CrashInfo?,
    val runtimeDir: File,
    val modsDir: File,
    val mods: List<ModInfo>,
) {
    val enabledModCount: Int
        get() = mods.count { it.enabled }

    val gameVersionMatches: Boolean
        get() = gameInstalled && gameVersionCode == RuntimeLayout.GAME_VERSION_CODE

    /** 清单必须是给"当前选中的游戏"导入的；换游戏后要重新导入。 */
    val manifestMatchesSelection: Boolean
        get() = manifest != null &&
            (manifest.gamePackage.isEmpty() || manifest.gamePackage == selectedPackage)

    val manifestValid: Boolean
        get() = manifest != null &&
            manifest.gameVersionCode == RuntimeLayout.GAME_VERSION_CODE &&
            manifest.abi == RuntimeLayout.ABI

    val checks: List<FileCheck>
        get() = nativeChecks + dataChecks

    val failedChecks: List<FileCheck>
        get() = checks.filterNot { it.matches }

    /** 可以启动：游戏版本对、清单属于当前游戏、所有关键文件都在且大小一致。 */
    val ready: Boolean
        get() = gameVersionMatches && manifestValid && manifestMatchesSelection && failedChecks.isEmpty()

    /** 可以先导入：游戏已安装且版本受支持。 */
    val canImport: Boolean
        get() = gameVersionMatches

    val importedAt: Long
        get() = manifest?.importedAtEpochMs ?: 0L
}
