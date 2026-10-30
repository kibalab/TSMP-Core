# Color codec transport measurements, 2026-10-30

Color256 and the shipped RGB16 preset preserved the common payloads in the tested 720p NVENC 6 Mbps files. Color256 also preserved all tested 1440p NVENC 10 Mbps files and all nonblack observations in actual OBS recordings. RGB16 had payload rejections at 1440p 10 Mbps in both FFmpeg and actual OBS. RGB20 passed the original PNG/RGB FFV1 controls but failed every tested H.264 observation, including QP0 conversion controls. Increasing bitrate alone is not an established remedy for RGB20 on this path.

Runtime, shaders, codec options and wire format were not changed. Measurements and documentation were committed on `codex/tsmp-reliability`; no merge or push was performed.

## Method and scope

Windows / RTX 4090 / Unity 2022.3.22f1 / Direct3D11 / Gamma / OBS 31.0.1. Each codec uses its actual shipped prefab and production `TryWriteFrame` raster implementation. Independent pre-raster payloads are identical to the previous Luma4 fixtures: 300 unique source IDs at 30 fps / ten seconds, structured and seeded-random raw-byte values, cycling through 32, 2,495 and 5,375 payload bytes. Both 1280x720 and 2560x1440 are native dimensions, block8/sample4. These are synthetic single-field NetworkFrames, not avatar captures or maximum-capacity tests.

The generator restamps codec ID, symbol mode and actual prefab options with valid protected protocol 0.2 header/payload CRC. Independent header/payload bins and PNG hashes are preserved. The dedicated Native project's relevant Core files match the branch, and codec files match the clean local codec repositories. No Udon or VRChat client ran.

| Codec package | Actual prefab options | Payload bits/block | Calculated 720p / 1440p capacity |
| --- | --- | ---: | ---: |
| Color256 2.0.0 (`a374e163`) | robust=true, localRefine=true | 8 | 13,120 / 55,360 B |
| RGB16 2.0.0 (`3e533b57`) | R4/G4/B4, localRefine=true, radius=2 | **12** | 19,920 / 83,040 B |
| RGB20 2.0.0 (`c7cd6d88`) | R7/G7/B6 | 20 | 32,800 / 138,400 B |

RGB16's shipped preset is 12 bits/block despite the codec name. Header `PayloadSize` limits a single payload to 65,535 B regardless of larger calculated raster capacity. These capacity numbers are arithmetic, not measured throughput or supported avatar counts. The GPU harness's 8,192-byte readback target covers the common 5,375-byte maximum used here.

All restored PNGs are checked by the production Native GPU decoder in strict-CRC, parse-only mode. EXACT requires the trusted frame ID, verified header/payload CRC, expected byte count and complete byte equality with independent source bins. No state or RPC is applied. Color payloads are never evaluated with the Luma4 CPU symbol model. OBS source IDs are associated through the shared grayscale header, then the full payload is evaluated by the actual codec's GPU path.

## Finite file results

Each cell combines structured and random patterns: **exact complete payloads / 600**. All H.264 profiles use 8-bit YUV420, BT.709, explicit range conversion, 30 fps and B0. CBR has a two-second VBV buffer; GOP is 60 at 720p and 30 at 1440p. FFmpeg NVENC uses p4/lookahead0. CRF is a quality setting, so its measured bitrate differs from CBR.

| Native size and encoder | Color256 | RGB16 default | RGB20 |
| --- | ---: | ---: | ---: |
| 720p x264 veryfast CBR 3.5 Mbps, Limited | 362/600 | 306/600 | 0/600 |
| 720p x264 veryfast CBR 6 Mbps, Limited | 585/600 | 556/600 | 0/600 |
| 720p NVENC CBR 3.5 Mbps, Limited | 593/600 | 588/600 | 0/600 |
| 720p NVENC CBR 6 Mbps, Limited | **600/600** | **600/600** | 0/600 |
| 1440p NVENC CBR 10 Mbps, Full | **600/600** | 593/600 | 0/600 |
| 1440p NVENC CBR 10 Mbps, Limited | **600/600** | 588/600 | 0/600 |
| 720p x264 CRF18, Limited | 122/600 | 6/600 | 0/600 |
| 720p x264 CRF28, Limited | 10/600 | 0/600 | 0/600 |
| 720p x264 QP0, Limited | 600/600 | 600/600 | 0/600 |
| 1440p x264 QP0, Full | 600/600 | 600/600 | 0/600 |
| 1440p x264 QP0, Limited | 600/600 | 600/600 | 0/600 |

Every codec passed **1,200/1,200 original PNGs and 1,200/1,200 RGB FFV1 controls**. These controls establish correct raster/reference and RGB-lossless paths for these fixtures. QP0 still includes RGB/YUV420 and range conversion; it is not RGB-lossless. RGB20's QP0 failures show that this conversion-containing path is already insufficient, without establishing which individual matrix, subsampling or rounding operation causes each error.

The file matrix contains 90 profiles / **27,000 observations: 17,509 EXACT and 9,491 REJECTED**. Rejections split into 1,171 header CRC and 8,320 payload CRC failures. Color256 totals 7,672 exact / 1,328 rejected; RGB16 7,437 / 1,563; RGB20 2,400 / 6,600. RGB20's 2,400 exact observations are exclusively the PNG and RGB FFV1 controls. Aggregate counts mix controls and deliberately difficult profiles; use the individual conditions above to assess candidate settings.

Actual video-packet bitrate uses packet bytes over media PTS duration, excluding network overhead. Color256 at nominal NVENC 6 Mbps reached a **9.274 Mbps maximum one-second window**; RGB16 reached 7.040 Mbps. A CBR target is not an instantaneous service cap, and these measurements do not establish compliance with a VRCDN limit.

## Actual isolated OBS recordings

Nine local recordings use a synthetic native-size FFV1 media source, CBR 10,000 kbps, 2560x1440 at 30 fps, NV12 / BT.709, B0, one-second keyframes, and Full or Partial range. Logs confirm effective **p5/hq/qres, lookahead eight frames and AQ**, which differ from the FFmpeg p4 model. No scene scaling or cropping, camera, screen, microphone or desktop-audio source was used. No publishing or service-start request was issued. OBS service-catalog auto-update traffic occurred; this is not a claim of no external network activity.

All 3,657 recorded observations were decoded; 3,641 were associated through valid headers with independent source references. The remaining sixteen are uniformly RGB zero, each near a source-restart transition immediately before source ID 1. The exact internal cause is unconfirmed. API pause/readiness did not certify frozen pixels; several prefixes contain advancing source IDs.

| Codec / source / range | Recorded observations | Exact payloads | Black rejections | Other payload rejections | Unique exact IDs / 300 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Color256 structured Full | 408 | 407 | 1 | 0 | 300 |
| Color256 random Full | 407 | 405 | 2 | 0 | 299 |
| Color256 random Partial | 406 | 404 | 2 | 0 | 300 |
| RGB16 structured Full | 406 | 403 | 2 | 1 | 299 |
| RGB16 random Full | 406 | 401 | 2 | 3 | 298 |
| RGB16 random Partial | 406 | 399 | 2 | 5 | 298 |
| RGB20 structured Full | 406 | 0 | 2 | 404 | 0 |
| RGB20 random Full | 406 | 0 | 1 | 405 | 0 |
| RGB20 random Partial | 406 | 0 | 2 | 404 | 0 |

OBS totals are **2,419 EXACT / 1,238 REJECTED**: 16 black observations, nine RGB16 payload failures and 1,213 RGB20 payload failures. Color256 preserved all 1,216 nonblack observations; RGB16 preserved 1,203/1,212; RGB20 preserved 0/1,213. These repeated-observation counts are distinct from the unique source coverage column.

Recordings include repeated/held frames, so observation counts are not new-update Hz. Header association observed all 300 source IDs in five cases. Color256 random Full, RGB16 random Full, RGB16 structured Full and RGB20 structured Full did not observe ID 300. The location/cause of that omission is unconfirmed; finite-file ending and source scheduling are hypotheses, not confirmed codec or network loss. Exact payload source coverage is separately recorded in `gpu-summary.json` and must not be inferred from header coverage.

Successful helper runs exited owned OBS processes normally with code 0 and reported zero render/output skipped counters. Those counters do not certify source-ID or payload delivery. OBS shutdown logs retain source-release warnings and a 1,932-allocation memory-leak diagnostic in each run; they were not suppressed or treated as TSMP byte failures.

## File packaging and implications

One structured Full recording per codec was copied without video re-encoding into local FLV and MPEG-TS HLS files. Original, FLV and HLS decoded RGB hashes and frame order were identical: Color256 408 frames, RGB16 406, RGB20 406. Packaging preserved original black frames and any original data corruption too. This tests file packaging, not RTMP publishing, RTSP/TCP, an HTTP server, CDN processing or HLS player buffering.

For the measured common loads, 720p NVENC 6 Mbps is a further-test candidate for Color256 and the default RGB16 preset; 1440p NVENC 10 Mbps is a candidate for Color256. Finite passes do not certify arbitrary content or service settings. RGB16's actual OBS failures need correction or more conservative settings before claiming reliable 1440p support. RGB20 should not be described as compatible with this ordinary H.264/NV12 path on the present evidence; its supported path needs an explicit limitation or a codec/transport redesign. The existing Luma4 remains the robustness comparison, rather than evidence that a new robust codec is required.

Service-level recommendations still require an authorized dedicated VRCDN channel and actual ingestion/playback validation. Spout/capture transforms, server re-encoding, live RTMP/RTSP/HLS delivery, reconnect behavior, source age, applied update rate, end-to-end latency, VRChat/Udon and CPU/GPU performance were not measured here. No local relay-server launch was retried.

## Evidence and completion

Evidence root: `F:/Unity/TSMP/Validation-Results/codec-transport-20261030`.

- `fixtures`, `fixture-audit.json`: 3,600 production-raster source PNGs, independent bins, source hashes and cross-codec byte equality.
- `matrix`, `matrix-gpu-manifest.json`, `gpu-matrix`: 78 encoded sequences, 12 PNG controls, exact commands/process exits, packet/frame PTS, restored PNGs and GPU verdicts.
- `obs`, `obs-analysis`, `obs-gpu-manifest.json`, `gpu-obs`: nine recordings, API journals, source/header associations and full GPU checks.
- `encoder-audit.json`, `environment/package-audit.json`, `obs-audit.json`, `obs-source-audit.json`: independent configuration, implementation and source-progress audits.
- `gpu-summary.json`, `gpu-summary.csv`, `gpu-verdict-audit.json`: final grouped results, payload-size breakdowns, exact source coverage and verdict-semantic audit.
- `remux`, `remux-summary.json`: local FLV/HLS frame-hash comparisons.

Both GPU runs must retain FAIL / nonzero command exit because measured frames were rejected. Python `MEASURED`, fixture generation PASS and audit PASS describe their respective execution/reference checks; they do not convert failing transport profiles into successful ones. Original evidence is preserved. Measurement helper smoke checks additionally verified 300 FFV1 RGB frames and color header-only association, with payload accuracy left to the production GPU.

Final independent verdict audit covers **99 profiles / 30,657 observations: 19,928 EXACT and 10,729 REJECTED**. Rejection stages are 9,542 payload CRC, 1,171 header CRC and 16 header/signal failures. WRONG, accepted-CORRUPT, missing mapped PNGs, timeouts and input errors are all zero. Missing mapped PNGs are a harness input metric and do not negate the separately reported unobserved source IDs. No measurement-owned Unity, OBS, FFmpeg or relay process remained running at completion; the unrelated pre-existing Unity process was left alone.
