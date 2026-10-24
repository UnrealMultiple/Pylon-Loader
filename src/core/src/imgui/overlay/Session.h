// 层：内核层 / imgui —— 游戏线程的帧会话
//
// 建帧这一侧只有三件事：挂上上下文、开一帧、把结果交出去。没有锁、没有超时。
#pragma once

struct ImGuiContext;

namespace pylon::imgui {

class Session {
public:
    /// 托管侧 CreateContext 之后。
    static void Attach(void* context);

    /// true = 可以 NewFrame，必须配对 End。
    static bool Begin();
    static void End();

    /// DestroyContext 之前：丢帧、拆后端、松上下文。
    static void Reset();

    /// 任意线程：上屏侧要挂同一个上下文。
    static ImGuiContext* Context();
};

}  // namespace pylon::imgui
