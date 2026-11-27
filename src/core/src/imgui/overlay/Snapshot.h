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

    /// 纹理的捕获态：像素与元数据在游戏线程捕获时拷齐，渲染线程只消费副本，
    /// 全程不碰 ImTextureData 活对象（活对象归 ImGui 上下文所在线程独占）。
    ///
    /// 只拷“本次要改的那块”：字形图集动辄 4096² RGBA（64MB），整图每帧重拷+重传
    /// 会让 swap 线程直接卡死。创建时拷整图，后续更新只拷 UpdateRect 子矩形，
    /// 渲染线程用 glTexSubImage2D 局部写入。
    struct TextureSnap {
        ImTextureData* pointer = nullptr;
        unsigned int uniqueId = 0;
        int width = 0, height = 0, bytesPerPixel = 4;
        ImTextureStatus status = ImTextureStatus_OK;
        bool full = false;   // true = pixels 是整图（WantCreate）
        int x = 0, y = 0;    // full=false 时，pixels 对应的子矩形原点
        int regionWidth = 0, regionHeight = 0;
        std::vector<unsigned char> pixels;
    };

    static std::shared_ptr<const Snapshot> Capture(const ImDrawData& draw);

    const std::vector<List>& Lists() const { return lists_; }

    const std::vector<TextureSnap>& Textures() const { return textures_; }

    ImVec2 DisplayPos() const { return displayPos_; }
    ImVec2 DisplaySize() const { return displaySize_; }
    ImVec2 FramebufferScale() const { return framebufferScale_; }
    int Vertices() const { return vertices_; }
    int Commands() const { return commands_; }
    int64_t TakenAt() const { return takenAt_; }

private:
    std::vector<List> lists_;
    std::vector<TextureSnap> textures_;
    ImVec2 displayPos_{0.f, 0.f};
    ImVec2 displaySize_{0.f, 0.f};
    ImVec2 framebufferScale_{1.f, 1.f};
    int vertices_ = 0;
    int commands_ = 0;
    int64_t takenAt_ = 0;
};

int64_t NowNs();

}
