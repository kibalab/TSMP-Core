# Fixed payload budget delivery experiment

This validation-only experiment measures the practical tradeoff of the existing
`TSMPEncoder.targetPayloadBytes`. It changes no product policy, format, codec or
default. Use an isolated SDK-free Unity 2022.3 project with the current Core,
Luma4 and Color256 packages. Do not run other owned GPU/encoding benchmarks at
the same time.

The source is 120 native 1280x720 media frames at 30 fps, block8/sample4. Actual
production Encoders generate six sequences: Luma4/Color256 times targets
0/1024/2304. The immutable semantic workload is identical:

| Field | Size | Changes | Priority |
| --- | ---: | ---: | ---: |
| critical | 64 B | every frame | 100 |
| medium | 512 B | every 3 frames | 50 |
| bulk | 2048 B | every 6 frames | 0 |

Each value embeds its generation ordinal and deterministic xorshift bytes.
Send-on-change is used with refresh disabled explicitly for this experiment;
wall-clock refresh cannot determine an offline media schedule. A named RPC is
enqueued every fifteen source frames, with the existing four-repeat policy and
the generation ordinal as its event ID. All targets receive exactly the same
source value changes and event schedule. A 1024 B target cannot carry the bulk
field; 2304 B can carry that complete field with the critical field, while
priority and competition with medium updates can defer it. No fragmentation or
automatic target selection is added.

Source packet/header binaries are captured from the actual Encoder output
before video compression, together with PNGs. Their reference origin is the
Encoder, not an independent alternative packet serializer or a color model.
The current GPU Luma4 option remains enabled; source rows record whether it
actually ran and the allocated GPU symbol/upload resource sizes. Color256 uses
its current buffered Native writer. ReadPixels flushes output after EncodeNow
timing, so that timing is CPU submission work rather than total GPU encode cost.
Python independently verifies all source field bytes/generations, RPCs, CRCs and
hashes. Encoder timestamps retain their normal host-clock behavior; media age
uses the explicit source ordinal and never subtracts clocks from different hosts.

`Measure.py encode` makes six x264 veryfast CBR 3.5 Mbps / NV12-compatible YUV420,
BT.709 Limited sequences with B0 and a two-second VBV. Native dimensions are
preserved. Original PNG controls and restored video PNGs are replayed through
the actual production GPU Decoder in application mode, with strict CRC and
unchanged prediction/combined/two-slot/retry settings. A thirteenth PNG-control
profile holds source44 during media frames45..56, deliberately covering every
repeat of event46. This is a deterministic missing-source/held-image control,
not a CDN or reconnect simulation.

Accepted payloads must match complete original source binaries; all actual field
bytes and event IDs are checked. Accepted raw bins and per-media-frame receiver
generations are retained for an independent Python state/RPC application model.
Rejected/duplicate frames must leave application state and events unchanged.
The analyzer reports unique applied updates/bytes, fields never received,
current-state coverage, source age, lag behind the latest desired generation,
and original event coverage/delay. An unseen field has no numeric age; it is
reported explicitly rather than treated as zero delay.

`EncodeNow` CPU wall time excludes fixture-value creation, ReadPixels, PNG and
file I/O. Decoder submission time covers `DecodeNow`; completion wall time adds
real asynchronous GPU callbacks/waits and host scheduling. It is not isolated
callback CPU time, GPU-pass timing, streamed playback latency or VRChat FPS.
First ten observations per sequence are marked warm and excluded from timing.
Sequential single-machine measurements do not establish statistical significance.

```powershell
./Validation~/BudgetDelivery/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<Native>' -Phase Generate -Results '<new fixtures>'
python ./Validation~/BudgetDelivery/Measure.py encode --fixtures '<fixtures>' --output '<new encoded>'
./Validation~/BudgetDelivery/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<Native>' -Phase Replay -Plan '<encoded>/replay-plan.json' -Results '<new replay>'
python ./Validation~/BudgetDelivery/Measure.py analyze --fixtures '<fixtures>' --replay '<replay>' --output '<new summary.json>'
```

Preserve each run's source hashes, input plan, failure logs and output binaries.
Do not reuse nonempty output folders. No live OBS publishing, VRCDN, RTMP/RTSP
service, HLS playback, actual VRChat or Udon performance is established here.
