#pragma once

struct ANativeWindow;

namespace ImGuiSetup
{
    void EnsureContext(ANativeWindow* platformWindow);
    void TeardownContext();
}
