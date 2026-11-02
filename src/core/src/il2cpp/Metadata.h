// 层：内核层 / il2cpp —— 字段：宽度、静态性与读写
#pragma once

#include <cstddef>
#include <string>
#include <string_view>

#include "il2cpp/Symbols.h"
#include "pylon/Status.h"

namespace pylon::il2cpp {

class Session;

class Field {
public:
    Field() = default;

    static Outcome<Field> Resolve(const Session& session, Il2CppClass* klass, std::string_view name);

    FieldInfo* info() const { return info_; }
    bool valid() const { return info_ != nullptr; }

    /// 0 = 宽度无法确定，调用方必须拒绝读写。
    size_t Size() const;

    bool IsStatic() const;

    Status Read(const void* instance, void* out) const;
    Status Write(void* instance, const void* in) const;

private:
    Field(const Symbols* symbols, FieldInfo* info) : symbols_(symbols), info_(info) {}

    const Symbols* symbols_ = nullptr;
    FieldInfo* info_ = nullptr;
};

}  // namespace pylon::il2cpp
