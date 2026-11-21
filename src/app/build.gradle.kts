// 层：启动器层（:app，外部 UI）。运行时随构建内置，预置逻辑在 pylon-staging.gradle.kts。
plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
}

apply(from = "pylon-staging.gradle.kts")

android {
    namespace = "dev.pylon.loader"
    compileSdk = libs.versions.compileSdk.get().toInt()
    buildToolsVersion = libs.versions.buildTools.get()

    defaultConfig {
        applicationId = "dev.pylon.loader"
        minSdk = libs.versions.minSdk.get().toInt()
        targetSdk = libs.versions.targetSdk.get().toInt()
        versionCode = 1
        versionName = "0.1.0-M1"
        ndk { abiFilters += "arm64-v8a" }
    }

    signingConfigs {
        create("debugLocal") {
            storeFile = System.getenv("PYLON_DEBUG_KEYSTORE")?.let { file(it) } ?: rootProject.file("debug.keystore")
            storePassword = System.getenv("PYLON_DEBUG_STORE_PASSWORD") ?: "android"
            keyAlias = System.getenv("PYLON_DEBUG_KEY_ALIAS") ?: "androiddebugkey"
            keyPassword = System.getenv("PYLON_DEBUG_KEY_PASSWORD") ?: "android"
        }
    }

    buildTypes {
        debug { signingConfig = signingConfigs.getByName("debugLocal") }
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("debugLocal")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { buildConfig = true }

    // 运行时随构建内置（发行形态没有"用户导入"）；CoreCLR/托管/代理生成件在 src/debug/assets（gitignore）。
    sourceSets {
        getByName("main") {
            assets.srcDirs(layout.buildDirectory.dir("pylon-staged/assets"))
            jniLibs.srcDirs(layout.buildDirectory.dir("pylon-staged/jniLibs"))
        }
        getByName("release") {
            assets.srcDirs("src/debug/assets")
            jniLibs.srcDirs("src/debug/jniLibs")
        }
    }

    packaging {
        jniLibs {
            // 必须解压到磁盘：Unity/ART 按 nativeLibraryDir 里的真实文件加载。
            useLegacyPackaging = true
        }
    }

    androidResources {
        // bin/Data 必须 STORED：引擎打开 APK zip 直接 mmap，Deflate 条目起不来；与 Unity 导出工程一致。
        noCompress += listOf(
            ".unity3d", ".ress", ".resource", ".obb", ".bundle", ".unityexp",
            ".png", ".json", ".dat",
            "unity default resources", "unity_builtin_extra",
        )
    }
}

dependencies {
    implementation(project(":common"))
    implementation(project(":game"))
}
