# RPC burst and queue workload

This validation-only workload runs freshly compiled client Encoder, Luma4 codec,
Decoder and receiver programs in the dedicated Worlds SDK Editor project, with
actual VRC asynchronous GPU readbacks. It does not inject decoded payloads or
replace the production clock. It keeps the default four RPC copies, one-second
state refresh, payload CRC, two recovery attempts and normal frame clearing.

```powershell
python ./Validation~/RpcBurst/Generate-Plan.py --output '<fresh plan.json>'
./Validation~/RpcBurst/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated VRC project>' -Plan '<plan.json>' -Results '<fresh directory>'
python ./Validation~/RpcBurst/Analyze-Results.py --results '<completed directory>'
```

Run exclusively: do not overlap another owned Unity/GPU/encoder workload. Existing
unrelated applications are not stopped. The runner only copies this harness to
`Assets/Validation/RpcBurst`; it changes no product settings or package sources.
The plan is frozen before results. Eight cases measure forty seconds at source
30Hz/render target90Hz, plus one-second pre-observation waits and observation
tails. Bursts1/8/32, queue overflow33, sustained inputs2/10Hz, mixed changing
integer state, and a budget29→256 recovery case are retained. The sustained10Hz
input stops after four seconds and the eight-second case allows the queue to
drain. Accepted/rejected calls and missed source deadlines are recorded.

Every successful publication retains its protected header and NetworkFrame.
Payload length comes from protected header offset42: the normal default
`clearAfterEncode` clears public diagnostics after output. Every accepted event
must appear exactly four times on the wire, execute exactly once in FIFO ID
order, and drain from the queue. Rejected event IDs must never appear. The Python
analyzer independently reparses message/argument boundaries, event IDs, header
and payload CRCs, and reconstructs queue/repeat changes from enqueue/publication
timestamps. It allows multiple distinct FIFO RPCs per packet for a separately
justified candidate, without weakening per-event copy and application checks.

The mixed cases update an integer marker on each send opportunity. Every received
positive marker must correspond to a published value and preserve increasing
order. Latency and per-render state age use the actual pre-EncodeNow timestamp.
The marker0 preflight is a correctness control and is excluded from performance
latency/age: its host dictionary timestamp is established after completion.
Nonmixed cases keep marker0 unchanged and exercise default periodic refresh.

Timed outer Encoder and Decoder Interpret calls include nested codec and receiver
work once. Receiver event/marker arrays and an event-ID read are validation work
inside that VM scope. Host packet parsing, recording and file writes occur
outside those timers, but affect scheduling, achieved rendering and sender
deadlines. VM timing is not total host CPU/GPU execution or live VRChat FPS.
The analyzer filters raw events by their own actual start times and uses frame
CPU sums over the union of measured frame and VM-event frame IDs. Receiver
timestamps use Unity float-clock precision; enqueue/publication clocks use double.

Source hashes, actual loaded program hashes and project configuration receipts
are retained. Client compilation is repeated after entering Play Mode. A failure
in the frame pump is forwarded to the Editor coroutine so the first exception
fails the run. Keep failed preliminary directories rather than overwriting them.
No OBS/CDN/actual-client, packet-loss guarantee, ACK, queue-batching feature or
runtime policy is implied by this measurement harness.
