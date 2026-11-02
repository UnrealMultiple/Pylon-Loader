// 层：内核层 / imgui —— 跨线程传递的一帧
//
// ImGui 的绘制缓冲属于上下文，只在游戏线程上活着。两个线程之间只递这个不可变副本，
// 上屏线程因此不必和建帧抢锁，也不会读到半个帧。
#pragma once

#include <cstdint>
#include <memory>
#include <vector>

#include "imgui.h"

namespace pylon::imgui {

class Snapshot {
public:
    struct List {
        std::vector<ImDrawVert> vertices;
        std::vector<ImDrawIdx> indices;
        std::vector<ImDrawCmd> commands;
    };

    /// 游戏线程（上下文是当前的）。空帧也会返回对象：上屏侧要靠它区分"没有帧"和"空帧"。
    static std::shared_ptr<const Snapshot> Capture(const ImDrawData& draw);

    const std::vector<List>& Lists() const { return lists_; }

    /// 后端要同步的纹理对象（字体图集重建会换新对象，所以每帧拷一份指针）。
    const std::vector<ImTextureData*>& Textures() const { return textures_; }

    ImVec2 DisplayPos() const { return displayPos_; }
    ImVec2 DisplaySize() const { return displaySize_; }
    ImVec2 FramebufferScale() const { return framebufferScale_; }
    int Vertices() const { return vertices_; }
    int Commands() const { return commands_; }
    int64_t TakenAt() const { return takenAt_; }

private:
    std::vector<List> lists_;
    std::vector<ImTextureData*> textures_;
    ImVec2 displayPos_{0.f, 0.f};
    ImVec2 displaySize_{0.f, 0.f};
    ImVec2 framebufferScale_{1.f, 1.f};
    int vertices_ = 0;
    int commands_ = 0;
    int64_t takenAt_ = 0;
};

int64_t NowNs();

}  // namespace pylon::imgui
