# Current Color256 encoder attribution and polling

Validation-only follow-up to `SustainedCost`, using the optimized Color256 source.
The driver is a renamed frozen copy of the proven sustained harness, extended
with explicit polling frequency, finite four-copy RPCs, stream restart, and nested
codec VM timing. It changes no product source or default.

```powershell
python ./Validation~/Color256Followup/Generate-Plan.py --output '<fresh plan.json>'
./Validation~/Color256Followup/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated Worlds SDK project>' -Plan '<plan.json>' -Results '<fresh results>'
python ./Validation~/Color256Followup/Analyze-Results.py --results '<completed results>'
```

The preselected fifteen cases measure144 seconds. Three pairs alternate
every-render and60Hz polling at source30/render90. Additional guards cover30Hz
polling with source jitter, source60/render90 including intentional under-polling,
render60, a held source and a new stream reusing event IDs. Each case uses new VM
instances, two warm seconds and a one-second ongoing publication tail. The entire
matrix runs in one fresh Editor process; the three pairs are not independent
fresh-process repetitions.

`pollHz` is independent of `sourceHz`; labels alone never control cadence. The
older sustained harness's validation-only `poll30` schedule follows its source
frequency and must not be used as a30Hz control when sourceHz is60.

All fields carry ID-seeded bytes and every applied field is fully checked.
Published IDs, sender misses, received IDs and source age remain separate.
The sender queues real client VM RPCs with positive event IDs at preselected
intervals and the Decoder dispatches them through its protected raster/GPU path.
Each receive callback records the currently decoded stream/event ID. Missing
or duplicate events are an adoption guard. A hold suppresses publications in the
local source schedule; it does not emulate lost compressed network packets.
The frozen RPC cadence places events before and after the held interval, not
inside it. This does not verify queueing an RPC during the hold.

`codec-stages.csv` records nonoverlapping nested codec custom-event intervals
inside actual Encoder and Decoder VM dispatch. The code uses the VM wrapper,
not an instrumented product overlay. Outer Encoder time includes codec work
once; measured nested observer costs are recorded and subtracted from the outer
interval. The remaining time combines binding/serialization/change comparison,
CRC, static pixels/header, bridge calls, texture upload and other work. It does
not establish separate timings for those private stages, GPU execution time,
or a predicted gain from a new GPU writer. Adjacent clock reads and a5ms host
sleep positive control are retained separately.

Each run freshly compiles client programs after entering Play Mode, records
source/program hashes and preserves project configuration hashes. Failed runs
remain in fresh separate result directories. The runner refuses an already-open
dedicated project; obtain the shared validation/GPU lease before launching.
PASS means the measurement and byte/order checks are valid, not that every
polling schedule receives every publication or is safe as a product default.
No actual video-arrival signal, live VRChat world, OBS or CDN is tested.

## Codec-observer calibration

`Generate-Calibration-Plan.py` freezes four8-second cases in ON/OFF/OFF/ON order.
An explicit `disableCodecTiming` case option supplies `role:null` to the codec
VM constructor, removing its timing wrapper entirely. Outer Encoder/Decoder and
receiver byte checks remain active. The analyzer permits absent stages only in
these explicitly disabled cases and reports attribution as unavailable there.

The calibration follows the separately validated binding-cache fix and uses its
final Core candidate. It is a same-current-source observer comparison and a real
GPU5KB/RPC integration confirmation. Its Core source differs from the initial
polling run, so the two runs must not be pooled or used to explain a historical
absolute timing difference. Both runs retain exact independent source, bytecode,
configuration and executed-harness receipts.
