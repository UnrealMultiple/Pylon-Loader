// 层：内核层 / boot —— 引用校验
// M2 的验收点之一："故意改一个名字 → 停用而不是崩"。所以校验的产物不是 bool，而是
// **每条引用的逐条结果 + 一句能写进日志的原因**。内核不认识具体游戏：清单是数据，
// 由 Java 侧传进来（M4 会由 Mod 的 manifest 生成）。
#pragma once

#include <string>
#include <vector>

#include "pylon/Status.h"

namespace pylon::boot {

/// 一条引用的解析结果。
struct RefResult {
    std::string spec;     // 原始清单行
    bool ok = false;
    std::string detail;   // 成功：解析到什么（类名/字段偏移/参数个数）；失败：为什么
};

struct RefReport {
    std::vector<RefResult> items;

    size_t OkCount() const;
    size_t FailCount() const { return items.size() - OkCount(); }
    bool AllOk() const { return FailCount() == 0; }

    /// 失败时的"停用原因"：一行一句，直接进日志与（M3 之后）内置 UI。
    std::string FailureReason() const;

    /// 逐条结果的多行文本，打日志用。
    std::string Describe() const;
};

/// 清单语法（docs/08 §2.2）：
///   class:<namespace>.<Name>            field:<ns>.<Class>::<field>
///   method:<ns>.<Class>::<name>/<argc>  方法也可只写名字（内核按 0..8 探一遍并报出真实 argc）
///   call:<ns>.<Class>::<name>[/<argc>]  **真的调用**（只支持静态方法，有副作用）
/// `#` 开头与空行忽略。
class RefValidator {
public:
    /// 逐条解析；**任何一条失败都不会中断后面的**（一次把问题都打出来，
    /// 免得用户改一个名字重跑一次）。
    static RefReport Validate(const std::vector<std::string>& specs);

private:
    /// `call:` 条目：只能在**游戏线程**上执行。探测线程上遇到它时记为"未执行（跳过）"，
    /// 而不是判失败 —— 否则清单里出现一条 call: 就会把全部 Mod 停用。
    static void ValidateCall(RefResult& result, RefReport& report, const std::string& type_part,
                             const std::string& method_name, int argc, bool arity_given);
};

}  // namespace pylon::boot
