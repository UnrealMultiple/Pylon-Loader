// 层：宿主层（:game）
//
// Android library，不是 application：宿主代码要跑在同一个 app 进程里，必须编进 :app 那一个 APK，
// 而 AGP 不允许 application 依赖 application。:game 只是代码归属，进程由 :app manifest 的
// android:process=":game" 决定。
//
// 职责：GameActivity（真 Activity + Context 覆写）、GameRuntimeLoader（按序加载导入目录的
// 原生库并建立游戏上下文）、兼容占位类。不写 hook、不写 Mod、不写 UI。
//
// 游戏资源（assets/bin/Data）不在这里：只有最终 APK 的 assets 会被 Unity 读到，
// 由 :app 的 debug 变体从本地 refs/ 预置，:game 不该知道 refs/ 在哪。
plugins {
    alias(libs.plugins.android.library)
    alias(libs.plugins.kotlin.android)
}

android {
    namespace = "dev.pylon.loader.game"
    compileSdk = libs.versions.compileSdk.get().toInt()
    buildToolsVersion = libs.versions.buildTools.get()

    defaultConfig {
        minSdk = libs.versions.minSdk.get().toInt()
        ndk {
            abiFilters += "arm64-v8a"
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    // 契约层：导入目录布局与清单格式，必须与启动器层完全一致。
    implementation(project(":common"))
}
