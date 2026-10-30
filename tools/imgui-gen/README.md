# imgui-gen

从 Pylon 嵌入的 dear imgui 头文件（mainline 1.92.2b）生成 C# 绑定与 C++ cimgui 风格桥接层，
API 暴露方式对齐 ImGui.NET：

- 解析：`imgui.h` / `imgui_internal.h` / `imstb_textedit.h`
- 产出 1：`src/core/src/imgui/PylonImguiBridge.gen.cpp`
    每个导出均为 `extern "C" PYLON_IMGUI_EXPORT`，命名对齐 C# 侧 DllImport；
    布尔 → int32 语义、UDT 返回走 pOut、重载用 `_Str` / `_FontPtr` 等后缀。
- 产出 2：`src/csharp/managed/Pylon.ImGui/Generated/*.gen.cs`
    ImGuiNative（DllImport "pylon"）、ImGui（友好静态层，字符串/ref/span 编组）、
    结构体原生布局 + Ptr 包装（RangeAccessor / ImVector / NullTerminatedString）、枚举。

用法：
    node main.mjs        # 重新解析并生成全部文件

注意：
- 头文件版本与 ImGui.NET 1.91.6+docking 不同（本嵌入为 1.92.2b mainline），
  结构字段以本仓库头为唯一真源，勿直接复制 ImGui.NET 的 gen 文件。
- 生成器以“可复现、无噪注释”为原则：关注生成逻辑，产出由 model 决定。
