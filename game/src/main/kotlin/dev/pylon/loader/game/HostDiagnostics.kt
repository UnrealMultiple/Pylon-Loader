package dev.pylon.loader.game

import android.app.Activity
import android.content.pm.PackageManager
import android.view.View
import android.view.ViewGroup

/**
 * 层：宿主层 / 诊断探针（不参与游戏逻辑）
 *
 * 引擎起不来时用来区分"native 没绑定"、"视图没就位"、"资源表没读到"这几类原因。
 * 只在可调试包或显式开关下调用（见 HostOptions.diagnostics）。
 */
internal object HostDiagnostics {

    /** meta-data 的值类型混杂（boolean / string / int），所以用不做类型假设的 [Bundle.get]。 */
    @Suppress("DEPRECATION")
    fun logResources(activity: Activity) {
        runCatching {
            val res = activity.resources
            val pkg = activity.packageName
            Log.stage("probe", "packageName=$pkg res=${res.javaClass.name}")

            // 复刻 UnityPlayer.GetGlViewContentDescription 的取法：按名字查本包资源。
            val id = res.getIdentifier("game_view_content_description", "string", pkg)
            Log.stage("probe", "getIdentifier(game_view_content_description) = 0x${id.toString(16)}")
            if (id != 0) Log.stage("probe", "getString = ${runCatching { res.getString(id) }.getOrNull()}")

            // Unity 的 splash/fullscreen 开关来自宿主包 meta-data；必须走 PackageManager + GET_META_DATA（Activity 缓存的 ApplicationInfo 不带 metaData）。
            val meta = activity.packageManager
                .getApplicationInfo(pkg, PackageManager.GET_META_DATA).metaData
            for (key in UNITY_META_KEYS) {
                Log.stage("probe", "metaData[$key] = ${runCatching { meta?.get(key) }.getOrNull()}")
            }
        }.onFailure { Log.w("资源探针失败：${it.javaClass.simpleName}: ${it.message}") }
    }

    fun logViewTree(activity: Activity) {
        runCatching {
            val root = activity.window.decorView
            Log.stage("probe", "decorView attached=${root.isAttachedToWindow} ${root.width}x${root.height}")
            logView(root, 0)
        }.onFailure { Log.w("视图探针失败：${it.message}") }
    }

    fun logPlayerInternals(activity: Activity, player: UnityPlayerRef) {
        runCatching {
            val clazz = Class.forName("com.unity3d.player.UnityPlayer")
            val instance = player.rawInstance()
            val natives = clazz.declaredMethods.filter { java.lang.reflect.Modifier.isNative(it.modifiers) }
            Log.stage("probe", "native 方法 ${natives.size} 个：${natives.take(5).joinToString { it.name }}")

            for (name in PLAYER_FIELDS) {
                val value = clazz.getDeclaredField(name).apply { isAccessible = true }.get(instance)
                val size = if (value is View) " attached=${value.isAttachedToWindow} ${value.width}x${value.height}" else ""
                Log.stage("probe", "  $name = ${value?.javaClass?.name}$size")
            }
            Log.stage("probe", "surfaceView=${player.surfaceView()?.javaClass?.name}")
        }.onFailure { Log.w("UnityPlayer 探针失败：${it.javaClass.simpleName}: ${it.message}") }
    }

    private fun logView(view: View, depth: Int) {
        if (depth > 3) return
        Log.stage(
            "probe",
            "${"  ".repeat(depth)}${view.javaClass.name} vis=${view.visibility} " +
                "attached=${view.isAttachedToWindow} ${view.width}x${view.height}"
        )
        if (view is ViewGroup) for (i in 0 until view.childCount) logView(view.getChildAt(i), depth + 1)
    }

    private val UNITY_META_KEYS = listOf(
        "unity.splash-enable", "unity.splash-mode", "unity.launch-fullscreen", "unity.auto-report-fully-drawn",
    )
    private val PLAYER_FIELDS = listOf("mGlView", "m_SplashScreen", "mState", "mQuitting")
}
