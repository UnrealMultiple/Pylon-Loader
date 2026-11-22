# Il2CppInterop（仓库内副本）

本目录是 **BepInEx/Il2CppInterop** 的源码副本，不再作为 git 子模块使用。

## 来源

| 项 | 值 |
|---|---|
| 上游 | https://github.com/BepInEx/Il2CppInterop |
| 提交 | `81a6f78c8b653e0da4a3420ac4cd00819e8b6292`（与 BepInEx-Android 的 `provenance/il2cppinterop.env` 一致） |
| 补丁 | `external/BepInEx-Android/references/il2cppinterop-arm64-disarm.patch`（已应用） |
| 许可 | LGPL-3.0-only（见本目录 `LICENSE`） |

补丁内容（ARM64 / Android 必需，官方源码直接编译不可用）：

- `Common/XrefScans`：x86 的 Iced 扫描改为 ARM64 的 Disarm 扫描；
- `InjectorHelpers`：模块匹配 `libil2cpp.so`、`NativeLibrary.Load("libil2cpp.so")`；
- `IDetourProvider.Create(..., specialReturnBuffer)` 三参签名；
- `Hook.ApplyHook` 找不到目标时告警而不抛；
- 所有 `XrefScannerLowLevel` 调用点改为三参 `JumpTargets`；
- Runtime / HarmonySupport retarget 到 `net10.0`。

## 本仓库在上游之外做的改动

1. **移除 HarmonySupport 工程**：hook 只有 `Il2CppInterop.Runtime.Hooking` 一套。
2. **Hook 引擎 = `Il2CppInterop.RuntimeDetour` 工程**：
   - `Hook`：MonoMod 形状的自由 hook（任意 `MethodBase` 或原生地址）；
   - `DetourBackend`：原生 detour 抽象类，宿主（Pylon）实现它接内核 ShadowHook；
   - `HookEndpoint`：生成的 `On.*` 事件的运行时端点；
   - `HookPlan/HookStub/MirrorTypes`：签名映射，含 `ref/out`、真值类型按值传、
     **装箱值类型（`Il2CppSystem.Nullable<T>` 这类 class 代理但原生按值传）的镜像 blob + `il2cpp_value_box/unbox`**。
   - `Il2CppInterop.RuntimeDetour.HookGen`：用 Cecil 把 `On.*` 事件直接发 IL 写进代理程序集
     （不生成 C# 源码、不经过 Roslyn；`On.__PylonHookGen` 标记保证可重复执行）。
3. **TFM 统一为 `net10.0`**（Common / Generator / StructGenerator / CLI），因为本机只有 .NET 10 SDK，
   且 `Il2CppInterop.Common` 要引用随运行时提供的 `Disarm.dll`（net6 元数据，netstandard2.0 工程会 CS1705）。
4. **离线 NuGet**：`src/csharp/il2cppinterop/local-feed/Disarm.2022.1.0-master.99.nupkg` 由
   `runtime/bepinex-android/bepinex/core/Disarm.dll` 重建（见该目录 README）。
5. 删除 `global.json`（上游把 SDK 钉在 8.x；本仓库统一用根目录的 .NET 10 SDK）。
6. **生成物 corlib 升级到 .NET 10**：`Generator/Utils/CorlibReferences` 的 `TargetCorlib` 从
   `KnownCorLibs.SystemRuntime_v6_0_0_0` 改为 `SystemRuntime_v10_0_0_0`（TargetFrameworkAttribute 同步为
   ".NET 10.0"），与仓库 net10.0 的 Runtime/Common/HookGen 一致，避免生成物带 System.Runtime 6.x
   的陈旧引用。
7. **`Nullable<T>` 字段属性 getter 走 `RawValueBox.Wrap`（生成器级根治，2026-11-21）**：
   `Generator/Extensions/ILGeneratorEx.cs` 的 `EmitPointerToObject` 新增分支——当被包装的
   **原始类型是本构建会抹 `hasValue` 前缀的 `System.Nullable<T>`**、且走的是「载荷指针 → 盒」
   的 `unboxValueType=false` 路径时，不再发 `il2cpp_value_box` + `new .ctor(IntPtr)`，改发
   `RawValueBox.Wrap<Nullable<T>>(klass, payload, il2cpp_class_value_size(klass))`。
   - 为什么：本构建的 `il2cpp_value_box` / `object_new` 造盒会抹掉 `Nullable` 载荷的 `hasValue`
     前缀（`RawValueBox.cs` 头注原文 "Both value_box and object_new erase Nullable's hasValue
     prefix in this runtime"）。生成代理里所有 `Nullable<T>` 字段的属性 getter（实例 + 静态）
   和同 emit 分支上的**直调方法 `Nullable<T>` 返回值**都把字段载荷经这种盒中转，读端
   `HasValue`/`Value`（runtime_invoke）与 `hasValue`/`value`（字段偏移直读）不可靠——
     `Item.shopCustomPrice` 是实证案例（docs/pipeline/content-shop.md 2.7、docs/audit/bunny-vs-pylon/shop.md D4）。
   - `Wrap` 是本 Runtime 的既有公共 API（HookGen 转换层先对返回值/装箱参数用了同一出口，
     真机验证过）；它用 `il2cpp_gc_alloc_fixed` 造完整载荷的根持有盒，代理回收时释放。
   - setter 不改：生成的 setter 是 `unbox(入参盒)` + `cpblk(value_size)`，属性本身无辜；
     盒载荷在源头（ctor / Wrap 盒 / runtime_invoke 返回盒）补全后，cpblk 落盘天然正确。
     没有把 setter 入参改成托管 `System.Nullable<int>`——那要引入「托管 Nullable<T> 布局 ==
     本构建 native 布局」的假设和混合类型属性，实现面更大。
   - `Generator/Utils/RuntimeAssemblyReferences.cs` 新增 `RawValueBox` 类型引用与
     `RawValueBox_Wrap` 方法引用（生成程序集对 Runtime 的既有 assemblyRef 机制，与
     `Il2CppObjectPool.Get` 同形）。
   - 复验：`pwsh -File src/csharp/tools/pylon.ps1 interop -DummyDir refs/DummyDll -Force`
     重新生成后，全部生成 DLL 的反编译 diff 仅限 Nullable 字段 getter（Assembly-CSharp 99 个
     getter 等，setter 与其余成员不变）；真机断言在 PipelineTest SHOP-W3。


## 构建

```powershell
dotnet build src/csharp/il2cppinterop/Il2CppInterop.Runtime/Il2CppInterop.Runtime.csproj -c Release
dotnet build src/csharp/il2cppinterop/Il2CppInterop.CLI/Il2CppInterop.CLI.csproj -c Release
```

沙箱/多进程环境下 MSBuild 会静默失败（命名管道不可用），加：
`-m:1 -nodeReuse:false -p:MSBUILDNOINPROCNODE=1 -p:UseSharedCompilation=false`。8. **泛型参数类型字段 getter 改发形状分派读（生成器级根治第二批，2026-11-22）**：
   `Generator/Utils/FieldAccessorGenerator.cs` 的 `MakeGetter`——当**原始字段类型是泛型参数
   （`GenericParameterSignature`，即开放泛型类里 `T` 类型的字段）**时，getter 不再发
   `PointerToValueGeneric<T>(ptr, true, false)`，改发 `typeof(T).IsValueType` 运行时分支：
   值类型 `ldobj T` 裸载荷直读、引用型走原链（引用形状不经 `value_box`，无缺陷）。
   - 为什么：条目 7 的 Wrap 分支只覆盖**具体** `Nullable<T>` 类型（如 `Item.shopCustomPrice`），
     而 `Il2CppSystem.Nullable<T>` 代理类自己的 `value` 属性（字段类型是泛型参数 `T`）走的
     `PointerToValueGeneric(isFieldPointer=true)` 仍是 `il2cpp_value_box` 缺陷链——真机探针
     （SHOP-W3-PROBE）实证 `value(字段)` 读垃圾而 `hasValue` 字段（bool 基元直读）、
     `HasValue`/`Value` 方法（runtime_invoke）全对，全 interop 里泛型参数字段 getter 是唯一
     还在过 `value_box` 的路径（已知基元字段都是 `ldobj` 直读）。
   - 与 setter 对称：`EmitObjectStoreGeneric` 的值类型分支早已是 `stobj T` 直写（`Unsafe.Write`），
     getter 缺失对称实现才是缺陷根源。
   - 复验：`interop -Force` 重生后 `Nullable<T>.value` getter 反编译为
     `typeof(T).IsValueType ? Unsafe.Read<T>(ptr) : PointerToValueGeneric(...)`；
     真机 SHOP-W3 四路（显式构造写 / 代理属性读 / Wrap 盒 `hasValue`+`value` 字段读 /
     Marshal 原生直读）+ 写回另一物品 + 清价全绿（commit 22f8ad9）。
