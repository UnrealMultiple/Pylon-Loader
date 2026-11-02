// 层：内核层 / boot —— 引用校验实现
#include "boot/RefValidator.h"

#include <cstdarg>
#include <cstdio>

#include "il2cpp/Domain.h"
#include "il2cpp/Invoke.h"
#include "pylon/Log.h"

namespace pylon::boot {
namespace {

std::string Trim(const std::string& in) {
    size_t begin = in.find_first_not_of(" \t\r\n");
    if (begin == std::string::npos) return {};
    size_t end = in.find_last_not_of(" \t\r\n");
    return in.substr(begin, end - begin + 1);
}

/// `A.B.C` → ("A.B", "C")。按**最后一个点**切：命名空间里带点的情况也有，
/// 而类名本身不会带点（嵌套类型 IL2CPP 用 `Outer/Inner`）。
void SplitNamespace(const std::string& qualified, std::string* namespaze, std::string* name) {
    size_t dot = qualified.rfind('.');
    if (dot == std::string::npos) {
        *namespaze = "";
        *name = qualified;
    } else {
        *namespaze = qualified.substr(0, dot);
        *name = qualified.substr(dot + 1);
    }
}

/// `<ns>.<Class>::<member>` → 类部分与成员部分。
bool SplitMember(const std::string& rest, std::string* type_part, std::string* member_part) {
    size_t sep = rest.rfind("::");
    if (sep == std::string::npos || sep == 0 || sep + 2 >= rest.size()) return false;
    *type_part = rest.substr(0, sep);
    *member_part = rest.substr(sep + 2);
    return true;
}

std::string Format(const char* fmt, ...) {
    char buffer[512];
    va_list args;
    va_start(args, fmt);
    std::vsnprintf(buffer, sizeof(buffer), fmt, args);
    va_end(args);
    return buffer;
}

/// 手写整数解析：不用 std::stoi，避免为一个参数个数引入异常路径（内核里不抛异常）。
bool ParseNonNegativeInt(const std::string& text, int* out) {
    if (text.empty()) return false;
    int value = 0;
    for (char c : text) {
        if (c < '0' || c > '9') return false;
        value = value * 10 + (c - '0');
        if (value > 4096) return false;  // 参数个数不可能这么大，当输入错误处理
    }
    *out = value;
    return true;
}

}  // namespace

size_t RefReport::OkCount() const {
    size_t n = 0;
    for (const auto& item : items) {
        if (item.ok) ++n;
    }
    return n;
}

std::string RefReport::FailureReason() const {
    std::string reason = Format("引用校验失败 %zu 条（共 %zu 条）：", FailCount(), items.size());
    for (const auto& item : items) {
        if (!item.ok) reason += "\n  ✗ " + item.spec + " —— " + item.detail;
    }
    return reason;
}

std::string RefReport::Describe() const {
    std::string text;
    for (const auto& item : items) {
        text += (item.ok ? "✓ " : "✗ ") + item.spec + " —— " + item.detail + "\n";
    }
    return text;
}

void RefValidator::ValidateCall(const il2cpp::Session& session, RefResult& result, RefReport& report,
                                const std::string& type_part, const std::string& method_name, int argc) {
    // 探测线程上记为"跳过"而不是失败：一条 call: 失败会把所有 Mod 停用。
    if (!il2cpp::Domain::OnGameThread()) {
        result.detail = "跳过：call: 只能在游戏线程(UnityMain)上执行（探测阶段不满足）";
        report.items.push_back(std::move(result));
        return;
    }

    // 只支持静态方法（清单里没有 this 可给）。参数个数照 method: 的规则解析（-1 = 探测）。
    auto resolved = il2cpp::Method::Resolve(session, type_part, method_name, argc);
    if (!resolved) {
        result.detail = resolved.reason;
        report.items.push_back(std::move(result));
        return;
    }
    if (!resolved.value.IsStatic()) {
        result.detail = "call: 只支持静态方法（清单里没有 this 可给）";
        report.items.push_back(std::move(result));
        return;
    }

    il2cpp::CallArgs args;
    const auto called = resolved.value.Invoke(session, nullptr, args);
    if (!called.invoked) {
        result.detail = called.error;
    } else if (called.threw) {
        result.detail = "调用抛托管异常：" + called.exception;  // 与"解析不到"同等对待
    } else {
        result.ok = true;
        result.detail = resolved.value.Describe() + " → " + called.Describe();
    }
    report.items.push_back(std::move(result));
}

RefReport RefValidator::Validate(const std::vector<std::string>& specs) {
    RefReport report;

    // 探测线程走到这里时已经 attach 过；再取一次票据是幂等的。
    auto session = il2cpp::Domain::Attach();
    if (!session) {
        RefResult failed;
        failed.spec = "(attach)";
        failed.detail = session.reason;
        report.items.push_back(std::move(failed));
        return report;
    }

    for (const auto& raw : specs) {
        const std::string spec = Trim(raw);
        if (spec.empty() || spec[0] == '#') continue;

        RefResult result;
        result.spec = spec;

        const size_t colon = spec.find(':');
        if (colon == std::string::npos) {
            result.detail = "语法错误：缺少 '类型:' 前缀（class/field/method）";
            report.items.push_back(std::move(result));
            continue;
        }
        const std::string kind = spec.substr(0, colon);
        const std::string rest = Trim(spec.substr(colon + 1));

        std::string type_part;
        std::string member_part;
        bool needs_member = (kind == "field" || kind == "method");
        if (needs_member && !SplitMember(rest, &type_part, &member_part)) {
            result.detail = "语法错误：期望 <命名空间>.<类>::<成员>，实际是 '" + rest + "'";
            report.items.push_back(std::move(result));
            continue;
        }
        if (!needs_member) type_part = rest;

        std::string namespaze;
        std::string class_name;
        SplitNamespace(type_part, &namespaze, &class_name);

        auto klass = session.value.FindClass(namespaze, class_name);
        if (!klass) {
            result.detail = klass.reason;
            report.items.push_back(std::move(result));
            continue;
        }

        if (kind == "class") {
            result.ok = true;
            result.detail = "解析为 " + session.value.ClassName(klass.value);
        } else if (kind == "field") {
            auto field = il2cpp::Field::Resolve(session.value, klass.value, member_part);
            if (!field) {
                result.detail = field.reason + "（类 " + session.value.ClassName(klass.value) + "）";
            } else {
                result.ok = true;
                // 标出 static/instance：静态字段的 offset 是**静态块内**偏移（实测 dayTime=1152），
                // 不是实例偏移，混在一起看会把"这个字段能不能这样读"判断错。
                result.detail = Format("%s offset=%zu 宽度=%zu 类=%s",
                                       field.value.IsStatic() ? "static" : "instance",
                                       session.value.raw().field_get_offset(field.value.info()),
                                       field.value.Size(), session.value.ClassName(klass.value).c_str());
            }
        } else if (kind == "method" || kind == "call") {
            // 参数个数可写可不写：写了是严格匹配（推荐，能钉住重载）；不写就按 0..8 探一遍。
            const size_t slash = member_part.rfind('/');
            const std::string method_name =
                    slash == std::string::npos ? member_part : member_part.substr(0, slash);
            if (method_name.empty()) {
                result.detail = "语法错误：方法名为空";
                report.items.push_back(std::move(result));
                continue;
            }

            int argc = -1;
            bool arity_given = false;
            if (slash != std::string::npos) {
                const std::string arity_text = member_part.substr(slash + 1);
                if (!ParseNonNegativeInt(arity_text, &argc)) {
                    result.detail = "语法错误：参数个数不是非负整数（'" + arity_text + "'）";
                    report.items.push_back(std::move(result));
                    continue;
                }
                arity_given = true;
            }

            if (kind == "call") {
                ValidateCall(session.value, result, report, type_part, method_name, argc);
                continue;  // ValidateCall 自己把结果放进了 report
            }

            auto method = il2cpp::Method::Resolve(session.value, type_part, method_name, argc);
            if (!method) {
                result.detail = method.reason + "（类 " + session.value.ClassName(klass.value) + "）";
            } else {
                result.ok = true;
                result.detail = Format("MethodInfo=%p argc=%d%s 类=%s",
                                       static_cast<const void*>(method.value.info()),
                                       method.value.ArgCount(),
                                       arity_given ? "" : "（探测所得，清单里可写死）",
                                       session.value.ClassName(klass.value).c_str());
            }
        } else {
            result.detail = "未知类型 '" + kind + "'（只支持 class/field/method/call）";
        }

        report.items.push_back(std::move(result));
    }

    return report;
}

}  // namespace pylon::boot
