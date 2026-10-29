// 层：公共契约层（:common）
//
// 纯 Kotlin/JVM 模块，刻意不是 Android 库：只放启动器层与宿主层共享的数据契约
// （运行时目录布局、导入清单格式、哈希工具），不碰 Android API。
// 这样契约可以用普通 JVM 测试覆盖，也不会把某一层的 Android 依赖泄漏给另一层。
plugins {
    alias(libs.plugins.kotlin.jvm)
}

kotlin {
    jvmToolchain(17)
}
