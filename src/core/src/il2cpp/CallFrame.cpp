// 层：内核层 / il2cpp —— 直调实现（保护边界复用 il2cpp/Guard）
#include "il2cpp/CallFrame.h"

#include <cstdio>
#include <cstring>
#include <string>

#include "il2cpp/Domain.h"
#include "il2cpp/Guard.h"

namespace pylon::il2cpp {

namespace {

thread_local std::string t_error;
thread_local char t_error_c[512] = {0};

void SetError(const std::string& text) {
    t_error = text;
    std::snprintf(t_error_c, sizeof(t_error_c), "%s", t_error.c_str());
}

#if defined(__aarch64__)
extern "C" void pylon_call_frame(void* fn, CallFrame* frame);
#endif

struct Pending {
    void* fn;
    CallFrame* frame;
};

// AAPCS64: x0-x7、d0-d7、8 个栈槽。
#define PYLON_CS_PARAMS                                                      \
    intptr_t, intptr_t, intptr_t, intptr_t, intptr_t, intptr_t, intptr_t,    \
    intptr_t, double, double, double, double, double, double, double, double, \
    intptr_t, intptr_t, intptr_t, intptr_t, intptr_t, intptr_t, intptr_t,    \
    intptr_t

struct S16 { uint64_t a, b; };
struct H1F { float a; };
struct H2F { float a, b; };
struct H3F { float a, b, c; };
struct H4F { float a, b, c, d; };
struct H1D { double a; };
struct H2D { double a, b; };
struct H3D { double a, b, c; };
struct H4D { double a, b, c, d; };
struct Big { uint8_t b[kMaxIndirectReturn]; };

/// 在保护边界内跑：只装寄存器、跳转、拷返回物，不能有析构。
void Run(void* p) {
    const Pending& c = *static_cast<const Pending*>(p);
#if defined(__aarch64__)
    pylon_call_frame(c.fn, c.frame);
#else
    CallFrame& f = *c.frame;
    const intptr_t* x = f.x;
    double d[8];
    std::memcpy(d, f.d, sizeof(d));

#define PYLON_CS_ARGS x[0], x[1], x[2], x[3], x[4], x[5], x[6], x[7], \
                      d[0], d[1], d[2], d[3], d[4], d[5], d[6], d[7], \
                      x[8], x[9], x[10], x[11], x[12], x[13], x[14], x[15]
#define PYLON_CS_CALL(T)                                                  \
    do {                                                                 \
        T r = reinterpret_cast<T (*)(PYLON_CS_PARAMS)>(c.fn)(PYLON_CS_ARGS); \
        std::memcpy(f.out, &r, sizeof(r));                                \
    } while (0)

    switch (static_cast<RetKind>(f.ret)) {
        case RetKind::Int: PYLON_CS_CALL(intptr_t); break;
        case RetKind::F32: PYLON_CS_CALL(float); break;
        case RetKind::F64: PYLON_CS_CALL(double); break;
        case RetKind::S16: PYLON_CS_CALL(S16); break;
        case RetKind::H1F: PYLON_CS_CALL(H1F); break;
        case RetKind::H2F: PYLON_CS_CALL(H2F); break;
        case RetKind::H3F: PYLON_CS_CALL(H3F); break;
        case RetKind::H4F: PYLON_CS_CALL(H4F); break;
        case RetKind::H1D: PYLON_CS_CALL(H1D); break;
        case RetKind::H2D: PYLON_CS_CALL(H2D); break;
        case RetKind::H3D: PYLON_CS_CALL(H3D); break;
        case RetKind::H4D: PYLON_CS_CALL(H4D); break;
        case RetKind::Indirect: {
            Big r = reinterpret_cast<Big (*)(PYLON_CS_PARAMS)>(c.fn)(PYLON_CS_ARGS);
            std::memcpy(f.indirect, &r, static_cast<size_t>(f.retSize));
            break;
        }
    }
#undef PYLON_CS_CALL
#undef PYLON_CS_ARGS
#endif
}

}  // namespace

}  // namespace pylon::il2cpp

extern "C" __attribute__((visibility("default"))) int32_t PylonMod_CallFrame(void* fn, void* framePtr) {
    using namespace pylon::il2cpp;
    t_error_c[0] = '\0';
    if (fn == nullptr) {
        SetError("target 为空");
        return 0;
    }
    if (framePtr == nullptr) {
        SetError("frame 为空");
        return 0;
    }
    CallFrame& frame = *static_cast<CallFrame*>(framePtr);
    frame.exc = nullptr;
    const auto kind = static_cast<RetKind>(frame.ret);
    if (frame.ret < 0 || frame.ret > static_cast<int32_t>(RetKind::Indirect)) {
        SetError("RetKind 非法");
        return 0;
    }
    if (kind == RetKind::Indirect &&
        (frame.retSize <= 0 || frame.retSize > kMaxIndirectReturn || frame.indirect == nullptr)) {
        SetError("间接返回参数非法");
        return 0;
    }

    if (!Domain::Attached()) {
        const auto attached = Domain::Attach();
        if (!attached) {
            SetError("未 attach 到 IL2CPP 域:" + attached.reason);
            return 0;
        }
    }
    if (!Domain::OnGameThread()) {
        SetError(Domain::RequireGameThread("直调游戏方法").reason);
        return 0;
    }
    // methodPointer 包装可能转换参数/返回 ABI,不能跳过包装直调内部 icall。
    Pending pending{fn, &frame};
    Caught caught;
    if (Guard::Run(Run, &pending, &caught)) {
        t_error_c[0] = '\0';
        return 1;
    }

    frame.exc = caught.exception;
    SetError(Describe(caught));
    return 0;
}

extern "C" __attribute__((visibility("default"))) const char* PylonMod_CallFrameError() {
    return pylon::il2cpp::t_error_c;
}
