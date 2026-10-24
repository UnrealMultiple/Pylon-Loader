package com.google.android.play.core.assetpacks

/**
 * 兼容占位：来源 docs/01 清单（Play Core / Asset Delivery）。
 * Unity 自带的 com.unity3d.player.IAssetPackManager* 随 unity-classes 一起进来，无需补（docs/01 已核实）。
 */

class AssetPackState {
    fun name(): String = ""
    fun status(): Int = ASSET_PACK_STATUS_UNKNOWN
    fun totalBytes(): Long = 0L
    fun bytesDownloaded(): Long = 0L
    fun transferProgress(): Double = 0.0

    companion object {
        const val ASSET_PACK_STATUS_UNKNOWN: Int = 0
        const val ASSET_PACK_STATUS_COMPLETED: Int = 4
    }
}

class AssetPackStates {
    fun packStates(): Map<String, AssetPackState> = emptyMap()
    fun totalBytes(): Long = 0L
}

interface AssetPackStateUpdateListener
