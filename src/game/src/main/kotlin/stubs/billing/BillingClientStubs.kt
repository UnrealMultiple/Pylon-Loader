package com.android.billingclient.api

/**
 * 兼容占位：来源 docs/01 清单（com.android.billingclient.api.*）。
 * 游戏走到商店/内购路径时会引用这些类，缺了就 ClassNotFoundException。
 * 只补能让类解析与调用通过的签名，不实现业务：宿主形态下不存在合法计费会话，官方 AAR 也会
 * 把 Google 的实现与 manifest 组件带进构建（docs/05 纪律）。类必须落在精确包名上，所以用顶层类而非嵌套类。
 */

class BillingClient private constructor() {

    companion object {
        const val VERSION: String = "8.0.0"
    }

    class Builder {
        fun setListener(listener: Any?): Builder = this
        fun enablePendingPurchases(): Builder = this
        fun enableAutoServiceReconnection(): Builder = this
        fun build(): BillingClient = BillingClient()
    }
}

class AcknowledgePurchaseParams private constructor() {
    class Builder {
        fun setPurchaseToken(purchaseToken: String?): Builder = this
        fun build(): AcknowledgePurchaseParams = AcknowledgePurchaseParams()
    }
}

interface AcknowledgePurchaseResponseListener

class ProductDetails private constructor() {
    fun getProductId(): String = ""
    fun getOneTimePurchaseOfferDetails(): Any? = null
    fun getSubscriptionOfferDetails(): List<Any> = emptyList()
}

interface ProductDetailsResponseListener

class ProductDetailsParams private constructor() {
    class Builder {
        fun setProductDetails(productDetails: ProductDetails?): Builder = this
        fun build(): ProductDetailsParams = ProductDetailsParams()
    }
}

class Purchase private constructor() {
    fun getPurchaseToken(): String = ""
    fun getProducts(): List<String> = emptyList()
    fun getPurchaseState(): Int = 0

    companion object {
        const val PURCHASE_STATE_PURCHASED: Int = 1
        const val PURCHASE_STATE_PENDING: Int = 2
    }
}

interface PurchasesResponseListener
interface PurchasesUpdatedListener

class BillingResult private constructor() {
    fun getResponseCode(): Int = RESPONSE_SERVICE_UNAVAILABLE

    companion object {
        const val RESPONSE_OK: Int = 0
        const val RESPONSE_SERVICE_UNAVAILABLE: Int = 2
    }
}

class ProductType private constructor() {
    companion object {
        const val INAPP: String = "inapp"
        const val SUBS: String = "subs"
    }
}
