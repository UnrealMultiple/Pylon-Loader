package com.games505.ageverification

/**
 * 兼容占位：来源 docs/01 清单（年龄验证；本机装的是亚马逊/505 版，大概走 Amazon 那条），
 * docs/03 排查表把它列为"启动即 ClassNotFoundException"的首要嫌疑。
 * start() 返回 false 表示没有启动验证流程——目的是不让宿主在启动阶段死掉，不是伪造一次验证通过。
 */

class AmazonAg {
    fun start(): Boolean = false
    fun isVerified(): Boolean = false
}

class GoogleAg {
    fun start(): Boolean = false
    fun isVerified(): Boolean = false
}

class SamsungAg {
    fun start(): Boolean = false
    fun isVerified(): Boolean = false
}
