# 钩子：`On.*` 与 `Il2CppInterop.RuntimeDetour`

项目里只有一套 hook：**MonoMod 写法 + 原生 detour 后端**。Harmony 已移除。

## 用 `On.*`（最常用）

`On.*` 事件由 `Il2CppInterop.RuntimeDetour.HookGen` **直接以 IL 生成进代理程序集**
（`interop/generated/Assembly-CSharp.dll`），所以 Mod 只引用代理程序集就能用：

```csharp
// 原版之后做点事
On.Terraria.Main.Update += AfterUpdate;

private static void AfterUpdate(On.Terraria.Main.orig_Update orig, Main self, GameTime gameTime)
{
    orig(self, gameTime);       // ← 原函数
    // 原函数之后
}
```

前缀语义（在原函数之前跑、必要时跳过原函数）：

```csharp
private static bool CanUse(On.Terraria.Player.orig_ItemCheck_CheckCanUse_Inner orig,
                           Player self, Item item, bool ignoreCursed)
{
    if (ShouldBlock(item)) return false;   // 不调 orig = 跳过原版
    return orig(self, item, ignoreCursed);
}
```

- 实例方法的 handler 第二个参数是 `self`；静态方法没有。
- 重载事件名带 `_1` / `_2` 后缀（同名方法的第 2、3 个）。
- 完整签名索引（生成物）：`interop/generated/on-signatures.txt`。
- 退订：`On.Terraria.Main.Update -= AfterUpdate;`（最后一个订阅者退订时撤销 detour）。

## 自由 hook（任意方法 / 原生地址）

```csharp
using Il2CppInterop.RuntimeDetour;

using var hook = new Hook(
    typeof(Main).GetMethod("DrawMenu", new[] { typeof(GameTime) }),
    new Action<Action<GameTime>, GameTime>((orig, time) =>
    {
        orig(time);
    }));
```

- `new Hook(MethodBase, Delegate)`：目标是 interop 代理方法；
- `Original` 拿到 orig 委托，`Undo()` / `Dispose()` 撤销；
- 也支持对已被 `On.*` 占用过的目标叠加（链式，先挂的先跑）。

## 原生 detour 由宿主提供

`Il2CppInterop.RuntimeDetour` 不绑定平台。宿主实现抽象类：

```csharp
public sealed class DobbyDetourBackend : DetourBackend
{
    public override bool IsAvailable => PylonMod_HookAvailable() != 0;
    public override int Apply(IntPtr target, IntPtr detour, out IntPtr trampoline)
        => PylonMod_HookApply(target, detour, out trampoline);
    public override int Remove(IntPtr target) => PylonMod_HookRemove(target);
}

DetourBackend.Current = new DobbyDetourBackend();   // 启动时、任何订阅之前
```

Pylon 侧就是 `src/csharp/managed/Pylon.Managed/DobbyDetourBackend.cs`，后端是内核的 Dobby 导出。

## 支持范围

支持：

- 静态 / 实例方法
- `int/long/float/double/short/byte/bool/char`、枚举、`string`
- 游戏代理类型（`Il2CppObjectBase` 派生）与 il2cpp 数组
- **真值类型按值**（`Vector2`/`Rectangle`/…，含结构体返回）
- `ref` / `out`
- **装箱值类型**（`Il2CppSystem.Nullable<T>` 这类"class 代理但原生按值传"）：
  按 `il2cpp_class_value_size` 造镜像 blob，参数/返回值/ref/out 都走
  `il2cpp_value_box` / `il2cpp_object_unbox` 桥接
- **泛型类型**：`On.ReLogic.Content.Asset<T>` 这类泛型宿主类，self 用闭合泛型实例
- **泛型方法**：`On.GameObjectExtensions.FindInChildren_1<GameObject>` 这类泛型宿主 + 泛型委托，
  运行时按 typeArgs `MakeGenericType/MakeGenericMethod` 解析

不支持（会在挂钩时抛 `NotSupportedException`，不静默）：

- 字段访问器（没有 `NativeMethodInfoPtr_*`）
- 指针参数、byref 返回
- 含引用字段、布局不可直传的值类型

## 生成的形态

```csharp
namespace On.Terraria
{
    public static class Main
    {
        public delegate void orig_Update(Main self, GameTime gameTime);
        public delegate void hook_Update(orig_Update orig, Main self, GameTime gameTime);
        public static event hook_Update Update;
    }
}
```

事件访问器（IL）：

```text
ldsflda __pylon_ep_N
ldtoken Terraria.Main;  call Type.GetTypeFromHandle
ldstr "Update"
newarr Type …（每个参数 ldtoken）
ldtoken orig_Update; call Type.GetTypeFromHandle
ldtoken hook_Update; call Type.GetTypeFromHandle
call HookEndpoint.BindOnce      // 第一次订阅才解析 + 挂 detour
ldarg.0; callvirt HookEndpoint.Add
```

`On.__PylonHookGen` 是标记类型：重新生成时先摘掉整片 `On.*` 再重发，可重复执行。

## 性能

桌面探针（`src/csharp/tools/hookprobe --bench`，x64，不含 il2cpp）：

```text
[bench] 直接调原函数   6.4 ns/call
[bench] 走 hook 入口  18.8 ns/call
[bench] 引擎增量      12.4 ns/call
```

对比 Harmony：Harmony 的 IL2CPP 支持层每次调用要走 `il2cpp_runtime_invoke`
（参数缓冲 + 装箱 + 反射分派），热路径通常是微秒级；现在没有这一步。

注意事项：

- 装箱值类型的参数每次调用会 `il2cpp_value_box` 一次（分配）。这类方法多在 UI，
  如果某个热方法带 `Nullable` 参数再单独处理。
- 多订阅者时每多一层加一个入口桩（MonoMod 同语义，先挂的先跑）。

## 实现位置

| 文件 | 作用 |
|---|---|
| `src/csharp/il2cppinterop/Il2CppInterop.RuntimeDetour/Hook.cs` | 自由 hook、链的装/卸 |
| `.../HookPlan.cs` | 托管签名 → 原生槽位、MethodInfo*/methodPointer、能力校验 |
| `.../HookStub.cs` | 入口桩 / orig 桩 IL |
| `.../MirrorTypes.cs` | 装箱值类型的镜像结构 |
| `.../HookEndpoint.cs` | `On.*` 的运行时端点 |
| `.../DetourBackend.cs` | 原生 detour 抽象（宿主实现） |
| `src/csharp/il2cppinterop/Il2CppInterop.RuntimeDetour.HookGen/OnHookGenerator.cs` | 把 `On.*` 发 IL 进代理程序集 |
| `src/csharp/managed/Pylon.Managed/DobbyDetourBackend.cs` | Pylon 的 Dobby 后端 |

## 重新生成 `On.*`

```powershell
# 代理程序集已经存在时（幂等，只重发 On.*）
dotnet src/csharp/il2cppinterop/bin/Il2CppInterop.CLI/net10.0/Il2CppInterop.CLI.dll hookgen --input interop/generated

# 完整链路：生成代理 + 写 On.*
pwsh -File src/csharp/tools/pipeline.ps1 -Stages interop
```