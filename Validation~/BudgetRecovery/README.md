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
