# HookGen regression tests

Run without building the native hook engine:

```powershell
dotnet run --project src/csharp/tests/HookGen.Tests/HookGen.Tests.csproj -c Debug -p:NuGetAudit=false
```

The executable creates an isolated Cecil proxy fixture and a compiled fake Endpoint contract under the platform temporary directory. It regenerates/re-reads the fixture, compiles a real strong-typed C# consumer with SDK-bundled Roslyn, loads all generated types and executes add/remove accessors. No production proxy is overwritten.

Coverage: single/overloaded constructors and pure parameter arrays; populated remove bodies; ref-return rejection diagnostics; metadata keywords; ref/out/in delegate consumption; recursive generic byref/array mapping; generic declaring type + generic method arguments; generic constraints; closed delegate and generic static field references; compact binding for overloads with distinct parameter counts; short IL macros; repeat generation; Profile selection/statistics/stable overload names.

Optional real RuntimeDetour integration (uses a prebuilt DLL; does not build the engine):

```powershell
dotnet run --project src/csharp/tests/HookGen.Tests/HookGen.Tests.csproj -c Debug -- `
  --runtime src/csharp/il2cppinterop/Il2CppInterop.RuntimeDetour/bin/Debug/net10.0/Il2CppInterop.RuntimeDetour.dll
```

This regenerates a separate fixture against the real runtime, compiles the same C# consumer, loads the generated types, and calls the real Endpoint binding APIs. It verifies strict out/in delegate validation, constructor pure-parameter resolution and independent declaring-type/method generic closure. It does not call Add on CLR-only fixture targets or exercise native ABI/detours.

Optional production-copy size measurement (the runtime DLL must expose the current 8-parameter `BindGenericOnce` protocol):

```powershell
dotnet run --project src/csharp/tests/HookGen.Tests/HookGen.Tests.csproj -c Debug -- `
  interop/generated/Assembly-CSharp.dll `
  src/csharp/il2cppinterop/Il2CppInterop.RuntimeDetour/bin/Debug/net10.0/Il2CppInterop.RuntimeDetour.dll
```

Both Full and a sample Profile (`Terraria.Main.Update`/`Draw`) are generated to separate temporary copies. This measures artifact size, not runtime performance or complete Mod compatibility.

Profile API:

```csharp
OnHookGenerator.Generate(proxyCopy, runtimePath, new HookGenOptions
{
    MethodFilter = method => selectedMethodIds.Contains(method.FullName)
});
```

The two-argument API remains Full. Names are allocated from the full candidate inventory before filtering so selecting one overload does not rename it. A Profile intentionally omits unselected public On APIs; use Full for general-purpose third-party Mod distribution. `HookGenResult` includes `CandidateHooks`, `SkippedByFilter`, and `Diagnostics` (including unsupported ref-return methods).
