// 层：启动器层（:app，外部 UI）
//
// 两件事：从用户已安装正版导入运行时（RuntimeImport）、把宿主进程拉起来（GameActivity）；
// 外面套一层单页状态界面（ui/），负责导入进度、校验详情与排障入口。
// 它也是唯一的 application 模块，所以 manifest 属性与组件声明都在这里（见 src/main/AndroidManifest.xml）。
plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
}

android {
    namespace = "dev.pylon.loader"
    compileSdk = libs.versions.compileSdk.get().toInt()
    buildToolsVersion = libs.versions.buildTools.get()

    // 内核（core/）**不用 AGP 的 externalNativeBuild**：
    // 本机沙箱里 cmake 的编译器 ABI 探测要用管道捕获 ninja 的输出，而沙箱禁止子进程
    // 走管道，结果是 configureCMakeDebug 永久阻塞（cmake/ninja 双双 0% CPU）。
    // 内核改由 core/build.ps1 直接用 NDK 的 clang++ 编译，产物落在
    // app/src/main/jniLibs/arm64-v8a/libpylon.so，Gradle 只负责把它打进 APK。
    // 细节见 core/README.md。

    defaultConfig {
        applicationId = "dev.pylon.loader"
        minSdk = libs.versions.minSdk.get().toInt()
        targetSdk = libs.versions.targetSdk.get().toInt()
        versionCode = 1
        versionName = "0.1.0-M1"

        // 只保留 arm64-v8a。字面量而非引用 :common 常量：Gradle 脚本 classpath 里没有模块源码，
        // 不一致会在导入校验时立刻暴露。
        ndk {
            abiFilters += "arm64-v8a"
        }
    }

    signingConfigs {
        // 本机沙箱不允许写 ~/.android，签名材料放在工作区；CI 可用环境变量指定正式密钥。
        create("debugLocal") {
            val fromEnv = System.getenv("PYLON_DEBUG_KEYSTORE")
            storeFile = if (fromEnv != null) file(fromEnv) else rootProject.file("debug.keystore")
            storePassword = System.getenv("PYLON_DEBUG_STORE_PASSWORD") ?: "android"
            keyAlias = System.getenv("PYLON_DEBUG_KEY_ALIAS") ?: "androiddebugkey"
            keyPassword = System.getenv("PYLON_DEBUG_KEY_PASSWORD") ?: "android"
        }
    }

    buildTypes {
        debug {
            signingConfig = signingConfigs.getByName("debugLocal")
        }
        release {
            // M1 不打 release 包；发行形态是 M5 的事（docs/02 形态 B）。
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("debugLocal")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        buildConfig = true
    }

    // 游戏资源与原生库只进 debug 变体，由下面的 stageRefs* 任务从本地 refs/ 预置。
    // release 走"用户导入"，构建图层面就不可能含游戏二进制。
    sourceSets {
        getByName("debug") {
            assets.srcDirs("src/debug/assets")
            jniLibs.srcDirs("src/debug/jniLibs")
        }
    }

    packaging {
        jniLibs {
            // 必须解压到磁盘：Unity/ART 按 nativeLibraryDir 里的真实文件加载，
            // 留在 APK 内（useLegacyPackaging=false）会找不到。
            useLegacyPackaging = true
        }
    }

    androidResources {
        // 关键：assets/bin/Data 下的资源必须**不压缩**（STORED）。
        //
        // Unity 2021.3 生成 Android 工程时自己就在导出的 Gradle 里写了这份
        // noCompress 列表。理由是引擎定位 bin/Data 的方式：它打开 base APK 的 zip
        // 直接 mmap，而不是走 AssetManager。Deflate 条目没法 mmap，引擎在打出
        // 第一条日志之前就失败——实测现象是 logcat 里连一条 I/Unity 都没有
        // （对比：游戏自己启动时第一条是 "MemoryManager: Using 'Dynamic Heap' Allocator."）。
        //
        // 本机实测过：修这份列表之前，我们 APK 里 resources.resource 是 Defl:N（压缩率
        // 0%，纯属白压 81MB），data.unity3d 是 Defl:N 80MB→64MB。
        // 列表与 Unity 导出的工程逐项一致，不要凭感觉加减。
        noCompress += listOf(
            ".unity3d",
            ".ress",
            ".resource",
            ".obb",
            ".bundle",
            ".unityexp",
            ".png",
            // 游戏包里这几个 JSON 也是 Stored（本机 adb unzip -v 逐项核对过），跟着对齐。
            ".json",
        )
    }
}

/**
 * 可选：把本地 refs/ 预置进 :app 的 debug 变体，仅用于不经过导入流程的本地调试。
 *
 * **默认关闭**（-PpylonStageRefs=true 开启）。发行/验收形态必须让启动器从设备上已安装的
 * 游戏导入：APK 里不应包含游戏二进制。默认关闭时，宿主通过
 *   · NativeLibraryPath —— 把导入目录追加进 classloader 的 native 搜索路径；
 *   · GameAssets —— 把所选游戏 APK 的 assets 挂到 AssetManager；
 * 从导入目录 / 已安装 APK 取游戏文件。
 *
 * 只在本地做“没导入也能启动”的快速排查时才打开该属性。refs/ 不存在时静默跳过。
 */
val stageGameRefs = (findProperty("pylonStageRefs") as String?)?.toBoolean() ?: false
val refsDataDir = rootProject.layout.projectDirectory.dir("refs/assets/bin/Data").asFile
val refsLibDir = rootProject.layout.projectDirectory.dir("refs/lib/arm64-v8a").asFile
val debugAssetsDataDir = layout.projectDirectory.dir("src/debug/assets/bin/Data").asFile
val debugJniLibDir = layout.projectDirectory.dir("src/debug/jniLibs/arm64-v8a").asFile

val stageRefsAssets by tasks.registering(Copy::class) {
    group = "pylon"
    description = "把 refs/assets/bin/Data 预置进 debug assets（需 -PpylonStageRefs=true；仅本地调试）"
    onlyIf {
        if (!stageGameRefs) {
            logger.lifecycle("[pylon] 不预置游戏资源（默认）；需要旧行为用 -PpylonStageRefs=true。")
            false
        } else {
            val ok = refsDataDir.isDirectory
            if (!ok) logger.lifecycle("[pylon] 跳过资源预置：$refsDataDir 不存在，按 refs/README.md 重新解出即可。")
            ok
        }
    }
    from(refsDataDir)
    into(debugAssetsDataDir)
}

val stageRefsJniLibs by tasks.registering(Copy::class) {
    group = "pylon"
    description = "把 refs/lib/arm64-v8a 的原生库预置进 debug jniLibs（需 -PpylonStageRefs=true；仅本地调试）"
    onlyIf {
        if (!stageGameRefs) {
            logger.lifecycle("[pylon] 不预置游戏原生库（默认）；需要旧行为用 -PpylonStageRefs=true。")
            false
        } else {
            val ok = refsLibDir.isDirectory
            if (!ok) logger.lifecycle("[pylon] 跳过原生库预置：$refsLibDir 不存在，按 refs/README.md 重新解出即可。")
            ok
        }
    }
    from(refsLibDir) {
        include("*.so")
    }
    into(debugJniLibDir)
}

tasks.matching { it.name == "mergeDebugAssets" }.configureEach {
    dependsOn(stageRefsAssets)
    doFirst {
        if (!stageGameRefs && File(debugAssetsDataDir, "data.unity3d").isFile) {
            throw GradleException(
                "src/debug/assets/bin/Data 还有旧预置；默认不应把游戏资源打进 APK。" +
                    "删掉它，或用 -PpylonStageRefs=true 明确开启本地回退。"
            )
        }
    }
}

tasks.matching { it.name == "mergeDebugJniLibFolders" }.configureEach {
    dependsOn(stageRefsJniLibs)
    doFirst {
        val stale = File(debugJniLibDir, "libil2cpp.so")
        if (!stageGameRefs && stale.isFile) {
            throw GradleException(
                "src/debug/jniLibs/arm64-v8a 还有旧预置的游戏 .so；默认不应打进 APK。" +
                    "删掉它们，或用 -PpylonStageRefs=true 明确开启本地回退。"
            )
        }
    }
}

// === Harmony 的 IL2CPP 支持层：本项目自己编译，编 APK 之前先编它 ===
//
// 这份 DLL（Il2CppInterop.HarmonySupport）不能直接用 BepInEx 原包里的：它有两个补丁
// （IDetourProvider 3 参签名、值类型参数包装），缺了它被 Harmony 挂钩的
// GUIPanel.Draw(..., Nullable<Color>×3) 一调用就 AccessViolationException 把进程 abort。
// 源码与原因见 interop/harmonysupport/Il2CppInterop.HarmonySupport.csproj 的注释。
val harmonySupportDir = rootProject.layout.projectDirectory.dir("interop/harmonysupport")
val harmonySupportDll = harmonySupportDir.file("bin/Release/net10.0/Il2CppInterop.HarmonySupport.dll")
val harmonySupportAssetDir = layout.projectDirectory.dir("src/debug/assets/pylon-interop")

val buildHarmonySupport by tasks.registering(Exec::class) {
    group = "pylon"
    description = "编译打过补丁的 Il2CppInterop.HarmonySupport（Harmony ↔ IL2CPP 桥）"
    workingDir = harmonySupportDir.asFile
    commandLine("dotnet", "build", "Il2CppInterop.HarmonySupport.csproj", "-c", "Release", "--nologo", "-v", "q")
    // MSBuild 启动时要建临时目录：沙箱下 TEMP 指向会话目录会直接 MSB1025（UnauthorizedAccessException）。
    // 收到工作区里，和把 Gradle 缓存收进 .gradle-home 是同一个思路。
    val msbuildTemp = rootProject.layout.projectDirectory.dir(".gradle-home/msbuild-temp").asFile
    val harmonySupportRefs = listOf(
        "0Harmony.dll", "Il2CppInterop.Runtime.dll", "Il2CppInterop.Common.dll",
        "Microsoft.Extensions.Logging.Abstractions.dll",
        "MonoMod.Core.dll", "MonoMod.Utils.dll", "MonoMod.Backports.dll",
        "MonoMod.RuntimeDetour.dll", "MonoMod.ILHelpers.dll", "Mono.Cecil.dll",
    )
    doFirst {
        msbuildTemp.mkdirs()
        // 这些引用来自 BepInEx-Android 运行时（见 docs/构建-从零开始.md）。
        // 缺件时给可读原因，不要让人对着 MSBuild 的 CS0006 猜。
        val missing = harmonySupportRefs.filterNot { harmonySupportAssetDir.file(it).asFile.isFile }
        if (missing.isNotEmpty()) {
            throw GradleException(
                "Harmony 支持层缺引用：${missing.joinToString()}（应位于 app/src/debug/assets/pylon-interop/）。" +
                    "先跑 tools/deploy-runtime.ps1，或按 docs/构建-从零开始.md 准备 runtime/。"
            )
        }
    }
    environment("TEMP", msbuildTemp.absolutePath)
    environment("TMP", msbuildTemp.absolutePath)
    inputs.file(harmonySupportDir.file("Il2CppInterop.HarmonySupport.csproj").asFile)
    inputs.files(fileTree(harmonySupportDir.asFile) { include("*.cs") })
    outputs.file(harmonySupportDll)
}

val stageHarmonySupport by tasks.registering(Copy::class) {
    group = "pylon"
    description = "把自编的 HarmonySupport 拷进 debug assets（随后被打进 APK）"
    dependsOn(buildHarmonySupport)
    from(harmonySupportDll)
    into(harmonySupportAssetDir)
}

tasks.matching { it.name == "mergeDebugAssets" }.configureEach {
    dependsOn(stageHarmonySupport)
}

dependencies {
    // 契约层：导入目录布局与清单格式，必须与宿主层完全一致。
    implementation(project(":common"))

    // 宿主层：GameActivity 与上下文覆写、运行时装载器、兼容占位。
    // 靠 module 依赖把它编进同一个 APK——两个模块共用同一 applicationId，
    // 同包名不可能装两份，而"加载进我们自己的进程"要求只有一个 app。
    implementation(project(":game"))
}
