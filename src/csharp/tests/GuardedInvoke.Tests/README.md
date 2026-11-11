# GuardedInvoke regression tests

Run from the repository root:

```powershell
dotnet run --project src/csharp/tests/GuardedInvoke.Tests/GuardedInvoke.Tests.csproj -p:UseSharedCompilation=false
```

The test project compiles the production `GuardedInvoke.cs` against a deterministic IL2CPP metadata double. It does not load game binaries or execute ARM64 code on the host.

Coverage: native/managed frame layout, X/D register-number collisions, float/double HFA packing, partial struct words, primitive/enum/struct fallback unboxing, large fallback return buffers, generic reference returns, reentrant fallback buffers, exception outputs and argument validation.

Native ARM64 execution and exception unwinding still require device validation. InternalCall/PInvoke/runtime implementations and generic declaring classes intentionally use `il2cpp_runtime_invoke`; the native executor must not scan instructions or bypass method wrappers.
