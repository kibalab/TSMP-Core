# Continuous frame delivery measurement

This harness measures the production Encoder and Decoder under a continuously changing source. It does not wait for a decode to finish before publishing another frame. `PASS` means that the measurement completed without invalid application data, not that all frames were delivered. See [the original sequential-path measurements](RESULTS.md) and [the predicted-readback comparison](PREDICTED-READBACK.md).

## Method

- A real Encoder serializes one always-sent `TransSync` byte-array field and renders through an installed codec. The Decoder reads that output RenderTexture directly.
- The packet carries a monotonically increasing number and complementary check bytes. The receiver records the number and a local monotonic timestamp inside `OnTSMPVariableReceived`, after the real binding dispatcher has assigned the decoded value.
- Encoder and Decoder run once per eligible Unity frame. Both are manually invoked by the harness to establish a repeatable publish-before-decode order; neither implementation is replaced. `autoEncode` and `applyEveryFrame` are off only to prevent duplicate automatic invocations.
- Publication is wall-clock paced, at most once per Unity frame. Missed sender deadlines are counted separately from published frames that were never applied. There is no catch-up burst that could artificially count several overwritten images as distinct visible frames.
- Each case first completes one preflight frame to exclude first-use shader creation, then continuously warms up for two seconds and measures for twelve seconds (sixty for `sustained-60`). It keeps publishing and decoding for one more second, without counting the new publications, so the last measured IDs have an ordinary opportunity to be decoded instead of being censored by shutdown. It then stops publishing and drains only the pending decode.
- Frame loss is the fraction of actually published, measured-window IDs absent from receiver callbacks, including the observation tail and final pending callback. Apply Hz counts measured-window IDs applied before the window ends and can consequently be slightly below the sender rate even with zero missing IDs. Publication and application traces retain both sides of this boundary.
- End-to-end local latency is receiver callback time minus the time immediately before encoding that ID. Snapshot latency starts immediately before its first decode request. Source age samples the age of the latest applied value each Unity frame and includes the hold time between applications.
- The first/last-third latency means expose growing backlog. Application-gap percentiles expose uneven motion updates even if mean latency stays bounded.
- Source and receiver use the same machine's `Time.realtimeSinceStartup` clock. The Udon-compatible callback timestamp is float precision; host scheduling uses its double-precision counterpart.
- The sender-only control deliberately has no receiver. Its missing/apply columns are not a delivery result.

## Execution

The optional [CPU, GPU and memory profile](RESOURCE-PROFILE.md) uses the `profile-*` cases. These are excluded from an unfiltered delivery run. Profiling performs separate timed calls, warmed memory inventories and GPU replay microbenchmarks; it is not a live VRChat or avatar-rig workload measurement.

The optional [combined byte output](COMBINED-OUTPUT.md) comparison uses `TSMP_DISABLE_COMBINED_OUTPUT=1` for the separate-packing control and `0` for compatible direct-output shaders. It is independent of prediction enablement; the first frame and prediction fallbacks still use ordinary decode passes.

The [two-slot overlap](TWO-SLOTS.md) comparison uses `TSMP_SINGLE_SLOT=1` for single-slot admission and `0` for the default bounded two-slot mode. This switch is independent of prediction and combined output. Use the same source and scheduling mode for each comparison. `overlap-regression` explicitly enables two slots for its assertions regardless of this switch.

`overlap-regression` checks simultaneous captures, full-capacity rejection, codec/sample/length changes, source overwrite, disable/re-enable cancellation and repeated RPC deduplication. Its ordering test first lets real GPU requests complete while the consumer is paused, then temporarily holds the older slot pending via test-only private state access. This tests ordered draining without claiming the hardware naturally completed out of order or synthesizing readback bytes. It also checks that a discarded or error-injected head does not stall its successor; diagnostic logging is disabled only around the intentional error. Results are written to `overlap-regression.txt`. `sustained-60-at-120` measures sixty seconds of 60-Hz publication with a 120-Hz target render loop.

The [30-FPS measurements](THIRTY-FPS.md) use `small-30-at-30`, `small-60-at-30`, `hd-large-every-frame-at-30` and `sustained-every-frame-at-30`. The first two retain wall-clock pacing. The latter two publish once every Unity update, regardless of a separate send deadline, to avoid under-driving the receiver when scheduling jitter skips wall-clock slots. They still never publish more than one image per update. `publicationMode` identifies these modes in the CSV; `schedulerMisses` is not applicable and stays zero in `every-update` mode. Use measured `loopHz`/`txHz` to establish the achieved rate. A 60-Hz sender setting in the same 30-FPS process is not an independent 60-FPS source test.

Use isolated Unity 2022.3.22f1 projects with file dependencies referencing the actual Core and four codec repositories. Do not use `-nographics`. Only run one benchmark process at a time.

```powershell
./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/continuous/native `
  -Mode Player

./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-VRC `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/continuous/udon `
  -Mode Udon
```

`-Mode Play` runs the native path in Editor. `-Filter small-60-at-60,small-60-at-120` selects cases. Every run writes `environment.txt`, `summary.csv`, per-case application traces, and `status.txt`; the wrapper also records Editor/Player logs and the Player BuildReport summary.

Set `$env:TSMP_AUTOMATIC_SCHEDULING = '1'` to measure automatic decoding instead of the legacy manual coroutine loop. The sender runs in an early `Update`; native decoders use their ordinary `Update`, while the VM adapter invokes the compiled `_update` event from that phase. The first preflight frame and `prediction-regression` remain manual. Remove the variable to use the earlier manual request pattern. Do not attribute a difference between these harness modes to a decoder optimization: publication/submission phase and instrumentation affect readback scheduling.

The rejected completion-retry experiment is archived in `Experiments/CompletionRetry.patch`, outside the imported package. See [its results and reproduction instructions](COMPLETION-RETRY.md). Only that optional overlay exposes the timing fields and `TSMP_DISABLE_READBACK_RETRY` comparison switch used by the harness. Normal production tests need no patch and install no new decoder options. Run `Audit-Scheduling.ps1 -ResultsDirectory <artifact-root>` to cross-check publication/application counts and, when instrumented, callback phases and per-frame capture limits.

For a sequential-path A/B control, set `$env:TSMP_DISABLE_PREDICTION = '1'` before starting the wrapper, then remove that environment variable before the enabled run. The final summary columns count accepted predictions and fallbacks. Use `-Filter prediction-regression` to check codec switches, changing payload lengths, multi-row packing, source overwrites, header option changes, CRC rejection, disable/enable cancellation, repeated RPC deduplication and two concurrent decoders in either native or compiled Udon execution. That case writes `regression.txt`; it is not a throughput measurement and requires prediction enabled. The Udon adapter additionally verifies automatic material assignment and serialization to a backing behaviour before entering Play Mode (`preparation.txt`).

The Udon adapter performs a full client-target UdonSharp compilation after Play Mode entry and before retrieving programs, loads the actual Encoder, Decoder, codec and probe programs into the SDK VM, and uses real VRC GPU readback callbacks. `bytecode-target.txt` records that selection. It does not manually synthesize or complete readbacks. It constructs backing behaviours explicitly for instrumentation and therefore is not a production scene serialization test or a live VRChat client test.

Before VM assertions or profile timers, the adapter draws cloned codec materials through native Graphics calls and flushes a pixel readback (`graphics-preparation.txt`). Cold shader preparation exceeded the VM's fixed ten-second watchdog at the VRCGraphics.Blit boundary on this host. Preparation leaves the watchdog unchanged and excludes that cost from steady-state measurements; preserve unprepared failures and do not treat this as a cold-start performance result.

`prediction-regression` also checks that production Encoder headers carry a matching payload CRC32. It changes only encoded payload pixels, verifies rejection before either a field or RPC is applied, then retries the intact frame with the same frame/event identity. This coverage runs through Native and compiled Udon paths.

`-Filter prediction-shrink` checks shrinking payloads, all four RGBA remainders,
dirty retained tails, growth, codec/sample/resolution changes, CRC/RPC retry and
both shrink/grow slot orders. It is excluded from unfiltered runs. The SDK VM
counts actual readback callbacks; Native reports `-1` and checks content and
public path counters. The default expects the retained exact-size prediction
rule. `TSMP_EXPECT_SHRINK_PREDICTION=1` is only for the private, rejected Luma4
prefix-reuse experiment; it does not enable that behavior in production.

## Interpretation

Compare `txHz` with `applyHz`, not the configured target alone. `schedulerMisses` can indicate that the source itself could not sustain the requested rate. `busyPercent` counts render ticks at which occupied slots meet the configured admission limit; it is not GPU utilization or the percentage of ticks with any pending request. `captureCount` includes duplicate-image requests. `decoderErrorObservations` records failing decoder diagnostics observed between requests, independently of callback delivery gaps.

`valueBytes` excludes TSMP message headers; use `payloadBytes` for the encoded payload length. The four sample-4 cases use the shipped codec prefab settings, including RGB16 4/4/4 Refine and Color256 Robust Refine.

This is a minimal local loopback with one field and a lightweight receiver, not an OBS/Spout/MediaMTX, video compression, multi-avatar, Quest, or live VRChat benchmark. Host VM dispatch and heap observation add measurement overhead. A low-loss result on this machine is not a universal guarantee or proof that a populated world has sufficient headroom.
