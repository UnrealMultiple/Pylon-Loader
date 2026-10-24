package dev.pylon.loader.game

import android.app.Activity
import android.content.Intent
import android.content.res.Configuration
import android.view.InputEvent
import android.view.SurfaceView
import android.view.View
import java.lang.reflect.InvocationHandler
import java.lang.reflect.InvocationTargetException
import java.lang.reflect.Method
import java.lang.reflect.Proxy

/**
 * 用反射创建并驱动 `com.unity3d.player.UnityPlayer`，让宿主编译期不依赖游戏类
 * （发行形态要求 Release 不含游戏二进制，docs/02 形态 B；引入 unity-classes.jar 就等于带进游戏代码）。
 *
 * 方法名与签名**全部取自 refs/classes.dex 的 UnityPlayer**：
 * <init>(Context) / <init>(Context, IUnityPlayerLifecycleEvents)；onStart/onResume/onPause/onStop/
 * destroy/quit/lowMemory；windowFocusChanged(boolean)/configurationChanged(Configuration)/
 * newIntent(Intent)/injectEvent(InputEvent):boolean/getView():View。
 * 注意：**没有** onWindowFocusChanged/onConfigurationChanged/onNewIntent —— 早先按 Activity 方法名
 * 反射，全部静默跳过（只留一条 warning）。
 */
internal class UnityPlayerRef private constructor(
    private val instance: Any,
    private val clazz: Class<*>,
) {

    /** 反射结果缓存：生命周期每秒都可能被调，不值得每次查一遍。 */
    private val methodCache = HashMap<String, Method?>()

    fun asView(): View? = instance as? View

    /** 供诊断探针与 Activity.mUnityPlayer 字段取原始实例。 */
    fun rawInstance(): Any = instance

    // --- 生命周期（名字即 UnityPlayer 的方法名） ---

    fun onStart() = call("onStart")
    fun onResume() = call("onResume")
    fun onPause() = call("onPause")
    fun onStop() = call("onStop")
    fun destroy() = call("destroy")
    fun lowMemory() = call("lowMemory")

    /**
     * **引擎的 resume 门就在这个调用里**（refs/classes.dex 2384d4 起）：
     * `windowFocusChanged(hasFocus)`：记下聚焦 → `mState.a()` 是则 return → 软键盘处理 →
     * `checkResumePlayer()` 只有走到这里才会 queueGLThreadEvent 把引擎 resume 起来。
     * 少了它 `setupUnityToBeResumed()` 的状态检查永远过不去，引擎不会启动：进程活着、
     * SurfaceView 有尺寸、logcat 里一条 I/Unity 都没有——本机就是这么卡住的。
     */
    fun windowFocusChanged(hasFocus: Boolean) = call("windowFocusChanged", hasFocus)

    fun configurationChanged(config: Configuration) = call("configurationChanged", config)

    fun newIntent(intent: Intent) = call("newIntent", intent)

    /** 把按键/触摸/鼠标事件交给引擎。返回是否被消费。 */
    fun injectEvent(event: InputEvent): Boolean =
        call("injectEvent", event) as? Boolean ?: false

    /**
     * **在游戏线程（UnityMain）上执行 runnable**。
     * `UnityPlayer.queueGLThreadEvent(Runnable)` 的语义（refs/classes.dex 反编译确认）：
     * 当前线程就是 m_MainThread → 直接 run()；否则塞进 m_Events，由 m_MainThread 取走执行。
     * m_MainThread（com.unity3d.player.L0）的 run() 第一句就是 setName("UnityMain") +
     * Looper.prepare() + Looper.loop()，所以它就是游戏线程 —— 托管方法只能在这个线程上调
     * （否则 SIGSEGV，本机实测 :game 反复重启）。上游用 hook Main.DoUpdate 拿同一信息
     * （runtime::noteGameThread），这里用 Unity 自己的 Java 侧队列，等价且不需要 hook。
     *
     * @return false 表示没能投递（反射失败），调用方应自行降级并说明。
     */
    fun runOnGameThread(runnable: Runnable): Boolean {
        // 这是包内可见（package-private）的方法，只能反射调；它不是 Android 框架 API，
        // 所以不受 hidden API 限制。
        val method = runCatching {
            clazz.getDeclaredMethod("queueGLThreadEvent", Runnable::class.java)
                .apply { isAccessible = true }
        }.getOrNull() ?: run {
            Log.w("UnityPlayer 没有 queueGLThreadEvent(Runnable)，无法投递到游戏线程")
            return false
        }
        return try {
            method.invoke(instance, runnable)
            true
        } catch (t: Throwable) {
            Log.w("投递到游戏线程失败：${rootCause(t)}")
            false
        }
    }

    /** Unity 的渲染 SurfaceView；游戏原版反射读 `UnityPlayer.mGlView`，这里同样先读字段，再退回遍历子 View。 */
    fun surfaceView(): SurfaceView? {
        runCatching {
            val field = clazz.getDeclaredField("mGlView").apply { isAccessible = true }
            (field.get(instance) as? SurfaceView)?.let { return it }
        }
        return (instance as? android.view.ViewGroup)
            ?.let { vg -> (0 until vg.childCount).map { vg.getChildAt(it) } }
            ?.filterIsInstance<SurfaceView>()
            ?.firstOrNull()
    }

    /**
     * 按「方法名 + 参数个数」取方法并调用，参数值原样传入。
     * 只传**值**，绝不把 `Xxx::class.java` 当参数：早先类型与值混在同一 vararg 按个数匹配，
     * 于是 `windowFocusChanged(true)` 被当成找 2 参数方法而永远找不到 —— 那正是引擎 resume 的门，
     * 结果黑屏且零 Unity 日志（见 docs/07）。这些方法名在各自参数个数下唯一，按个数匹配安全。
     */
    private fun call(name: String, vararg args: Any?): Any? {
        val key = "$name/${args.size}"
        val method = methodCache.getOrPut(key) {
            clazz.methods.firstOrNull { it.name == name && it.parameterTypes.size == args.size }
        }
        if (method == null) {
            Log.w("UnityPlayer 没有 $name/${args.size} 参数的方法，跳过")
            return null
        }
        return try {
            method.invoke(instance, *args)
        } catch (t: Throwable) {
            Log.w("调用 UnityPlayer.$name 失败：${rootCause(t)}")
            null
        }
    }

    companion object {

        /** activity 必须是真 Activity：UnityPlayer.<init> 里是裸 instanceof Activity 判断。 */
        fun create(activity: Activity, lifecycleEvents: Any): UnityPlayerRef {
            val clazz = Class.forName("com.unity3d.player.UnityPlayer")
            val lifecycleClass = Class.forName("com.unity3d.player.IUnityPlayerLifecycleEvents")

            // 不能按精确类型找（参数声明是 Context/Activity，传 GameActivity.class 会 NoSuchMethod），按可赋值性匹配。
            val twoArg = clazz.constructors.firstOrNull { ctor ->
                val types = ctor.parameterTypes
                types.size == 2 &&
                    types[0].isAssignableFrom(activity.javaClass) &&
                    types[1].isAssignableFrom(lifecycleClass)
            }
            if (twoArg != null) {
                twoArg.isAccessible = true
                return UnityPlayerRef(twoArg.newInstance(activity, lifecycleEvents), clazz)
            }

            // 回退旧版单参数构造（内部会委托到两参数版本）；代价是生命周期回调失效（Unity 不持有
            // 我们的代理）→ onQuit/onUnloaded 永远不会来，所以必须打日志说明退化，否则"游戏内退出后
            // Activity 留在前台"会被当成别的问题排查。
            val oneArg = clazz.constructors.firstOrNull { ctor ->
                ctor.parameterTypes.size == 1 && ctor.parameterTypes[0].isAssignableFrom(activity.javaClass)
            } ?: throw NoSuchMethodException("UnityPlayer 没有可用的 (Context/Activity[, IUnityPlayerLifecycleEvents]) 构造器")
            Log.w("UnityPlayer 只有单参数构造器：生命周期回调不可用（onQuit/onUnloaded 不会触发）")
            oneArg.isAccessible = true
            return UnityPlayerRef(oneArg.newInstance(activity), clazz)
        }

        /**
         * 用动态代理实现游戏的 `IUnityPlayerLifecycleEvents`。
         * 本构建里接口**只有两个方法**（refs/classes.dex 反查）：onUnityPlayerQuitted()（游戏里空实现）、
         * onUnityPlayerUnloaded()（游戏里 moveTaskToBack(true)）。名字写错回调永远不会来（早先按 onUnityPlayerQuit 分发）。
         */
        fun createLifecycleProxy(
            onQuit: () -> Unit,
            onUnloaded: () -> Unit,
        ): Any {
            val lifecycleClass = Class.forName("com.unity3d.player.IUnityPlayerLifecycleEvents")
            val handler = InvocationHandler { _, method: Method, args: Array<Any?>? ->
                when (method.name) {
                    "onUnityPlayerQuitted" -> {
                        onQuit()
                        null
                    }
                    "onUnityPlayerUnloaded" -> {
                        onUnloaded()
                        null
                    }
                    // toString/hashCode/equals 等走默认值，避免代理自身行为异常。
                    else -> when (method.returnType) {
                        java.lang.Boolean.TYPE -> false
                        Integer.TYPE -> 0
                        java.lang.Long.TYPE -> 0L
                        else -> null
                    }
                }
            }
            return Proxy.newProxyInstance(lifecycleClass.classLoader, arrayOf(lifecycleClass), handler)
        }

        /** 反射异常常被 InvocationTargetException 包一层，日志要露出根因。 */
        fun rootCause(t: Throwable): String {
            var cause = t
            while (cause is InvocationTargetException && cause.targetException != null) {
                cause = cause.targetException
            }
            return "${cause.javaClass.simpleName}: ${cause.message}"
        }
    }
}
