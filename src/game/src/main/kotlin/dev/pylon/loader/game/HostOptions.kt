package dev.pylon.loader.game

import android.content.Intent
import dev.pylon.loader.common.RuntimeLayout

/**
 * 宿主启动参数：`:app` 组装、`:game` 读取。extra 的键只在这里定义一份。
 *
 * 放在 `:game` 而不是 `:common`：`:common` 是纯 Kotlin 契约（不碰 Android API），
 * 而这里要解析 `Intent`。`:app` 依赖 `:game`，所以启动器可以直接用这几个常量与转发函数。
 */
data class HostOptions(
    /** 启动器选中的已安装游戏包名；默认目标包。 */
    val gamePackage: String = RuntimeLayout.GAME_PACKAGE,
    /** 故意写错的引用，验证"引用校验失败 → 停用而不是崩"。 */
    val bogusRef: String? = null,

    /** 整份替换引用清单（逗号分隔），用来探名字/参数个数。 */
    val refsProbe: String? = null,

    /** 跑一次 C# 方法绑定自检。 */
    val bindTest: Boolean = false,

    /** 跑测试 Mod 探针（独立 .so 经导出 ABI 调内核）。 */
    val modProbe: Boolean = false,

    /** 起 CoreCLR 并加载 C# Mod。默认开：手动点图标启动也要有 Mod（extra 可显式关掉）。 */
    val coreClr: Boolean = true,

    /** 内核静置时间（越小越危险，见 src/core/src/boot/Probe.h）。 */
    val settleMs: Int = DEFAULT_SETTLE_MS,
    /** 是否启动内核探测。默认关：探测时机只能靠静置猜测，而引擎启动期调用 IL2CPP 会 SIGSEGV（见 docs/15）。 */
    val probe: Boolean = true,

    /** 诊断探针（视图树、UnityPlayer 内部、资源表）；release 默认关闭。 */
    val diagnostics: Boolean = false,
) {
    companion object {
        const val EXTRA_BOGUS_REF = "bogusRef"
        const val EXTRA_REFS_PROBE = "refsProbe"
        const val EXTRA_BIND_TEST = "bindTest"
        const val EXTRA_MOD_PROBE = "modProbe"
        const val EXTRA_CORECLR = "coreclr"
        const val EXTRA_SETTLE_MS = "settleMs"
        const val EXTRA_PROBE = "probe"
        const val EXTRA_DIAGNOSTICS = "diagnostics"
        const val EXTRA_GAME_PACKAGE = "gamePackage"

        /** 与内核侧默认值一致（`PylonCore.DEFAULT_SETTLE_MS`）。 */
        const val DEFAULT_SETTLE_MS = 25_000

        /** @param debuggable 可调试包默认开诊断探针。 */
        fun from(intent: Intent?, debuggable: Boolean): HostOptions {
            if (intent == null) return HostOptions(diagnostics = debuggable)
            val settleMs = intent.getIntExtra(EXTRA_SETTLE_MS, 0)
            return HostOptions(
                gamePackage = intent.getStringExtra(EXTRA_GAME_PACKAGE) ?: RuntimeLayout.GAME_PACKAGE,
                bogusRef = intent.getStringExtra(EXTRA_BOGUS_REF),
                refsProbe = intent.getStringExtra(EXTRA_REFS_PROBE),
                bindTest = intent.getBooleanExtra(EXTRA_BIND_TEST, false),
                modProbe = intent.getBooleanExtra(EXTRA_MOD_PROBE, false),
                coreClr = intent.getBooleanExtra(EXTRA_CORECLR, true),
                settleMs = if (settleMs > 0) settleMs else DEFAULT_SETTLE_MS,
            probe = intent.getBooleanExtra(EXTRA_PROBE, true),
                diagnostics = intent.getBooleanExtra(EXTRA_DIAGNOSTICS, debuggable),
            )
        }

        /** 把已识别的开关原样转交给宿主（`:app` 拉起 `:game` 时用）。 */
        fun copyInto(source: Intent?, target: Intent) {
            if (source == null) return
            source.getStringExtra(EXTRA_GAME_PACKAGE)?.let { target.putExtra(EXTRA_GAME_PACKAGE, it) }
            source.getStringExtra(EXTRA_BOGUS_REF)?.let { target.putExtra(EXTRA_BOGUS_REF, it) }
            source.getStringExtra(EXTRA_REFS_PROBE)?.let { target.putExtra(EXTRA_REFS_PROBE, it) }
            // 有 extra 就原样转发（包含显式 false，否则"关掉"的意图会丢失）
            for (key in listOf(EXTRA_BIND_TEST, EXTRA_MOD_PROBE, EXTRA_CORECLR, EXTRA_PROBE,
                               EXTRA_DIAGNOSTICS)) {
                if (source.hasExtra(key)) target.putExtra(key, source.getBooleanExtra(key, false))
            }
            source.getIntExtra(EXTRA_SETTLE_MS, 0).takeIf { it > 0 }
                ?.let { target.putExtra(EXTRA_SETTLE_MS, it) }
        }
    }
}