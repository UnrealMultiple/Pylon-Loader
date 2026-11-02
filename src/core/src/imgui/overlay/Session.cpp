// 层：内核层 / imgui —— 帧会话实现
#include "imgui/overlay/Session.h"

#include <atomic>

#include "imgui.h"
#include "imgui/overlay/Present.h"
#include "imgui/overlay/Snapshot.h"

namespace pylon::imgui {

namespace {

std::atomic<ImGuiContext*> g_context{nullptr};
bool g_building = false;

}  // namespace

void Session::Attach(void* context) {
    g_context.store(static_cast<ImGuiContext*>(context));
}

ImGuiContext* Session::Context() {
    return g_context.load();
}

bool Session::Begin() {
    if (g_building) return false;
    ImGuiContext* context = ImGui::GetCurrentContext();
    if (context == nullptr) return false;
    g_context.store(context);
    if (!Present::Ready()) return false;
    g_building = true;
    return true;
}

void Session::End() {
    if (!g_building) return;
    g_building = false;
    const ImDrawData* draw = ImGui::GetDrawData();
    if (draw == nullptr || !draw->Valid) {
        Present::Submit(nullptr);
        return;
    }
    Present::Submit(Snapshot::Capture(*draw));
}

void Session::Reset() {
    g_building = false;
    Present::Submit(nullptr);
    Present::Teardown();
    g_context.store(nullptr);
}

}  // namespace pylon::imgui
