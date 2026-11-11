# PerfTest 0.4 — typed proxy and raw bridge benchmarks

The entry implements only `IPylonMod`. It registers one `Main.Update` listener and one ImGui tab; it does not use the content `Mod` base class, register commands, or install world/content/save pipelines. No benchmark or metadata scan starts without an explicit panel/file request. The UI keeps at most six result lines, each truncated to 120 characters.

## Build and install

```powershell
dotnet build src/csharp/samples/PerfTest/PerfTest.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
```

Install only `bin/Release/net10.0/PerfTest.dll` and `manifest.json` in `Mods/PerfTest/`; do not install private copies of framework/game assemblies from the build output. This project neither deploys itself nor starts a device.

## Manual operation

Open the loader's ≡ overlay and select **PerfTest**:

- **Bunny 同口径 / Run typed**: typed generated proxy benchmark with the protocol below.
- **原始路径对照 / Run raw**: retained production `GuardedInvoke.Invoke` versus `il2cpp_runtime_invoke` comparison.
- **统计 ABI 覆盖 / Coverage**: metadata planning only, not callable proxy coverage.
- **取消 / Cancel**: stop at the next frame; an already running continuous round cannot be interrupted.

Keep the game foreground, remain still and avoid other interaction while measuring. Run after entering a world to include world/player/string cases; those cases explicitly SKIP in menus. Loading a world is never necessary for the pure math cases.

## Explicit file requests

Every 30 game frames the runner checks `request.txt` in both directories below. UTF-8 text `bunny`, `raw`, `safety`, `frameab`, `framediag`, `coverage`, or `cancel` requests the corresponding operation. The file is consumed once; no file means no operation. Requests while busy are discarded rather than queued. Use these only as part of an explicitly authorized test, not as a startup task.

```text
/storage/emulated/0/Android/data/dev.pylon.loader/files/PerfTest/request.txt
/storage/emulated/0/Android/data/dev.pylon.loader/Mods/_data/PerfTest/request.txt
```

The first path is the recommended automation endpoint. The second is derived from the installed Mod directory and may change for a different installation layout. Creating the external directory/request file from ADB is the deployer's responsibility; this sample does not invoke shell commands.

## Bunny-compatible typed mode

This implementation was written independently using the published benchmark protocol, not copied from Bunny's source. It targets the same game operations and input shapes:

| Case | Operations per round | Correctness check |
|---|---:|---|
| Empty integer accumulation loop | 20,000 | baseline only |
| `MathHelper.Clamp(float,float,float)` | 20,000 | clamp above upper bound returns 10 |
| `Vector2.Distance((1,2),(4,6))` | 20,000 | distance is 5 |
| `Matrix.Add(CreateScale(2),CreateScale(3))` | 20,000 | diagonal 5,5,5,2 and an off-diagonal zero |
| `WorldGen.InWorld(100+(k&7),100,0)` | 20,000 | negative coordinate false, interior true |
| Local player's `CountBuffs()` | 20,000 | count agrees with nonzero buff slots |
| Local player's `Center.X` | 20,000 | center agrees with position + size/2 |
| `Lang.GetItemNameValue(98).Length` | 5,000 | localized name is nonempty |

Every case warms up 16 calls and measures three full contiguous rounds. Each round dispatches a delegate **once**, whose body contains the typed loop and calls the real generated game proxy. No per-operation reflection/delegate/native-pointer shortcut is used. Preparation and correctness checks are untimed. The result is median round-average ns/op minus the median empty loop, clamped to zero. Raw median/minimum and empty-loop cost are retained for transparency. Matrix inputs are prepared outside timing.

Only one round runs per frame, but the entire 20,000/5,000 operations remain continuous inside that round. **There is no 2 ms inner-loop slicing in this mode: a round may visibly stall the game thread.** Bunny's original test runs all cases together in one frame; separating rounds across frames is a deliberate responsiveness difference and can affect CPU frequency/cache/background load. Cancellation and world availability are checked between rounds, not within the timed loop.

The selected proxy IL is inspected during setup to determine whether it calls `GuardedInvoke` or fixed `runtime_invoke`. `IsDirectSupported(IntPtr)` is used if available, otherwise private `Resolve` is consulted only during setup. Labels distinguish DIRECT/FALLBACK/UNKNOWN. Reflection errors affect the label, not the measured typed call. A FALLBACK/UNKNOWN result must not be claimed as direct-call speedup. No GC transition suppression is added by this Mod.

## Safety mode (not a benchmark)

Explicit `safety` request / **异常恢复 / Safety** runs on the game thread only. It resolves IL2CPP `System.Math.Sign(double)`, requires a DIRECT plan, and first confirms `Sign(NaN)` throws through `runtime_invoke`. It then repeats 64 DIRECT throws, requiring a nonzero exception returned from the native frame each time. Every throw is followed by successful DIRECT `Sign(-42)` and integer `Clamp` checks. Missing metadata, fallback, missing exceptions, or failed recovery is FAIL, never a passing unwind test. Reports: `safety-latest.txt` and a timestamped archive. This exercises repeated exception unwind/recovery, not exhaustive register/ABI coverage; native debugger verification is still needed to inspect individual restored registers.

## Production vs diagnostic build

Default runtime builds compile out both route switches and the TLS comparison branch from `TryInvokeFrameCached`; generated pilots use only static certificates (plus metadata-mismatch fallback). `Invoke` and `TryInvokeFrame` remain for existing callers. `FrameDiagnosticsAvailable` is false; legacy switch setters reject false with NotSupportedException rather than silently producing fake comparisons. `safety` and `bunny` remain usable.

For explicit A/B work only, build the runtime with `-p:PylonFrameDiagnostics=true` and deploy that matching runtime. Only this build accepts `frameab` / `framediag`; the Mod rejects these requests on production runtime before changing UI/state. Rebuild without the property before shipping. No native protection is disabled by either build.

## Generated frame pilot A/B

Explicit file request `frameab` verifies that Clamp, Distance and Matrix.Add proxies contain `TryInvokeFrame`, and that runtime metadata certifies the expected frame shapes. It runs five alternating FRAME/COMPAT pairs in the same process, with typed 20,000-call loops, a warm loop for each side, and correctness checks. Reports `frameab-latest.txt` and a timestamped archive. Each number includes loop cost (no empty-loop subtraction), so do not equate it with the Bunny net protocol. This opt-in test runs continuously on one game frame and can briefly stall rendering. `GuardedInvoke.FramePilotEnabled=false` selects the compatibility path; it does not disable exception/thread protection. New pilot proxies require the matching runtime and must pass `hookgen` after generation.

## Frame scheduling / certificate diagnostic

Explicit `framediag` reuses the **exact Bunny typed loops** and also constant-result loops. Per frame it measures the first 20k calls, warms a full identical loop, then measures another 20k without yielding. Five TLS/CACHED pairs alternate order; every sample and median is retained. CPU number/current frequency are untimed endpoint snapshots, not proof of the frequency throughout the interval. Tiering/PGO environment settings are reported but are not JIT event evidence.

The diagnostic temporarily hides the ImGui overlay (four frames to drain old rendering), then restores it even on cancellation/failure. This isolates a reproduced render-thread `GetTexID` assertion, **not a fix** for that renderer issue. Results under this condition must not be silently substituted for the standard visible-overlay Bunny protocol. Three same-frame five-pair cache-only A/B groups follow while hidden: in those explicitly labelled groups FRAME means CACHED and COMPAT means TLS; both use generated frames, not the old Invoke.

Generated pilot methods hold a private static opaque `FrameMethodPlan` certificate. The runtime mints it only after metadata ABI validation; method/shape changes trigger revalidation. Immutable certificates use acquire/release publication. The certificate contains no native target address: each invocation reads the current MethodInfo methodPointer and keeps the native Guard/thread checks. Metadata lifetime is the same process-lifetime assumption as the existing plan dictionary; domain reload/address reuse would require invalidating **both** caches. Old `Invoke` and `TryInvokeFrame` entries remain supported; new proxies require the matching runtime.

Safety mode additionally exercises ABI shape 4 (static double -> int) through both frame entries: 64 throws per entry, managed exception raising, and same-target/generated Clamp recovery after each throw. This is direct frame-entry coverage, not a generated throwing proxy or exhaustive ABI test.

## Raw bridge mode

The retained six math cases call identical MethodInfo/self/argument cells using production `GuardedInvoke.Invoke` versus `il2cpp_runtime_invoke` + unbox. It uses 3 warmup rounds, 9 measured rounds and 256 calls per path, alternating order, with approximately 2 ms frame slices checked every 8 calls. It does not subtract an empty-loop baseline. This mode isolates the bridge better than typed mode but is **not** the Bunny protocol.

## Results and interpretation

All output goes to both `Mods/_data/PerfTest/` and `/storage/emulated/0/Android/data/dev.pylon.loader/files/PerfTest/` when writable. Each benchmark has a unique timestamped archive:

- `bunny-yyyyMMdd-HHmmss-fff.txt` plus `bunny-latest.txt`;
- `raw-yyyyMMdd-HHmmss-fff.txt` plus `results-latest.txt`;
- `coverage-yyyyMMdd-HHmmss-fff.txt` plus `coverage.txt`.

Logs use `[perf]`. CHECK PASS means only the listed numerical check passed; SKIP is unavailable/precondition mismatch, FAIL is exception/wrong result. Inspect the whole report rather than treating DONE as all-pass.

**B/op measures only CoreCLR thread allocations**, not IL2CPP boxing/native allocation or other threads. No forced GC is performed. CPU/thermal state, platform/runtime versions and game state still matter. Bunny's documented Galaxy A15 timings cannot yield a controlled speedup ratio against a different phone; run the same protocol on the same device/session conditions for stronger comparisons.

Coverage scans game/Unity metadata in small frame slices through `GuardedInvoke.Build`; it never executes the scanned game methods or initializes proxy cctors. Build-plan counts are not proxy-entry intersection counts; constructors/generic/abstract metadata can differ from generated proxy eligibility. Full coverage reports are file/log only, not rendered in the panel.
