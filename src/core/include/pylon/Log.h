// 层：内核层 —— 统一日志
// tag 与 Kotlin 侧一致（:common 的 RuntimeLayout.LOG_TAG），一条 logcat 过滤命令即可
// 同时看到启动器、宿主与内核三个阶段，排错时不用来回换 tag。
#pragma once

#include <android/log.h>

#include <string>

#define PYLON_TAG "PylonLoader"

// stage 作为第一个参数插进格式串，日志形如：[core] 绑定 15/15
#define PYLON_LOG(level, stage, fmt, ...)                                     \
    __android_log_print(level, PYLON_TAG, "[%s] " fmt, stage, ##__VA_ARGS__)

// stage 用来区分模块：[core] / [boot] / [refs] / [il2cpp] / [fx]
#define LOGD(stage, fmt, ...) PYLON_LOG(ANDROID_LOG_DEBUG, stage, fmt, ##__VA_ARGS__)
#define LOGI(stage, fmt, ...) PYLON_LOG(ANDROID_LOG_INFO, stage, fmt, ##__VA_ARGS__)
#define LOGW(stage, fmt, ...) PYLON_LOG(ANDROID_LOG_WARN, stage, fmt, ##__VA_ARGS__)
#define LOGE(stage, fmt, ...) PYLON_LOG(ANDROID_LOG_ERROR, stage, fmt, ##__VA_ARGS__)

namespace pylon {

/// 给 std::string 场景用（宏只能接 printf 格式参数）。
inline void LogInfo(const char* stage, const std::string& message) {
    LOGI(stage, "%s", message.c_str());
}

inline void LogError(const char* stage, const std::string& message) {
    LOGE(stage, "%s", message.c_str());
}

}  // namespace pylon
