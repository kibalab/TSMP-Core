# Offline video roundtrip

See [OBS-TRANSPORT-RESULTS.md](OBS-TRANSPORT-RESULTS.md) for the 720p/1440p CBR matrix, actual isolated OBS recordings, GPU checks and local FLV/HLS file-packaging results. Live relay/CDN measurements are separate.

See [CODEC-TRANSPORT-RESULTS.md](CODEC-TRANSPORT-RESULTS.md) for the same-load comparison of the shipped Color256, RGB16 and RGB20 codecs, including RGB-lossless controls and actual OBS recordings.

See [CODEC-OPTIMIZATION-REVIEW.md](CODEC-OPTIMIZATION-REVIEW.md) for the subsequent source/evidence review: Native color-buffer reuse, existing profile efficiency, useful-data metrics and gated shader/cadence candidates. It adds no runtime changes or new performance claims.

The [implemented performance results](../ColorRaster/RESULTS.md) record Native raster reuse and RGB16 refinement improvements, production-path verification and measured limitations.

The subsequent [receiver robustness results](../RefineRobustness/RESULTS.md) measure CRC-gated RGB16/Color256 recovery on the same videos and a new seeded input, correct low-bit RGB16 byte assembly, and record GPU costs and the decision to leave header recovery off by default.

## Measuring other codecs

`Run-CodecTransportFixture.ps1 -UnityEditor <exe> -Project <Native-project> -Plan <json> -Results <new-directory>` calls each production prefab's `TryWriteFrame`. Its plan contains `fixtures`, each with a unique `name`, `codecPrefabPath` and `sourceFixtureDirectory`. The source is a completed fixture from `Generate-Transport-Fixture.py`. Payload bins are reused unchanged; codec ID, symbol mode and actual prefab options are stamped into the header with valid CRC. This preserves equal byte loads across codecs. Generated PNG/header/payload hashes and package source hashes are recorded. Generation completion does not certify raster or transport accuracy.

`Measure-CodecStreaming.py --fixture <generated-fixture> --codec-prefab-path Packages/...prefab --results <new-directory> --ffmpeg <exe> --ffprobe <exe> --core-package <dir> --codec-package <dir>` runs RGB FFV1 plus the existing native-resolution CBR/QP0 matrix. Add `--include-crf` for CRF18/28. It requires independent pre-raster bins, verifies source CSV hashes when supplied, and emits explicit GPU mappings. No Luma4 payload model is used for color codecs. `MEASURED` means process completion; byte accuracy remains `AWAITING_GPU` until the GPU harness runs.

The GPU manifest supports an optional per-profile `codecPrefabPath`, defaulting to Luma4 for older manifests. The harness registers Luma4 for the common header and the selected codec for its payload, with cloned materials. Run PNG controls as well as encoded profiles and compare complete bytes against the independent bins. These common-load tests use at most 5,375 payload bytes; the harness's 8,192-byte readback target is not a codec maximum-capacity test.

For a color OBS recording, add `--header-only-association --codec-prefab-path Packages/...prefab` to `Analyze-ObsRecording.py`. This associates IDs using the shared grayscale base header and leaves payload accuracy explicitly unmeasured. The resulting manifest must undergo production GPU validation. Header coverage, exact payload coverage and repeated observations are different measurements.

## Extended measurement tools

- `Generate-Transport-Fixture.py`: creates structured/random protected Luma4 fixtures and independent payload/header bins. Supply `--template`, fresh `--results`, `--width/--height`, `--frames`, `--fps 30`, `--pattern` and actual `--core-package/--codec-package` directories.
- `Measure-Streaming.py --input <fixture/input> --results <new-directory> --ffmpeg <exe> --ffprobe <exe> --seconds 10 --codec-package <package>`: measures x264/NVENC profiles and produces a GPU manifest. Requires NumPy/Pillow. `MEASURED` means completion, not an integrity pass.
- `Run-TransportMeasurement.ps1 -UnityEditor <exe> -Project <isolated-Native-project> -Manifest <json> -Results <new-directory>`: strict-CRC parse-only GPU comparison against original raw bins. Rejections make the command fail and remain in the evidence; no state/RPC is applied.
- `Measure-ObsTransport.py --record-only`: controls a private portable OBS via authenticated obs-websocket (`websocket-client` required). Provide `--obs-root`, `--source-video`, fresh `--results`, native `--width/--height`, `--bitrate`, `--duration`, `--drain` and `--color-range Full|Partial`. Wait for `stream-ready.json`, then create `go.signal`. API readiness does not certify frozen pixels. Network publishing is disabled. Existing OBS processes are left untouched; the tool refuses to start alongside one.
- `Analyze-ObsRecording.py --recording <mkv> --source-fixture <fixture-root> --results <new-directory> --ffmpeg <exe> --ffprobe <exe>`: associates trusted IDs with independent bins, records unknown/duplicate/missing source IDs and produces a GPU manifest. CPU classification remains a numerical model.
- `Measure-Remux.py --input <H.264-recording> --results <new-directory> --ffmpeg <exe> --ffprobe <exe>`: compares decoded RGB hashes after local FLV/HLS remuxing without a server or video re-encoding. It measures file packaging, not live delivery or latency.

Use fresh output directories. Generated recordings, fixtures and local automation configuration belong outside the repository and runtime package.

## Original finite probe

Run in a dedicated SDK-free Unity 2022.3 project with Core and Luma4 installed:

```powershell
& './Validation~/VideoTransport/Run-Validation.ps1' -UnityEditor $unity -Project $project -Results $newResults -Ffmpeg $ffmpeg
```

The harness writes 12 changing Luma4 frames at 1280x720, block8, sample4. Synthetic payload lengths cycle through 32, 2495 and 5375 bytes. Each is a structurally valid NetworkFrame containing one raw-byte field, with protocol 0.2 payload CRC, decoded in parse-only mode without applying receiver state or RPC. The decoder requires a checksum and the CSV distinguishes its presence from verification. The larger lengths match the byte loads of the candidate Full16/Fingers16 profiles; they do not simulate avatar capture or multi-user CPU costs.

Each frame passes through PNG control, RGB FFV1 control, H.264 CRF18 and H.264 CRF28 (`libx264`, `veryfast`, `yuv420p`, nominal file rate 30). FFmpeg restores the files to PNG, and the actual TSMP GPU decoder reads them back. `frames.csv` compares the entire received payload with the known sender bytes and records `EXACT`, `REJECTED` or `CORRUPT`. Header validity alone does not count as success. Any rejected or changed frame makes that profile and the command fail; failed profiles remain useful measurements and must not be omitted.

The result directory contains source images, encoded files, restored images, exact FFmpeg arguments/version and Unity logs. Use a new directory on every run. This is a finite, offline byte-accuracy experiment. It does not measure live frame rate, source age, network loss, OBS/Spout, VRChat client/Udon, state/RPC application or real avatars. Passing these settings does not certify arbitrary encoder settings or transports. No protocol bytes or runtime integrity behavior are changed.

## Payload mutation characterization

For the measured cause of CRF28 rejection, calibration boundary measurements and isolated color-conversion/lossless/filter controls, see [CRF28-ANALYSIS.md](CRF28-ANALYSIS.md). Its reproduction script uses the saved images and a CPU symbol model, independently checked against the saved GPU outcomes; the new controls are not additional GPU or live-client certification.

Run `Validation~/Run-Validation.ps1 -Step PayloadIntegrity` in a dedicated Native project (with the usual UnityEditor, Project and Results arguments). It preserves the legacy characterization (a format-preserving Int32 42-to-43 change can be applied without a checksum), then checks the protected `CompletePayload` boundary rejects the mutation and accepts the original retry. It also checks the standard CRC vector, empty CRC zero, valid-length/dirty-tail behavior and strict/compatible legacy policies. A `PASS` covers both the recorded legacy limitation and protected rejection. No GPU, video, Udon or RPC is involved in this separate test; Snapshot and ContinuousFrames cover those integration boundaries.
