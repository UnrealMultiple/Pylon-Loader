# imgui-gen

从仓库内嵌的 dear imgui 头文件（mainline `1.92.2b`）生成 C# 绑定与 C++ 桥接层，API 形态对齐 ImGui.NET。

## 产出

| 产出 | 位置 | 说明 |
|---|---|---|
| C++ 桥接 | `src/core/src/imgui/PylonImguiBridge.gen.cpp` | 每个导出都是 `extern "C" PYLON_IMGUI_EXPORT`，命名与 C# 侧 `LibraryImport` 对应；布尔按 int32 传递，UDT 返回走出参，重载加 `_Str` / `_FontPtr` 后缀 |
| C# 绑定 | `src/csharp/managed/Pylon.NaviteAPI/ImGui/Generated/*.gen.cs` | `ImGuiNative`（`LibraryImport("pylon")`）、`ImGui`（字符串 / ref / span 编组的友好层）、结构体原生布局与指针包装、枚举 |

输入头文件：`imgui.h`、`imgui_internal.h`、`imstb_textedit.h`。

## 用法

```powershell
node main.mjs      # 重新解析并生成全部文件
```

## 注意

- 结构字段以**本仓库头文件**为唯一真源；嵌入版本与 ImGui.NET 的发布版本不同，不要直接拷 ImGui.NET 的生成文件。
- 生成器以"可复现、无噪注释"为原则：逻辑在生成器里，结果由模型驱动；改生成规则后请整份重新生成，不要手改产出。
