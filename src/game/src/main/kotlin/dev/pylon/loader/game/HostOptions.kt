package dev.pylon.loader.game

import android.content.Intent
import dev.pylon.loader.common.RuntimeLayout

/// 宿主启动参数：`:app` 组装、`:game` 读取。extra 的键只在这里定义一份。
data class HostOptions(
    val gamePackage: String = RuntimeLayout.GAME_PACKAGE,
    val bogusRef: String? = null,
    val refsProbe: String? = null,
    val modProbe: Boolean = false,
    val coreClr: Boolean = true,
    val probe: Boolean = true,
    val diagnostics: Boolean = false,
) {
    companion object {
        const val EXTRA_BOGUS_REF = "bogusRef"
        const val EXTRA_REFS_PROBE = "refsProbe"
        const val EXTRA_MOD_PROBE = "modProbe"
        const val EXTRA_CORECLR = "coreclr"
        const val EXTRA_PROBE = "probe"
        const val EXTRA_DIAGNOSTICS = "diagnostics"
        const val EXTRA_GAME_PACKAGE = "gamePackage"

        fun from(intent: Intent?, debuggable: Boolean): HostOptions {
            if (intent == null) return HostOptions(diagnostics = debuggable)
            return HostOptions(
                gamePackage = intent.getStringExtra(EXTRA_GAME_PACKAGE) ?: RuntimeLayout.GAME_PACKAGE,
                bogusRef = intent.getStringExtra(EXTRA_BOGUS_REF),
                refsProbe = intent.getStringExtra(EXTRA_REFS_PROBE),
                modProbe = intent.getBooleanExtra(EXTRA_MOD_PROBE, false),
                coreClr = intent.getBooleanExtra(EXTRA_CORECLR, true),
                probe = intent.getBooleanExtra(EXTRA_PROBE, true),
                diagnostics = intent.getBooleanExtra(EXTRA_DIAGNOSTICS, debuggable),
            )
        }

        /// 把已识别的开关原样转交给宿主（有 extra 就转发，包含显式 false）。
        fun copyInto(source: Intent?, target: Intent) {
            if (source == null) return
            source.getStringExtra(EXTRA_GAME_PACKAGE)?.let { target.putExtra(EXTRA_GAME_PACKAGE, it) }
            source.getStringExtra(EXTRA_BOGUS_REF)?.let { target.putExtra(EXTRA_BOGUS_REF, it) }
            source.getStringExtra(EXTRA_REFS_PROBE)?.let { target.putExtra(EXTRA_REFS_PROBE, it) }
            for (key in listOf(EXTRA_MOD_PROBE, EXTRA_CORECLR, EXTRA_PROBE, EXTRA_DIAGNOSTICS)) {
                if (source.hasExtra(key)) target.putExtra(key, source.getBooleanExtra(key, false))
            }
        }
    }
}
