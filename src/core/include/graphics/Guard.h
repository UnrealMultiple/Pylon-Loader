// 层：内核层 / graphics —— "这批 GL 调用是我自己发的"
//
// 我们的绘制也走 libGLESv2 的同一批入口（因此命中同一批 hook）。镜像只该记 Unity 的调用，
// 所以在自己画的时候把标记打开，hook 里就只转发、不记账。
#pragma once

namespace pylon::graphics {

extern thread_local bool g_ownDraw;

class OwnDraw {
public:
    OwnDraw() { g_ownDraw = true; }
    ~OwnDraw() { g_ownDraw = false; }
    OwnDraw(const OwnDraw&) = delete;
    OwnDraw& operator=(const OwnDraw&) = delete;
};

}  // namespace pylon::graphics
