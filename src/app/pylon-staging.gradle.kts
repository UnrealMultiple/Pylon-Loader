// 内置游戏文件预置：il2cpp.so 要打补丁，不能从用户安装的游戏现解。
//   · refs/lib/arm64-v8a/*.so → jniLibs（安装时解到 nativeLibraryDir，按序加载）
//   · refs/assets/bin/Data    → assets/bin/Data
//   · refs/classes.dex        → assets/pylon-game/classes.dex
// 缺关键文件直接构建失败：按 refs/README.md 从自己安装的正版重新解出。
val refsDir = rootProject.layout.projectDirectory.dir("refs")
val refsDataDir = refsDir.dir("assets/bin/Data")
val refsLibDir = refsDir.dir("lib/arm64-v8a")
val requiredLibs = listOf("libc++_shared.so", "libmain.so", "libunity.so", "libGNS_Wrapper.so", "libil2cpp.so")
val stagedAssetsDir = layout.buildDirectory.dir("pylon-staged/assets").get().asFile
val stagedJniLibsDir = layout.buildDirectory.dir("pylon-staged/jniLibs").get().asFile

val stageGameFilesAssets by tasks.registering(Copy::class) {
    group = "pylon"
    description = "把 refs 的 bin/Data 与 classes.dex 预置进 APK assets"
    from(refsDataDir) { into("bin/Data") }
    from(refsDir.file("classes.dex")) { into("pylon-game") }
    into(stagedAssetsDir)
    doFirst {
        val metadata = refsDataDir.file("Managed/Metadata/global-metadata.dat").asFile
        require(metadata.isFile) { "refs/assets/bin/Data 缺 global-metadata.dat：按 refs/README.md 重新解出" }
        require(refsDir.file("classes.dex").asFile.isFile) { "refs/classes.dex 不存在：按 refs/README.md 解出" }
    }
}

val stageGameFilesJniLibs by tasks.registering(Copy::class) {
    group = "pylon"
    description = "把 refs/lib/arm64-v8a 的原生库预置进 APK jniLibs"
    from(refsLibDir) { include("*.so") }
    into(File(stagedJniLibsDir, "arm64-v8a"))
    doFirst {
        val missing = requiredLibs.filterNot { refsLibDir.file(it).asFile.isFile }
        require(missing.isEmpty()) { "refs/lib/arm64-v8a 缺少游戏原生库：${missing.joinToString()}" }
    }
}

tasks.matching { it.name == "mergeDebugAssets" || it.name == "mergeReleaseAssets" }.configureEach {
    dependsOn(stageGameFilesAssets)
}

tasks.matching { it.name == "mergeDebugJniLibFolders" || it.name == "mergeReleaseJniLibFolders" }.configureEach {
    dependsOn(stageGameFilesJniLibs)
}
