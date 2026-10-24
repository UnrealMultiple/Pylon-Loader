#pragma once

#include <android/native_window.h>

namespace AndroidPlatform
{
    ANativeWindow* GetNativeWindow();
    void InvalidateNativeWindow();
}
