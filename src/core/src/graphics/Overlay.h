// 层：内核层 / graphics —— 覆盖层契约
//
// graphics 不认识任何具体 UI：谁想画在最上层，就把回调注册进来，帧末在默认 framebuffer 上被叫一次。
#pragma once

namespace pylon::graphics {

/// width / height 是表面像素尺寸。
using OverlayFn = void (*)(int width, int height);

}  // namespace pylon::graphics
