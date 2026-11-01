# Measured RPC queue workloads, 2026-11-01

The existing FIFO correctly retains and delivers the tested RPCs, but its
one-event-per-frame/four-copy policy creates substantial burst delay. Thirty-two
simultaneous events take **4.132 seconds** from the first event's first send to
the last event's first send. All accepted events execute exactly once. This is
local SDK Editor evidence, not network/CDN latency or a delivery guarantee.

## Frozen workload and evidence

Evidence:
`F:/Unity/TSMP/Validation-Results/workload-followup-20261101/rpc-burst`.
`plan-v1.json` was generated before output; SHA256
`c0d2c47f925e34fcca6dd549dc007294cd8f31e2a7a2235d0d192a8cb8462648`.
The eight cases measure forty seconds, using actual production clocks at source
30Hz/render target90Hz, default four repeats and one-second state refresh.

`baseline-corrected` passes **11,107 assertions**. Fresh client compilation after
Play entry is followed by actual Encoder→GPU Luma4→VRC GPU Decoder→receiver VM
execution. All **159 accepted events execute once in FIFO order**, and each has
exactly four protected on-wire copies (**636 copies**). One intentional queue-full
call is rejected and its event ID never appears on the wire. All measured source
deadlines are met. This does not test network loss or prove infinite queue headroom.

The independent Python audit passes **813 protected packet/header pairs**,
enqueue/queue/repeat transitions, 159 event application rows, 367 state rows,
3,960 frame rows and 9,271 top-level VM events. Product sources remain identical
across all **392 source receipts**, with **23 project configuration files**
unchanged. Actual loaded bytecode receipts include Core Encoder
`90c142e388c90447ec92e9a670c11544034b78ab79a53bf5d13d90c1bf308b8c`
and Decoder
`1cf211baab412d4d22c80396f3a3852c5ece8e29ebbd9c4fd8b0847c9fc91f42`.

## Results

| Workload | Accepted / executed | Enqueue→apply mean / p95 / maximum | Maximum queue | Frame VM p95 |
| --- | ---: | ---: | ---: | ---: |
| Burst1 | 1 / 1 | 37.42 / 37.42 / 37.42 ms | 1 | 1.115 ms |
| Burst8 | 8 / 8 | 491.52 / 954.87 / 954.87 ms | 8 | 1.515 ms |
| Burst32 | 32 / 32 | 2,088.23 / 4,021.39 / 4,176.73 ms | 32 | 1.542 ms |
| Overflow33 | 32 / 32; one rejected | 2,087.25 / 4,021.38 / 4,176.73 ms | 32 | 1.556 ms |
| 2Hz input for3s | 6 / 6 | 20.69 / 22.71 / 22.71 ms | 1 | 1.407 ms |
| 10Hz input for4s | 40 / 40 | 667.81 / 1,245.28 / 1,333.17 ms | 11 | 1.717 ms |
| Burst32 + changing state | 32 / 32 | 2,088.66 / 4,021.45 / 4,154.78 ms | 32 | 1.704 ms |
| Burst8 + reduced budget | 8 / 8 | 1,682.01 / 2,144.16 / 2,144.16 ms | 8 | 1.728 ms |

Burst32 last enqueue→first-send is **4,132.190ms**. The measured first-to-last
first-send span is **4,132.142ms**, agreeing with the earlier 31×4/30 calculation
of4,133.333ms to within real scheduling phase. The two values use different
time origins. At2Hz the queue remains at one; at10Hz input exceeds the current
calculated7.5-events/second service rate. The four-second input ends, allowing
the queue to drain before the eight-second case ends. This is not a test of
sustained10Hz admission until eventual queue overflow.

The mixed32 case receives all **180/180 changing positive state markers**.
State application latency p95 is **22.730ms**, maximum44.702ms. The budget case
enqueues at256bytes, holds budget29 until actual start+1.2s, then restores256.
Its **36 blocked send opportunities** preserve every queue item and all four
remaining repeats while the29-byte integer-state packet continues. All150/150
positive state markers arrive; state latency p95 is22.561ms. Increasing the
budget resumes the retained events. No bypass, automatic fragmentation, queue
reordering or policy change was introduced.

## Interpretation and next step

The demonstrated cost is event waiting time, not excessive VM frame CPU for this
small workload. Batching several FIFO events into one packet could reduce that
waiting time, but changes how much packet space RPCs take before state fields.
It also requires correct commit accounting for mixed per-event repeat counts,
capture-time enqueue, failed output, queue compaction and the bounded dedup cache.
There is no default batching change in this work. Repeated-count increases would
reduce the existing service rate further.

Use state fields for persistent values that must recover through periodic
refresh. The separate actual-clock loss experiment already shows that100ms can
cover all four event copies. These intact local burst tests do not change that
finite-repeat delivery limit.

## Retained harness failure and precision

`baseline-initial` is preserved as **FAIL**, with no accepted performance claim.
Its observer read public `payloadBytes` after normal `clearAfterEncode` reset it
to zero, then failed in its own packet parser. Exceptions from the MonoBehaviour
frame pump were initially only logged, so the missing observation later failed
the repeat-count guard. The corrected observer takes length from the actual
protected header offset42 and propagates the first pump exception to the
validation coroutine. Product sources, default frame clearing and the plan are
unchanged to obtain the pass.

The corrected run uses .NET's default seven-significant-digit CSV float display
for receiver timestamps. Its double latency column retains the calculation from
the original float. The independent analyzer reconstructs the original IEEE754
float from enqueue/send+latency, checks that exact arithmetic, and constrains its
display difference to the known half-unit decimal rounding bound. The current
runner source prints float/double values with round-trip format for later runs;
that formatting-only source update is not claimed to have run in
`baseline-corrected`. Preflight marker0 is retained as a control and excluded
from latency/age performance; its dictionary timestamp is set after preflight.

VM CPU includes the validation receiver's event-ID read and recording arrays.
Packet parsing/files are outside timed VM calls but can affect frame scheduling.
Per-frame CPU sums unique top-level Encoder/Decoder events and counts idle
measured frames; it is not total process CPU or GPU duration. The fixture is a
lightweight Gamma/D3D11 Editor scene on i9-13900K/RTX4090 and does not establish
populated-world FPS, Quest, OBS or RTMP/HLS/CDN behavior.
