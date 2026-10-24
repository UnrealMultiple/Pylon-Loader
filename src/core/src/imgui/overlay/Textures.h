// 层：内核层 / imgui —— 纹理闸门
//
// 托管侧在游戏线程上建房，那里没有 GL 上下文；纹理只能在渲染线程建。于是分成两半：
// 渲染线程预生成 GL 名字、上传像素、删纹理；游戏线程只领名字、排像素。
// 名字本身就是 ImTextureID（imgui_impl_opengl3 的约定），后端不需要翻译。
#pragma once

#include <cstdint>

namespace pylon::imgui {

class Textures {
public:
    /// 渲染线程至少跑过一次 Apply（名字池已生成）。
    static bool Ready();

    /// 游戏线程：领一个名字并把像素排队。失败返回 0（托管侧当"没有纹理"）。
    static uint64_t Create(const uint8_t* rgba, int width, int height);
    static void Destroy(uint64_t id);

    /// 渲染线程：补名字池、删排队中的、上传排队中的。
    static void Apply();
};

}  // namespace pylon::imgui
