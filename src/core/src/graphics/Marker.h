// 层：内核层 / graphics —— 世界层分界标记
//
// 一张 17x3 的 RenderTexture：世界相机结束时对它做一次 Blit，这次 Blit 在渲染线程的
// GL 队列里就是一次 glViewport(0,0,17,3)，世界层效果就画在那里。
#pragma once

namespace pylon::graphics {

/// 标记尺寸：捕获侧靠这两个数认出"这次 viewport 是标记"。
constexpr int kMarkerWidth = 17;
constexpr int kMarkerHeight = 3;

/// 游戏线程、相机绘制之外调（一次）。
bool CreateMarker();
bool MarkerReady();

/// 游戏线程，在世界相机的 FireOnPostRender 里调。
void IssueMarker();

}  // namespace pylon::graphics
