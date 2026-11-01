# Fixed-budget delivery results, 2026-10-31

The existing budget can trade packet loss for delayed or unsent application data.
In this finite workload, a 2304 B cap retained all desired updates but delayed
bulk state; a 1024 B cap achieved perfect packet acceptance while sending none
of the bulk updates. **The results do not justify changing the default zero
target or adding automatic budget selection.** No product policy was changed.

## Actual applied delivery

Core runtime `b5f35755`, after the structural-preflight fix and Sparse rollback.
Unity 2022.3.22f1 / Native Editor / D3D11 / Gamma / RTX 4090. Actual production
Encoders, codec raster writers and GPU Decoders applied three fields and named
RPCs. Native GPU Luma4 actually ran on all 360 Luma4 source frames; its symbol
textures and upload-buffer capacities are recorded. Color256 used its current
buffered Native raster writer.

The frozen four-second workload has 120 critical changes of 64 B, forty medium
changes of 512 B, twenty bulk changes of 2048 B, and eight events with four
repeats each. Fields use priorities 100/50/0, on-change sending and explicit
refresh zero for deterministic offline scheduling. Total desired unique state
is **69,120 B**, excluding message metadata and repeated events.

All six pristine GPU controls pass 120/120. The six x264 veryfast CBR 3.5 Mbps
BT.709 Limited / YUV420 / B0 videos produce these actual application results:

| Codec | Target | Exact frames | Critical /120 | Medium /40 | Bulk /20 | Unique state bytes applied | Events /8 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Luma4 | 0 | 120/120 | 120 | 40 | 20 | 69,120 | 8 |
| Luma4 | 1024 | 120/120 | 120 | 40 | **0** | **28,160** | 8 |
| Luma4 | 2304 | 120/120 | 120 | 40 | 20 | 69,120 | 8 |
| Color256 | 0 | 119/120 | 119 | 39 | 20 | 68,544 | 8 |
| Color256 | 1024 | 120/120 | 120 | 40 | **0** | **28,160** | 8 |
| Color256 | 2304 | 120/120 | 120 | 40 | 20 | 69,120 | 8 |

The 1024 B profile applies **40.74%** of desired unique state bytes, despite
100% acceptance of its produced packets. Its 2048 B atomic bulk field is never
received in all 120 observations. This known size limitation is not video loss;
the age of an unseen field is undefined and explicitly reported as such.

The 2304 B profile allows the complete bulk field plus critical state. When a
medium update competes for space, the existing priority scheduler defers bulk
until the next source frame. Bulk updates are all delivered, but current desired
bulk state is present on 100/120 media observations versus 120/120 at target zero.
Its first observation has no bulk value. Bulk source-age p95 rises from
**166.67 to 200.00 ms** for both codecs. The age includes the intentional six-frame
bulk update cadence; it is separate from asynchronous Decoder processing delay.

Color256's one zero-target loss is source22, a 608 B protected payload rejected
after both Refine retries. It loses one critical and one medium version, totaling
576 state bytes. The 2304 B sequence recovers one more complete observation than
zero in this sample. That single observation is insufficient to establish a
general safe cap. Luma4 already retains every produced packet at target zero.

Actual outgoing payload ranges are **89–2704 B**, **89–649 B** and **89–2185 B**
for targets 0/1024/2304. Zero and 2304 each produce 73,852 network-payload bytes
over the sequence; 1024 produces 32,752 B because it omits bulk. CBR video packet
rates are 3.326798–3.326836 Mbps across all six sequences, excluding container
and network overhead. Smaller packets are not a measured video bitrate saving
under this fixed-CBR configuration.

## Cost observations

First ten source/replay observations are excluded from timing, leaving 110 per
profile. `EncodeNow` CPU wall time excludes value construction, PNG/file I/O and
the later ReadPixels/GPU completion. Decode submission likewise excludes image
loading; completion wall time includes actual asynchronous callbacks, waits and
host scheduling. It is not isolated callback CPU time or live playback latency.

| Native source | Target | Encode CPU mean / p95 | CBR Decode submission mean / p95 | CBR completion wall p95 |
| --- | ---: | ---: | ---: | ---: |
| Luma4, actual GPU writer | 0 | 0.194 / 0.236 ms | 0.285 / 0.339 ms | 20.869 ms |
| Luma4, actual GPU writer | 1024 | 0.199 / 0.247 ms | 0.312 / 0.408 ms | 20.804 ms |
| Luma4, actual GPU writer | 2304 | 0.193 / 0.236 ms | 0.268 / 0.321 ms | 22.685 ms |
| Color256, buffered writer | 0 | 1.606 / 1.865 ms | 0.283 / 0.367 ms | 22.709 ms |
| Color256, buffered writer | 1024 | 1.525 / 1.678 ms | 0.280 / 0.340 ms | 20.066 ms |
| Color256, buffered writer | 2304 | 1.620 / 1.883 ms | 0.282 / 0.347 ms | 22.588 ms |

These sequential single-machine samples establish no statistical significance
or whole-frame/VRChat FPS improvement. In particular, lower Encoder CPU cost at
1024 is accompanied by complete bulk-field starvation; it is not a useful
performance win for applications requiring that state. Other source schedules,
field sizes, rates and encoders require separate measurements.

## Deterministic held-image control

One additional Color256/2304 pristine replay holds source44 during media45..56.
It yields exactly **108 exact observations and 12 duplicate skips**, no corrupt
application, 108/120 critical versions, 36/40 medium versions, 18/20 bulk versions,
and **seven of eight events**. Every repeat of event46 lies in the held interval,
so that event never arrives. No duplicate event is applied.

Critical source age reaches 400 ms; bulk reaches 600 ms before a later changed
bulk version arrives. This demonstrates the existing finite-repeat/on-change
contract with refresh explicitly disabled. It is not a CDN pause/reconnect test
and does not measure the default one-second periodic refresh. Zero media-frame
RPC delay in ordinary profiles means first transmitted-ordinal reception; it
does not mean zero GPU, network or video latency.

## Provenance and limits

Evidence: `F:/Unity/TSMP/Validation-Results/followup-performance-20261031/budget-delivery`.

- Policy was frozen before any generated output. Earlier pre-output proposals
  and source snapshots are retained: the GPU-default revision and cached-CRC
  validation correction preceded source generation. No result-driven settings
  or input changes were made.
- `fixtures/result.txt`: PASS, 2,940 assertions and 720 actual source frames.
  Binary references come from actual Encoder output before compression; they are
  not reconstructed from received images or an alternative packet serializer.
- `replay/result.txt`: PASS, 13,323 assertions and 1,560 GPU observations.
  Only Color256 zero-target source22 rejects; all controls meet required outcomes.
- `summary.json` and `accepted-byte-audit.json`: independent PASS, **1,547**
  accepted complete byte comparisons, **318** before/after source comparisons,
  source/header/video/decoded-image hashes, independent desired field bytes,
  stable-key hashes, receiver generation/state/event model and pristine guards.
- Shared StreamId 20261031, starting frame one, geometry, codec options and source
  field/event schedule are verified. Normal Encoder host timestamps differ
  between runs, so protected headers are not bit-identical paired controls.
- `configuration-after.json`: all **23** captured Native manifest/lock/project
  configuration files are preserved. Product runtime sources remain unchanged.

No Udon performance, actual VRChat, OBS publishing, RTMP/RTSP/HLS service,
network delay, avatar capacity or live transport support is established here.
No merge, push, release, default change, fragmentation or adaptive policy was made.
