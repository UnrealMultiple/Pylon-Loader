// Pylon Loader — 根构建脚本（层：构建工程）
// 只声明插件版本，不在这里给任何模块加依赖；模块各自的 build.gradle.kts 负责自己那一层。
plugins {
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.android.library) apply false
    alias(libs.plugins.kotlin.android) apply false
    alias(libs.plugins.kotlin.jvm) apply false
}

// 构建前提自检：把 SDK/JDK/Gradle 家目录打出来，出问题时先看这一步，不靠猜。
tasks.register("pylonPreflight") {
    group = "pylon"
    description = "打印本机构建前提（SDK/JDK/Gradle 家目录），不修改任何东西"
    doLast {
        logger.lifecycle("[pylon] gradleUserHome = ${gradle.gradleUserHomeDir}")
        logger.lifecycle("[pylon] javaHome       = ${System.getProperty("java.home")}")
        logger.lifecycle("[pylon] sdk.dir        = ${project.findProperty("sdk.dir") ?: System.getenv("ANDROID_HOME")}")
    }
}
