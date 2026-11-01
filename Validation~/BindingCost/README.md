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

Use an exclusive lease on the dedicated project and GPU. Preserve failed runs,
source receipts and configuration receipts. Results on this host do not establish
actual VRChat, Quest or populated-world performance.
