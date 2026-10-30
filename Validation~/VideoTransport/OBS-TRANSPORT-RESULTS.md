# OBS and CBR measurements, 2026-10-30

The existing Luma4 codec was measured without runtime, shader or wire-format changes. Controlled NVENC profiles preserved complete payloads in these samples. Software x264 at 3.5 Mbps lost some frames. Actual OBS recordings additionally exposed source-restart black frames and incomplete observation of the final source ID. Byte accuracy and source progress are separate results.

## Method and scope

Windows, RTX 4090, OBS 31.0.1, Unity 2022.3.22f1 / Direct3D11 / Gamma. Four synthetic fixtures cover 720p/1440p and structured/seeded random raw-byte values. Each has 300 unique source IDs at 30 fps / ten seconds, cycling through 32, 2,495 and 5,375 payload bytes. These are synthetic byte loads, not captured avatars. Independent pre-raster header/payload bins are preserved. All 1,200 original PNGs passed the actual Native GPU decoder.

Measurements include eighteen finite FFmpeg encoder profiles, three actual isolated OBS recordings using a native-size lossless FFV1 media source, and local FLV/HLS file remuxing. Restored PNGs were checked by the production GPU decoder in strict-CRC parse-only mode, comparing complete payloads against independent raw bins. No state or RPC was applied.

**Not measured:** Spout/camera capture, RTMP publishing, RTSP/TCP delivery, HTTP/CDN behavior, service-enforced encoder settings, live HLS buffering, network delay, reconnect recovery, Udon or VRChat clients. The automated-only restriction remains in effect, and no dedicated external test channel was provided.

## Encoder results: authoritative GPU verdicts

| Native dimensions | Encoder/configuration | Structured exact | Random exact |
| --- | --- | ---: | ---: |
| 1280×720 | x264 veryfast, CBR 3.5 Mbps | 295/300 | 290/300 |
| 1280×720 | x264 veryfast, CBR 6 Mbps | 300/300 | 300/300 |
| 1280×720 | NVENC p4, CBR 3.5 Mbps | 300/300 | 300/300 |
| 1280×720 | NVENC p4, CBR 6 Mbps | 300/300 | 300/300 |
| 2560×1440 | NVENC p4, CBR 10 Mbps, Full | 300/300 | 300/300 |
| 2560×1440 | NVENC p4, CBR 10 Mbps, Limited | 300/300 | 300/300 |

All six x264 QP0 conversion-control sequences passed 300/300. Including four PNG controls, the matrix contains **6,600 final GPU observations: 6,585 exact / 15 rejected**. Two rejected frames failed header CRC and thirteen failed payload CRC. No accepted-corrupt or wrong frame occurred. `MEASURED` in Python output means execution completed, not lossless transport; failed profiles remain failed.

Profiles use 30 fps, B-frames 0, a two-second VBV buffer, BT.709 and explicit Full/Limited conversion. The 720p GOP is 60 frames; the 1440p GOP is 30. Successful finite samples are candidates for further testing, not universal support certification.

Video-packet bitrate is packet bytes divided by media PTS duration, excluding network overhead. The 720p NVENC 6 Mbps random case averaged **6.186 Mbps**, with a maximum one-second media-time window of **8.133 Mbps**. A nominal CBR setting does not establish compliance with a strict instantaneous service cap. Neither on-wire RTMP rate nor VRCDN's limit enforcement was measured.

## Actual OBS recordings

Saved user settings selected NVENC H.264 / CBR 10,000 kbps / 2560×1440 / 30 fps / keyframe interval one second / B0 / NV12 / BT.709 / Full. Private recordings recreated those explicit fields, plus one Partial-range comparison. Logs confirm effective **p5, quarter-resolution multipass and eight-frame lookahead** defaults. These differ from the FFmpeg p4/lookahead-0 model. Recording did not apply streaming-service restrictions.

| Recording | Observed frames | Exact payloads | Rejected black frames | Unique exact source IDs |
| --- | ---: | ---: | ---: | ---: |
| Structured, Full | 920 | 919 | 1 | 300/300 |
| Random, Full | 406 | 404 | 2 | 299/300 |
| Structured, Partial | 406 | 404 | 2 | 299/300 |

All **1,727 nonblack TSMP observations** matched source payloads in both the CPU model and GPU decoder. The other five observations are uniformly RGB zero, without a header/calibration signal. They occur near source-restart transitions at recording PTS 17.633 seconds, or 0.500/0.533 seconds. They remain REJECTED. The precise internal cause was not instrumented.

Preparation requested pause and seek, but API state did not prove frozen pixels. Random/Partial recordings show changing IDs before restart. API readiness and pixel readiness are different contracts. Original journals and summaries remain preserved; earlier wording implying a guaranteed paused-first-frame prefix is superseded by this audit.

Random/Full and Structured/Partial did not observe source ID 300. The location/cause of that omission is unconfirmed; finite-file ending and source scheduling are hypotheses. OBS reported zero render/output skipped frames, which does not establish complete source-ID delivery. Repeated/held frames are not new updates. Media PTS, cursor, FPS and request timestamps are not end-to-end network latency measurements.

Successful helper runs stopped recording and exited owned OBS processes normally. Only a synthetic FFV1 source was present; no camera, screen, microphone or desktop-audio source was added. Original OBS setting values compared with the initial snapshot remained the same; no pre-run byte hash existed, so complete byte-level immutability is not claimed. User service keys were not read and no public stream was started.

## Packaging and numerical-model limits

One OBS recording was remuxed without re-encoding into FLV and MPEG-TS HLS files. Both retained the original **920 decoded RGB frame hashes and sequence exactly**. This establishes local file-packaging preservation for the sample, not RTMP/RTSP/HTTP/CDN delivery or buffering behavior.

Of 7,132 encoded/OBS observations compared with the CPU model, pass/reject classification differed on one: 720p random / x264 3.5 Mbps / source ID 22. The CPU accepted it; the GPU rejected its payload. A symbol lies exactly on the modeled calibration midpoint. Some float32 arithmetic reproduces the GPU CRC, consistent with rounding near the boundary; actual shader arithmetic was not captured. Seven payload-computed CRCs differed in that profile; six were already rejected by both implementations. GPU verdicts are authoritative. No model/runtime change was made to turn failures green.

## Evidence and validation history

Root: `F:/Unity/TSMP/Validation-Results/obs-transport-20261030`.

- `fixtures`, `matrix`: source bins/PNGs, hashes, commands, exits, encoder videos, decoded PNGs, ffprobe PTS/packet/frame data and CPU diagnostics.
- `gpu-matrix-720p`, `gpu-1440p-structured-final`, and random/OBS profiles in `gpu-1440p-and-obs`: selected final GPU results.
- `obs`, `obs-analysis`, `remux-obs-full`: recordings, API journals, source associations, GPU manifests and frame-hash comparisons.
- `final-measurement-audit.json`, `encoder-stats.json`, `obs-actual-audit.json`: independent count, reference, source and scope audits.

Final selected results contain **25 profiles / 8,332 observations: 8,312 exact / 20 rejected**. The 1,200 original controls are included in the matrix, not added again. GPU `MISSING=0` means no expected PNG mapping is absent; it does not negate the two unobserved unique OBS source IDs.

Initial OBS readiness failures (WebSocket 207 and unloaded source dimensions zero) were fixed with bounded polling. An initial 1440p structured GPU run omitted its raw-bin reference directory; its 1,500 INPUT_ERROR rows are preserved and superseded by the focused corrected-reference run. The encoder launch source and later raw-reference manifest fix are both preserved by hash; encoding/model logic was unchanged. Runtime changes are zero; all 111 checked Native Core source/shader/assembly files match the branch.

## Remaining measurement

Automatic approval review rejected the local MediaMTX relay launch with “blocked by policy”; no detailed reason was supplied and no alternate relay launch was attempted. Isolated OBS recording and local file packaging were the safe alternative. Actual VRCDN and live RTSP/HLS delay remain unverified.

Further acceptance testing needs an authorized dedicated channel and actual service constraints. Measure source progress, signal gaps, payload correctness and receiver delay separately. Current results support testing the existing Luma4 profiles first, without adding a new codec.
