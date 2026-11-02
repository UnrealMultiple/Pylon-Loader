// 层：内核层 / il2cpp —— 字段实现
#include "il2cpp/Metadata.h"

#include <cstring>
#include <string>

#include "il2cpp/Domain.h"

namespace pylon::il2cpp {

namespace {

constexpr uint32_t kFieldStatic = 0x0010u;  // FIELD_ATTRIBUTE_STATIC

std::string TypeName(const Symbols& symbols, const Il2CppType* type) {
    if (type == nullptr || symbols.type_get_name == nullptr) return {};
    char* raw = symbols.type_get_name(type);  // malloc 出来的串
    if (raw == nullptr) return {};
    std::string name(raw);
    if (symbols.free != nullptr) symbols.free(raw);
    return name;
}

size_t PrimitiveSize(const std::string& name) {
    if (name == "System.Boolean" || name == "System.Byte" || name == "System.SByte") return 1;
    if (name == "System.Char" || name == "System.Int16" || name == "System.UInt16") return 2;
    if (name == "System.Int32" || name == "System.UInt32" || name == "System.Single") return 4;
    if (name == "System.Int64" || name == "System.UInt64" || name == "System.Double" ||
        name == "System.IntPtr" || name == "System.UIntPtr" || name == "System.String") {
        return sizeof(void*);
    }
    return 0;
}

}  // namespace

Outcome<Field> Field::Resolve(const Session& session, Il2CppClass* klass, std::string_view name) {
    if (klass == nullptr) return Outcome<Field>::Bad("类型为空");
    if (name.empty()) return Outcome<Field>::Bad("字段名为空");

    const Symbols& symbols = session.raw();
    const std::string key(name);
    for (Il2CppClass* current = klass; current != nullptr; current = symbols.class_get_parent(current)) {
        if (FieldInfo* info = symbols.class_get_field_from_name(current, key.c_str())) {
            return Outcome<Field>::Good(Field(&symbols, info));
        }
    }
    return Outcome<Field>::Bad("字段 " + key + " 在本类与父类链上都不存在");
}

bool Field::IsStatic() const {
    return (symbols_->field_get_flags(info_) & kFieldStatic) != 0;
}

size_t Field::Size() const {
    const Symbols& symbols = *symbols_;
    const Il2CppType* type = symbols.field_get_type != nullptr ? symbols.field_get_type(info_) : nullptr;
    if (type == nullptr) return 0;

    if (const size_t primitive = PrimitiveSize(TypeName(symbols, type)); primitive != 0) return primitive;

    if (symbols.class_from_il2cpp_type == nullptr || symbols.class_is_valuetype == nullptr ||
        symbols.class_value_size == nullptr) {
        return 0;
    }
    Il2CppClass* klass = symbols.class_from_il2cpp_type(type);
    if (klass == nullptr) return 0;
    if (!symbols.class_is_valuetype(klass)) return sizeof(void*);

    uint32_t size = 0;
    return symbols.class_value_size(klass, &size) == 0 ? size : 0;
}

Status Field::Read(const void* instance, void* out) const {
    const Symbols& symbols = *symbols_;
    if (IsStatic()) {
        if (symbols.field_static_get_value == nullptr) {
            return Status::Fail("静态字段读取需要 il2cpp_field_static_get_value，但它不可用");
        }
        symbols.field_static_get_value(info_, out);
        return Status::Ok();
    }
    if (instance == nullptr) return Status::Fail("字段是实例字段，但 instance 为空");
    if (Status ready = Domain::RequireGameThread("读实例字段"); !ready) return ready;
    if (symbols.field_get_value == nullptr) return Status::Fail("il2cpp_field_get_value 不可用");
    symbols.field_get_value(const_cast<Il2CppObject*>(static_cast<const Il2CppObject*>(instance)), info_, out);
    return Status::Ok();
}

Status Field::Write(void* instance, const void* in) const {
    const Symbols& symbols = *symbols_;
    if (IsStatic()) {
        if (symbols.field_static_set_value == nullptr) {
            return Status::Fail("静态字段写入需要 il2cpp_field_static_set_value，但它不可用");
        }
        symbols.field_static_set_value(info_, const_cast<void*>(in));
        return Status::Ok();
    }
    if (instance == nullptr) return Status::Fail("字段是实例字段，但 instance 为空");
    if (Status ready = Domain::RequireGameThread("写实例字段"); !ready) return ready;
    if (symbols.field_set_value == nullptr) return Status::Fail("il2cpp_field_set_value 不可用");
    symbols.field_set_value(static_cast<Il2CppObject*>(instance), info_, const_cast<void*>(in));
    return Status::Ok();
}

}  // namespace pylon::il2cpp
