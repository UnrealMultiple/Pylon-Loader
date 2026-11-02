// 层：内核层 / graphics —— Unity 相机钩子
#pragma once

namespace pylon::graphics {

/// 游戏线程。挂 Camera.FireOnPostRender，用来在世界相机画完之后发标记。
bool InstallCameraHook();
bool CameraHookInstalled();

}  // namespace pylon::graphics
