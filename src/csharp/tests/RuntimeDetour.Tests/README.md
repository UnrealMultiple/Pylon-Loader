# Hook engine regression tests

Run from the repository root (SDK 10):

```powershell
dotnet build src/csharp/tests/RuntimeDetour.Tests/RuntimeDetour.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet src/csharp/tests/RuntimeDetour.Tests/bin/Release/net10.0/hookprobe.dll --bench
dotnet run --project src/csharp/tests/RuntimeDetour.Tests/RuntimeDetour.Shim.Tests.csproj -c Release -p:UseSharedCompilation=false -- --bench
```

The normal project references the **current production engine and runtime**. Its assembly name is
`hookprobe` to use the engine's existing internal test access. Tests cover delegate contracts,
exact/compact/generic/constructor binding, mixed delegate types in a chain, native boundary
conversions, undo order, retry, concurrent subscriptions, self-undo, invocation frames, generic
shared-code context bypass, idle forwarding sites and repeated apply/undo site reuse.

The separate shim project compiles the same current engine source with a test-only IL2CPP storage
shim. It executes emitted boxed ref/out IL, replacement without orig, post-orig updates, byref
alias preservation and recursive indirect argument handling. It does **not** certify ARM64 ABI,
IL2CPP garbage collector behavior, HFA layout or native exception unwinding.

If package restore is unavailable, the normal project also accepts `-p:HookTestRuntimeDir=...`
pointing to a folder containing Runtime and Common dependency DLLs. This fallback compiles current
RuntimeDetour source; it does not test a stale RuntimeDetour DLL.

## Native lifetime policy

One native entry is cached per code address. Subscriptions form a typed managed chain. Removing the
last subscription removes user handlers, but physically removing the patch requires a backend that
explicitly reports `SupportsSafeRemoval`. The default is false; ShadowHook currently keeps an idle
forwarding patch and trampoline for process lifetime because the host does not yet provide a native
quiescence guarantee. This is intentional bounded-per-target retention, not per-subscription slot
leakage. `HookManager.CachedSiteCount` diagnoses sites; `AppliedCount` counts user subscriptions.

Safe-removal backends defer removal until managed calls leave. Their contract must additionally
protect the native-branch-to-managed-entry gap and must not wait for callbacks while holding Apply/
Remove (the engine serializes publication). `HookManager.FlushRetired` retries deferred removals.
Concurrent subscription changes affect subsequent next-hop reads; an invocation is not an immutable
snapshot of the entire chain.

## Performance interpretation

The benchmark is a desktop unmanaged callback microbenchmark, not game FPS or Android latency.
Measure Release separately from Debug. The stable site adds native in-flight accounting for safety;
multiple handlers now call each other in managed code without additional native crossings. Never
claim a universal speedup from these timings. Profile mode reduces generated metadata without
adding reflection to steady-state dispatch.

## Profile CLI

Use a COPY of the proxy assembly when experimenting. Profile entries are exact Cecil `FullName`
strings, one per line (blank lines and `#` comments ignored). See `profile.example.txt`.

```powershell
dotnet src/csharp/il2cppinterop/bin/Il2CppInterop.CLI/net10.0/Il2CppInterop.CLI.dll hookgen --input YOUR_COPY_DIRECTORY --profile src/csharp/tests/RuntimeDetour.Tests/profile.example.txt
```

Omit `--profile` for Full mode. The CLI reports filtered counts, unsupported signatures and
unmatched entries. Never use a two-event profile for arbitrary third-party Mods.

## Measurement snapshot (Windows x64, Release)

Final production regression executable: direct callback 4.4–4.9 ns, one handler 20.3–21.9 ns,
two handlers 22.0–23.7 ns. The retained old probe reported one handler about 13 ns with a longer,
different benchmark loop; this is only a rough reference, not a controlled A/B. Safety accounting
adds cost, so this change does not claim a single-handler speedup. Additional handlers no longer
cross the native boundary. Android/ARM64 game measurements remain required.

Real Assembly-CSharp copies: original 33,323,520 bytes; fixed Full 33,453,568 bytes / 25,140 hooks;
two-event Profile 20,570,112 bytes / 2 hooks (38.51% below fixed Full). Full grew 0.39% because
missing constructor APIs and valid metadata were restored. No production proxy was overwritten.

Deployment must regenerate proxies with the matching new engine (especially out/in and generic
metadata). Preserve Full generation for general third-party Mod compatibility; Profile mode is an
explicit allowlist and deliberately omits unselected APIs.
