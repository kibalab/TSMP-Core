# Payload diagnostics and unchanged-state recovery

This harness checks the production Encoder's current-frame diagnostics and the
existing default one-second refresh policy. It does not add retries, ACKs, field
fragmentation or a transport implementation.

```powershell
./Validation~/BudgetRecovery/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated Native project>' -Results '<fresh Native evidence>'
./Validation~/BudgetRecovery/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated Worlds SDK project>' -Results '<fresh VM evidence>' -Vm
```

Install the matching package sources before running. The runner preserves its
source snapshot and product/configuration hashes. Do not run alongside GPU
performance measurements. Initial failures and superseded measurements must be
retained separately.

The Native diagnostic checks call actual `TSMPEncoder.EncodeNow` and inspect its
serialized packets. The SDK check compiles client programs again **after Play
entry**, reads `SerializedProgramAsset.RetrieveProgram()`, binds actual Encoder
and source VMs and records the sixteen product programs' source/bytecode hashes.
Both check dynamic array growth/shrinkage, fields that fit alone versus fields
that cannot fit alone, equal-priority rotation, retained FIFO RPC repeats after a
budget reduction, recovery after budget expansion, and clearing diagnostics on
an early return.

The separate Native refresh experiment passes a monotonic simulated 30 Hz clock
to the unchanged production scheduler/frame builder. Its metadata cache persists
for the whole scenario, matching the Encoder's field identity lifetime. Actual
protected CRC completion and Decoder state/RPC dispatch process emitted bytes.
The experiment changes three 32-byte fields once at 0.5 seconds, then leaves them
unchanged. Their priorities are 100/50/0 and minimum intervals 0.1/0.4/1.5 seconds.
Refresh remains one second. Three burst losses, held images and a new stream with
reused RPC event ID exercise recovery and finite-repeat limits.

The CSV records all 241 simulated observations per case, emitted field masks,
RPC writes/remaining repeats, source frame IDs and receiver contents. Every
nonempty original packet and its protected header are saved before loss is
applied. An independent reader can check the exact byte values and CRCs without
using production serialization helpers.

This is a deterministic scheduler/application test. Injecting bytes into
`CompletePayload` bypasses the header/readback stages and header-based frame
duplicate suppression. Held-image application and `lastStreamId` exercise RPC
deduplication, but do not establish end-to-end reconnect behavior, GPU costs,
live VRChat FPS or OBS/CDN/RTMP/HLS service behavior. No state waiting age is
inferred from the sender's last successful local output time.

## Actual monotonic-clock complement

```powershell
./Validation~/BudgetRecovery/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated Native project>' -Results '<fresh realtime evidence>' -Realtime
python ./Validation~/BudgetRecovery/Analyze-Realtime.py --results '<completed realtime evidence>'
```

This separate eight-and-a-half-second PlayMode loop calls actual `EncodeNow` at
shared30Hz deadline opportunities. Production `GetSendTime` reads
`Time.realtimeSinceStartupAsDouble`; the harness does not inject clock values.
Seven concurrent encoders use their normal persistent bindings/caches, default
one-second refresh, four RPC copies and zero target budget. A no-loss restart
control guarantees the old stream actually records event501 before the new
stream reuses that ID. The short-loss boundary is judged by actual copy times,
since real deadline jitter can change how many copies a0.1-second interval covers.

Per-opportunity receipts include actual source-change, send, successful local
output and apply times, deadline lateness/missed slots, field masks and remaining
RPC copies. Every field's eligibility is checked against the Encoder's actual
`_sendTime` and its preceding successful `Entry.LastSent`, with the real minimum
interval and refresh rules. These stamps establish local scheduling; they are
not remote waiting ages. Packet/header IO occurs after the clocked loop.

The Python analyzer separately reparses raw protected packets/headers, rechecks
CRC and full values, models field eligibility and finite RPC copies, and
reconstructs receiver state from visible originals. GPU Luma4 submissions are
recorded where the production path uses them, but Decoder completion still
receives original byte buffers directly. This measures actual local scheduling
and recovery opportunities without a network, GPU readback or service reconnect.

Cases are created after Play entry, when Editor automatic scene binding rebuilds
are inactive. This preserves each scenario's explicit source/receiver isolation;
the harness does not disable or modify Editor preparation or production code.
