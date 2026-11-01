# Many-field and many-target client VM measurement

Generate the fixed 128-float validation probe and freeze a fresh plan before
running Unity. The matrix compares 32/128 fields on one/32/128 targets, with
changing, unchanged, and quarter-changing values. Eight warm calls precede 32
measurements in each case. One actual EncodeNow opportunity is admitted per
observed Unity frame, avoiding a tight loop of queued graphics submissions.

```powershell
python ./Validation~/BindingCost/Generate-Fixtures.py --plan '<fresh plan.json>'
./Validation~/BindingCost/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated SDK project>' -Plan '<plan.json>' -Results '<fresh results>'
python ./Validation~/BindingCost/Analyze-Results.py --results '<completed results>'
```

Compilation explicitly targets the client both before and after Play entry.
Bytecode is retrieved from the serialized client programs and receipts retained.
Every EncodeNow call is timed through actual UdonBehaviour dispatch with nested
VM work counted once. Input setup, packet checking, file output and injected
Decoder completion occur outside that timer. The Encoder uses the actual GPU
Luma4 path, recorded on every publication. The Decoder application check receives
original protected packet/header bytes through an injected completed slot; it
does not measure receiver GPU work, transport, source deadlines or live FPS.

Captured packet/header pairs retain every emitted field from warmup and measured
calls. A separate Python parser checks original CRCs, identities, complete float
values and expected changed-field sets. Callback count is checked per target on
each call. Untimed semantic controls check first-seen callback order through
rotation, same-length in-place target replacement, binding-direction changes and
sendMode selected inside BeforeEncode. A helper-only search/control measurement
is retained separately and must not be treated as measured whole-Encoder savings.
The direct helper control dispatches callbacks without the helper's sendMode
query and bookkeeping, so their difference is not a pure search-only timing.

Five additional semantic groups cover mixed priority/rotation, a target's first
field being interval-ineligible or ReceiveOnly, one callback changing another
target's sendMode before/after its first visit, callback-time replacement of a
public binding target (old snapshot now, replacement next frame), and changing
an upcoming field's direction inside a callback. Nineteen rows retain actual
callback order/count, encoded-field effects and outer-call time. Their recorder
overhead makes those timings different from the ordinary matrix. `-SemanticsOnly`
runs just these controls and the original nine rounds before a formal benchmark.

For a source A/B, archive both exact Encoder sources under `sources/`, generate
the current probe/plan in a fresh file, then run `Freeze-AB.py --root '<root>'
--plan '<plan.json>'` before output. `Run-AB.ps1` executes six fresh SDK processes
in preselected AB/BA/AB order, changing only the dedicated project's Encoder
source between processes and restoring the candidate in `finally`. The initial
profiling baseline is excluded. `Analyze-AB.py --manifest '<ab-manifest.json>'`
compares all complete matrices and cold/warm observations without dropping any
run or outlier. Sources, probe, plan and executed harness are pinned by hash.

Defaults retain payload CRC, refresh policy and frame layout; this fixture sets
refresh to zero so unchanged serialization cost can be compared without an
elapsed-time-dependent periodic transmission. Garbage collection is requested
between cases outside measured calls; no zero-allocation claim is made.

The historical [tail review](RESULTS.md#current-tail-review-frequency-and-measurement-limits)
identified out-of-timer verification allocations and quadratic identity searches.
The current harness builds a collision-checked identity index once per case,
reuses callback/injection arrays and buffers bounded packet/header snapshots and
timing structs. Raw files and CSV formatting happen after the case. Every packet,
CRC, float and callback check remains; failed calls retain a partial row and any
captured bytes. This reduces observer interference without changing product
Runtime. Source setup, reflection/boxing and decoding still consume host work.
Keep cold paths and per-process clusters separate; N=96 nearest-rank p99 equals
the maximum and does not establish real-world incidence.

Add `-Telemetry` for a separate diagnostic run. The default starts no profiler
recorders and performs no telemetry controls or extra waits. Opt-in telemetry
enumerates available metrics, checks known allocation and forced-GC positive
controls, then resets **and restarts** frame recorders before warmup. Running
flags and sample counts accompany every observation. Unsupported scoped Mono
allocation values are `-1`, not zero. Profiler values are the latest available
samples, with no exact frame ID or individual VM-call attribution. Global
collection-count deltas can locate collection activity within an observation
window, but cannot explain every spike or establish a product allocation rate.
Use a separately frozen off/on/on/off comparison; instrumentation is not free.

`Run-VariableLength.ps1` exercises the actual compiled Encoder and Decoder with
RawBytes, a GPU Luma4 publication and independently owned duplicate receive
bindings. Freeze a JSON plan containing `cases` with `name`, `warm`, `samples`,
`lengths`, `sourceMode` (`reuse`, `fresh` or `pooled`) and `unchanged`. For example,
compare fixed 1024 bytes with alternating `[1024,1041]`, using fresh and pooled
producer arrays separately. The 17-byte difference matches a root pose record's
width; this remains synthetic RawBytes, not a real avatar or transport trace.

```powershell
./Validation~/BindingCost/Run-VariableLength.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated SDK project>' -Plan '<frozen variable plan.json>' -Results '<fresh results>'
```

The variable fixture records producer allocations, private snapshot identity,
capacity and header-derived valid length, exact receiver lengths/ownership,
phase timings and protected packet/header bytes. Receiver mutation must not alter
the other receiver or sender snapshot. Unchanged and warm calls remain in the
evidence. The injected Decoder path excludes receiver GPU/transport work, just
as in the scalar fixture. Snapshot reuse alone does not prove a maximum-latency
or global-GC improvement.

Use an exclusive lease on the dedicated project and GPU. Preserve failed runs,
source receipts and configuration receipts. Results on this host do not establish
actual VRChat, Quest or populated-world performance.
