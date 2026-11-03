// 层：内核层 / graphics —— GL 调用捕获
#pragma once

namespace pylon::graphics {

/// 挂 GL hooks（帧末上屏、世界层分界、状态镜像都靠它们）。幂等。
bool InstallGlHooks();

}  // namespace pylon::graphics
