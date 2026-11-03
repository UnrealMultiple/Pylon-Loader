# Pylon.NaviteAPI

内核导出面（libpylon.so 的 `extern "C"` 符号）在 C# 侧的唯一绑定程序集。
**所有 `[LibraryImport("pylon")]` 都只出现在这里**，其他工程不得再声明 P/Invoke。

## 目录

```text
ImGui/            namespace Pylon.NaviteAPI.ImGui
  Generated/      机器生成（tools/imgui-gen）：ImGuiNative（ig* 导出）、ImGui 友好层、结构体、枚举
  ImGuiTextures   纹理上传闸门（igCreateTexture / igDestroyTexture）
  ImGuiNativeApi  覆盖层画布（PylonImgui_*：IsReady / BeginFrame / DisplaySize …）
  ImVector / ImTextureRef / VaList / Util / Delegates …  绑定需要的托管侧原语
Effects/          namespace Pylon.NaviteAPI.Effects
  ScreenEffectsNative   PylonFx_*（着色器注册、提交、统计）
  PylonFxStatsNative    与内核 PylonFxStats 逐字段对齐的封送结构
Host/             namespace Pylon.NaviteAPI.Host
  LogNative             PylonMod_Log
  HookNative            PylonMod_Hook*
  ThreadNative          PylonMod_IsGameThread / *ThreadId
  PatchNative           PylonMod_PatchCompareLimit / PylonMod_LastError
```

## 约束

- 依赖方向：本工程只依赖 BCL。不引游戏代理、不引 Il2CppInterop、不引上层；
  否则 `Pylon.ModLoader` / `Pylon.Managed` 与它会互相引用成环。
- 绑定默认 `internal`，用 `InternalsVisibleTo` 只开给宿主与框架；
  要给 Mod 作者的入口在 `Pylon.ModLoader` 里包一层托管 API（例如 `TerrariaUI`）。
- **只用 `LibraryImport`（源生成）**：`DllImport` 的运行期编组在新代码里一律不接受。
  字符串加 `StringMarshalling = StringMarshalling.Utf8`；回调与不透明缓冲区用裸指针。
- 生成文件（`ImGui/Generated/*.gen.cs`）不手改：改 `tools/imgui-gen` 后整份重新生成。

## 重新生成 ImGui 绑定

```powershell
cd tools/imgui-gen
node main.mjs      # 同时刷新 C++ 桥接（core/src/imgui/PylonImguiBridge.gen.cpp）
```
