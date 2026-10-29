package dev.pylon.loader.game

import android.content.Context
import android.content.pm.ApplicationInfo
import android.content.res.AssetManager

/**
 * 层：宿主层 / 资源挂载
 *
 * Unity 通过 AssetManager 读 `assets/bin/Data`；导入形态要读已安装游戏的 APK，用隐藏 API
 * `addAssetPath`（反射）把所选游戏的 base.apk 挂到同一 AssetManager。挂载是增量：失败时
 * debug 变体仍可用 APK 内置资源，不会弄坏游戏。
 */
internal object GameAssets {

    fun mountGameApk(context: Context, gameInfo: ApplicationInfo): Boolean {
        val apk = gameInfo.sourceDir ?: return false
        return try {
            val method = AssetManager::class.java.getMethod("addAssetPath", String::class.java)
            val cookie = method.invoke(context.assets, apk) as? Int ?: 0
            Log.stage("host", "挂载游戏 APK 资源：${gameInfo.packageName} → $apk（cookie=$cookie）")
            cookie != 0
        } catch (t: Throwable) {
            Log.w("挂载游戏 APK 资源失败（回退 APK 内置资源）：${t.javaClass.simpleName}: ${t.message}")
            false
        }
    }
}
