package dev.pylon.loader

import android.content.Context
import java.io.File

/** 启动器与 Mod 页共用的目录规则：app 专属外部目录，不需要额外存储权限。 */
internal object ModStorage {

    const val DIR_NAME = "Mods"

    fun directory(context: Context): File {
        val externalRoot = context.getExternalFilesDir(null)?.parentFile
        return if (externalRoot != null) {
            File(externalRoot, DIR_NAME)
        } else {
            File(context.filesDir, DIR_NAME)
        }
    }

    fun ensure(context: Context): File = directory(context).apply { mkdirs() }
}
