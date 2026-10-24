#pragma once

#include <android/native_window.h>

class IGraphicsBackend
{
public:
    virtual ~IGraphicsBackend() = default;

    virtual bool Init(ANativeWindow* window, int width, int height) = 0;
    virtual void BeginFrame() = 0;
    virtual void EndFrame() = 0;
    virtual void Shutdown() = 0;
    virtual bool IsReady() const = 0;
};
