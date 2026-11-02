// 层：内核层 / il2cpp —— 符号绑定
#include "il2cpp/Symbols.h"

#include <dlfcn.h>

#include <cstring>

namespace pylon::il2cpp {

namespace {

#define PYLON_SYMBOL_BIND(need, name, type, params)                                           \
    do {                                                                                      \
        void* address = dlsym(handle, "il2cpp_" #name);                                        \
        if (address == nullptr) {                                                              \
            if (Need::need == Need::Required) report->missingRequired.push_back("il2cpp_" #name); \
            else report->missingOptional.push_back("il2cpp_" #name);                           \
        } else {                                                                               \
            std::memcpy(&out->name, &address, sizeof(address));                                \
            if (Need::need == Need::Required) ++report->required;                              \
            else ++report->optional;                                                           \
        }                                                                                      \
    } while (0);

}  // namespace

bool Symbols::Load(BindReport* report) {
    *report = BindReport{};

    void* handle = dlopen("libil2cpp.so", RTLD_NOLOAD | RTLD_NOW);
    if (handle == nullptr) {
        const char* error = dlerror();
        report->missingRequired.push_back(std::string("libil2cpp.so 尚未加载（dlopen RTLD_NOLOAD 失败：") +
                                          (error != nullptr ? error : "未知") + "）");
        return false;
    }

    Symbols* out = this;
    PYLON_IL2CPP_SYMBOLS(PYLON_SYMBOL_BIND);
#undef PYLON_SYMBOL_BIND

    return report->ok();
}

}  // namespace pylon::il2cpp
