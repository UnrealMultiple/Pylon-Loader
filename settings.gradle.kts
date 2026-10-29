// Pylon Loader — 模块与仓库声明（层：构建工程）
pluginManagement {
    repositories {
        google {
            content {
                includeGroupByRegex("com\\.android.*")
                includeGroupByRegex("com\\.google.*")
                includeGroupByRegex("androidx.*")
            }
        }
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "PylonLoader"

// 分层与模块一一对应：
//   :app     启动器层（外部 UI）—— 只负责导入运行时、拉起 :game
//   :game    宿主层（:game 进程）—— 把游戏运行时加载进我们自己的进程
//   :common  公共契约层（纯 Kotlin）—— 启动器与宿主共享目录布局/清单/日志，避免两边各写一份路径
// 源码集中在 src/ 下：三个模块的目录显式指过去（Gradle 默认按模块名找根目录同名目录）。
include(":common")
include(":app")
include(":game")
project(":common").projectDir = file("src/common")
project(":app").projectDir = file("src/app")
project(":game").projectDir = file("src/game")
