# Changelog

## 1.0.0

Stable promotion of all 0.3.0-beta.1 through 0.3.0-beta.3 changes since stable 0.2.0. Runtime, shaders and asset GUIDs are unchanged from beta.3.

### TransSync Sending and Component Controls

- Implement per-field `Priority`, `SendOnChange` and `MinSendInterval` in both native Unity and Udon. Compare serialized contents, including mutations to existing arrays, and measure intervals from successful output.
- Send higher-priority fields first, rotate equal-priority fields after successful output, reserve queued RPC capacity before automatic variables and defer fields that do not fit without truncating them.
- Add unchanged-value refresh, defaulting to one second. A zero `transSyncRefreshInterval` disables refresh. Preserve pending state on failure; when no data is eligible, preserve the last output and frame index.
- Add the shared Inspector **Send Mode** dropdown: Default uses each field's attribute, On Change enables filtering with refresh, and Always also sends unchanged values. Changes apply live without rebuilding bindings. Eligibility, minimum intervals, priority and capacity still apply; RPCs and manual Writer calls are unaffected.
- Add opt-in `TransSync.SentEvent` so avatar root delta and keepalive state commit only after successful output. Guard against encoder reentry during callbacks.

### Timeline, Animator, BlendShapes and Avatars

- Apply the first Timeline position independently of the drift threshold, prepare paused graphs before evaluating, and avoid rebuilding stopped graphs for repeated packets.
- Implement Continuous Timeline drift correction, including the shortest correction across loop boundaries. Clear pending corrections when reception is disabled, the component is disabled or the Director changes.
- Reject malformed Timeline packets, unknown states and invalid times before applying them; clear outgoing data when the source is unavailable.
- Fix Play/Resume after Stop without restarting an already playing Director. Add `Seek(float)` with playback-state preservation and local duration bounds.
- Respect receiver Animator layer selection, correct drift across looping/non-looping states, and read negative state hashes without Udon conversion failures.
- Invalidate stale BlendShape interpolation targets when selections, Renderer, mesh or receive state changes.
- Ignore TransSync reception before decoding field values for Receive Interpolation None. Clear pending Rigidbody velocities when physics reception is disabled.
- Deactivate avatar pool slots above a reduced Max Players limit and clear retired assignments. Retain their objects/rigs for reuse; do not destroy supplied pool objects. Pool Size includes retained inactive objects.

### Decoder Consistency, Ordering and Throughput

- Capture a private image so header, calibration and payload use the same source even when the live input changes during readback. Cancel pending results after disable and release owned resources.
- Filter duplicate and older frames with wrap-aware UInt32 ordering. Add receiver-local **Window Size**, default 256: frame zero may restart ordering when the previous applied index reaches one window.
- Reuse payload capacity across shorter frames while bounding parsing and diagnostics to valid bytes. Reject empty/truncated NetworkFrame payloads before payload readback. Keep exact-length plugin arguments and independent received field arrays.
- Add the default-enabled predicted path: use the previous validated configuration to decode the **current** header and payload in one GPU readback. Always validate the current header CRC. Equal length never means reusing old payload contents.
- Fall back on the same frozen image when codec, options, stream, layout, sample size or payload length changes. Require an exact payload-length match; custom codecs are not assumed to support arbitrary prefix decoding.
- Keep manual layout and safety modes on the sequential path; retain sequential fallback when prediction resources are unavailable. Add advanced opt-outs and prediction/fallback diagnostics.
- Overlap at most two independent captures and apply results in capture order. Full slots skip new captures instead of building an unbounded queue; cancellation drains outstanding requests before buffer reuse. Add an overlap opt-out and pending/busy-capture diagnostics.
- Let opted-in codec shaders write header and payload bytes directly to the readback texture, removing a payload intermediate and packing draw. Existing third-party shaders retain the legacy path.

### CPU, GPU and Memory

- Replace same-type Udon image/payload loops with guarded bulk copies, preserving receiver and in-flight buffer isolation.
- Read GPU output into slot-owned byte arrays and bulk-copy validated ranges without retaining request-owned native views.
- Reuse encoder-owned native raster storage through an optional codec API, keeping the original writer fallback.
- Match known 8-bit/half-float snapshot formats and linear/sRGB interpretation. Keep Float32 for unknown/high-precision inputs and non-RenderTexture Udon sources.
- Encode Luma4 bytes into a small GPU symbol image before block expansion. Prepare materials automatically, retain CPU fallback, defer unused CPU image allocation and release owned GPU resources. Native codecs explicitly opt in.
- Add `TSMPCodec.PrepareDecode(Texture, Material)`, `GetDecodeSampleSize`, calibration-LUT helpers and the optional `calibrationMaterial`. Codecs own Float32 LUT allocation, per-pass refresh and cleanup; Core does not select codec-specific algorithms.
- Remove Setup's redundant Editor output Blit. Only successful Encoder output is presented.
- Preserve integer pixel blocks during expansion and clear right/bottom remainders when dimensions are not divisible by block size.

### RPC and Desktop Streaming

- Preserve RPC queue accounting under reentrant sends and reject events that can never fit the configured payload. Repeats remain a finite attempt budget, not acknowledged delivery.
- Reject FFmpeg source/output dimension mismatches before startup and stop publishing when source dimensions change. Validate RGBA32 readbacks before flipping/writing; do not silently resize TSMP pixels.
- Isolate processes, buffers, callbacks and diagnostics per FFmpeg session so stopped sessions cannot submit into restarted sessions.
- Use cooperative writer shutdown and terminate blocked processes, dispose resources after the writer finishes, detect unexpected exits and restore background execution on failure/shutdown.
- Pace FFmpeg output independently of texture updates while retaining the latest frame.

### Documentation and Regression Coverage

- Update English, Korean and Japanese sending, decoding, codec API, shader integration and installation guides.
- Add reusable English issue templates and contribution guidance across the repositories.
- Add native/Udon scheduling and Timeline regression tests, FFmpeg lifecycle cases, changing-source decoder tests, GPU pixel comparisons and repeatable Player/Udon performance and delivery measurements.

### Upgrade and Measurements

- Install Core 1.0.0 with Luma4 1.0.0 and, if used, RGB16/RGB20/Color256 2.0.0. Codec UPM dependencies use Core 1.0.0; VPM uses >=1.0.0. No prerelease selection is required. SDK remains optional in ordinary Unity and VPM-only for VRChat.
- SendOnChange now controls actual traffic compared with 0.2.0. Select Send Mode Always for continuous resend, subject to field intervals and capacity. RPC repeats remain finite.
- Protocol version, the 56-byte header, packet layouts and codec IDs are unchanged. Window Size is receiver-local, not a new wire field or a reliable session identifier.
- Included resource measurements: Udon sender 11.27 -> 0.457 ms (24.7x), receiver submission/callback 3.904 -> 0.791 ms (4.9x), paired CPU means 15.17 -> 1.248 ms. These historical resource measurements predate protected payload CRC and subsequent Refine recovery; they are not current whole-path costs. The baseline already had predicted/two-slot decoding; these are not direct 0.2.0-versus-1.0.0 measurements.
- Two known 8-bit snapshots use 75% less texel storage. Native whole-Player median frame GC allocation fell from 3,686,964 B to 612 B in the measured 720p case. This does not imply zero allocations or a 75% total-memory reduction.
- Finite native/Udon delivery tests passed at 30/60 Hz; the separate 30-FPS two-slot test received 1799/1799, with median application latency about 66.6 ms versus 33.3 ms with one slot. GPU Luma4 adds a small measured conversion-draw cost.
- Native Windows Mono Player, full UdonSharp compile and SDK VM tests passed during the beta validation. Live VRChat, Quest and IL2CPP remain unverified; the legacy Linear harness limitation remains documented.
- See [1.0.0 release notes](https://github.com/kibalab/TSMP-Core/releases/tag/v1.0.0) for complete cumulative changes, datagram fields, methodology, compatibility and validation limits.

## 0.3.0-beta.3

### Performance

- Replace same-type Udon image/payload copy loops with guarded bulk copies, preserving independent receiver and in-flight buffers.
- Read GPU results directly into slot-owned byte buffers and bulk-copy validated header/payload ranges, without retaining request-owned native views.
- Reuse encoder-owned native raster arrays through an optional codec method; existing third-party writers keep their original fallback.
- Automatically match known 8-bit/half-float snapshot storage to the input, preserving linear/sRGB interpretation. Keep Float32 for unknown/high-precision inputs and non-RenderTexture Udon sources.
- Encode Luma4 bytes into a small GPU symbol image before block expansion. Automatically prepare the material, retain CPU fallback, defer unused CPU image allocation, and release owned GPU resources on disable/destruction. Native codecs explicitly opt in; other codec writers are unchanged.

- Add default-enabled bounded two-slot readback overlap. Capture the next image while an older request is pending, retain independent snapshots/buffers/fallback state, and apply results in capture order. Full capacity skips new captures rather than growing a queue. Disabling cancels both slots; outstanding requests drain before their storage can be reused.
- Add an advanced overlap opt-out and pending/busy-capture diagnostics. Overlap can reduce missed frames at the cost of more snapshot memory, GPU/CPU work and potentially higher application latency; it is not a delivery guarantee.
- Add optional combined byte output for capable codec shaders. Write the decoded header prefix and payload directly into the private readback texture instead of rendering a payload intermediate and a separate packing pass. Preserve the legacy path for third-party shaders without the opt-in properties.
- Add a default-enabled predicted decoder readback path: decode the current header and payload with the previous validated configuration, pack both byte outputs, and request one GPU readback instead of two sequential requests.
- Validate the current header CRC before accepting any speculative payload. Changes to codec, options, stream, layout, sample size or payload length use the existing payload path on the same frozen image. Payload length must match exactly; custom codec implementations are not assumed to support decoding a longer prefix.
- Keep manual layout and safety modes on the sequential path. Assign the packing material automatically for existing/new decoders and retain sequential operation if it is unavailable. Add prediction/fallback diagnostics and an advanced opt-out.
- The transmitted datagram, existing codec entry points, RPC repeat budget and event deduplication are unchanged. At most two captured images are retained; this is not an unlimited frame queue or guaranteed-delivery mechanism.

### Measured Results

Unity 2022.3.22f1, Windows 11, i9-13900K, RTX 4090, D3D11; Luma4, block size 8, sample size 1. Udon uses the SDK Editor VM (Worlds 3.10.4-beta.2); native uses a Development Mono Player. Three-second warmup, twelve-second measurement. Baseline is the pre-resource-optimization profile, already including predicted/two-slot decoding, not the previous published beta.

| Measurement | Before | After |
| --- | ---: | ---: |
| Udon encoder, 720p / 4 KiB / 60 Hz, mean | 11.27 ms | 0.457 ms |
| Udon readback callback, same workload, mean | 3.52 ms | 0.421 ms |
| Native publish, 720p / 4 KiB, mean | 3.33 ms | 0.095 ms |
| Native publish, 4K / 32 B, mean | 20.72 ms | 0.141 ms |
| Native 720p, whole-Player median frame GC allocation | 3,686,964 B | 612 B |
| Two 8-bit snapshots, 4K, texel storage | 253.13 MiB | 63.28 MiB |

- Final native/Udon profiles received 361/361 published frames at 30 Hz and 721/721 at 60 Hz in the measured cases. These finite local tests are not a guarantee for video/network transport.
- GPU Luma4 adds a conversion draw: measured Gamma conversion/expansion medians were about 16.5 us versus 8.3 us at 720p, excluding transfers/readback. CPU work is reduced at a small measured GPU draw cost on this hardware.
- Allocation counters cover the whole test Player, not TSMP alone; collections still occur. Snapshot savings are not total memory savings. Live VRChat, Quest and IL2CPP were not verified.
- See the [release notes](https://github.com/kibalab/TSMP-Core/releases/tag/v0.3.0-beta.3) for complete methodology, latency tradeoffs, validation and limitations.

### Compatibility

- Updated codec releases target Core 0.3.0-beta.3. Luma4 0.0.4-beta.2 requires the new buffered/GPU writer APIs.
- Existing custom codec writers/shaders retain their fallback. No wire-format change or additional setup menu is required.
- Use the output RenderTexture as the encoded result; hidden CPU staging outputTexture is not refreshed by GPU encoding.

## 0.3.0-beta.2

### Added

- Add the shared component-level Send Mode dropdown: Default preserves each TransSync field's SendOnChange setting, On Change enables change filtering with periodic refresh, and Always also sends unchanged values. Mode changes are read live in native Unity and Udon without rebuilding bindings.
- Preserve minimum send intervals, successful-output state, field eligibility, priority and payload capacity in every mode. Udon reads the mode once per captured target per encode. RPCs, manual Writer calls and the wire format are unchanged.
- Document Send Mode and its bandwidth/delivery tradeoffs in English, Korean and Japanese.

### Codec API

- Add `TSMPCodec.PrepareDecode(Texture, Material)` before each header/payload byte pass, with codec-owned linear Float32 calibration LUT allocation, per-pass refresh and lifecycle cleanup. Existing custom codecs can retain the default no-op preparation path.
- Provide `GetDecodeSampleSize`, `PrepareCalibrationLut` and the optional `calibrationMaterial` field for adaptive codec preparation without codec-specific branches in Core.
- Preserve packet layout, codec IDs and legacy shader paths. New codec sources that use the preparation API require this Core version; missing preparation materials fall back to ordinary decoding but cannot compensate for an older Core API.
- Document preparation ordering, shader variants, precision and material ownership in English, Korean and Japanese.

### Fixes

- Remove Setup's redundant Editor output Blit. The encoder alone presents successful frames, including block expansion; failed or skipped encodes no longer trigger a second copy of stale texture data.
- Reuse decoder payload capacity across shorter frames while bounding all network parsing and diagnostics to the current valid byte count. Reject zero/truncated NetworkFrame payloads before payload readback. Exact-length plugin arguments and independently owned received field arrays are unchanged.
- Deactivate avatar pool slots above a reduced Max Players limit, clear retired assignments and retain their objects and rigs for reuse when the limit grows. Supplied pool objects are not destroyed; Pool Size includes retained inactive objects.
- Filter duplicate and older frames within the current stream using wrap-aware UInt32 ordering. Decoder Window Size defaults to 256 frames and is user-configurable: frame zero is also accepted when the previous applied index is at least one window. Track skipped older frames separately.
- Freeze decoder input in a reusable linear Float32 snapshot so header, calibration and payload passes use the same image. Discard pending readbacks after disable, release owned snapshots and preserve the existing wire format.
- Preserve RPC queue accounting under reentrant sends and reject events that cannot fit the configured payload. Retransmission remains a finite attempt budget, not guaranteed delivery.
- Pace FFmpeg output independently of incoming updates while retaining the latest frame.
- Ignore TransSync reception before value decoding when the target selects None, and clear pending Rigidbody velocities when physics reception is disabled.
- Invalidate stale BlendShape interpolation targets after selection, renderer, mesh or receive-state changes, and apply Animator layers only when selected by the receiver.
- Correct Animator drift handling for loop boundaries and non-looping states; read negative state hashes without Udon numeric conversion failures.
- Preserve integer-sized pixel blocks during output expansion and clear right/bottom remainder pixels for dimensions not divisible by block size.
- Commit avatar root delta and keepalive state only after successful output through the opt-in `TransSync.SentEvent` callback; guard encoder reentry during callbacks.

### Datagram and receiver compatibility

The datagram and protocol version are unchanged: a 56-byte header followed by the existing payload. Relevant header fields remain:

| Byte range | Field | Encoding |
| --- | --- | --- |
| 20..23 | StreamId | UInt32 little-endian |
| 24..27 | FrameIndex | UInt32 little-endian |
| 28..31 | TimestampMs | UInt32 little-endian; not used for ordering |
| 44..49 | Reserved | Six zero bytes |
| 52..55 | Header CRC32 | UInt32 little-endian; computed over bytes 0..51 |

Window Size is receiver-local; no window or session field is transmitted. Existing senders do not need changes. The existing `skipDuplicateFrames` option now controls both duplicate and older-frame filtering. With filtering disabled, neither order rule is enforced. Changing streams replaces the order baseline after successful application; no retired-stream history is kept.

The zero/window exception is a restart heuristic, not a session identifier: a delayed old zero can trigger a false restart, a missing zero or restart before one window may be missed, and high old frame numbers may appear newer after a reset. RPC event deduplication is unchanged.

Install this Core version before updating codecs that use the preparation API. Existing codecs using the default no-op preparation path do not require changes.

### Validation limits

Native Unity scheduling, Gamma GPU loopback and a Windows x64 Mono Player passed. UdonSharp client compilation, Encoder VM tests and a local SDK world bundle build also passed; these do not establish live VRChat client compatibility. The full legacy Linear loopback harness failed its CPU calibration/block-expansion check, although separate Linear Editor encoding checks passed. IL2CPP, Quest and serialized references in a running VRChat client remain unverified.

## 0.3.0-beta.1

### Added

- Add per-field TransSync send scheduling in native Unity and Udon: descending Priority, content-based SendOnChange and MinSendInterval measured from successful output.
- Add configurable unchanged-value refresh (one second by default), equal-priority rotation and capacity deferral with diagnostics. Reserve queued RPC payload before automatic variables and retain unsent state for retry. The wire format is unchanged.

### Timeline

- Apply the first received Timeline position after preparing playback, independently of the drift threshold. Prepare paused graphs before evaluating and avoid rebuilding stopped graphs for repeated packets.
- Implement Timeline Continuous receive correction, including shortest-path correction across loop boundaries. Clear pending corrections when reception is disabled, the component is disabled, or the Director changes.
- Reject malformed Timeline packets, unknown states and invalid times before changing playback. Clear outgoing Timeline data when its source is unavailable.
- Make Timeline Play and Resume work after Stop without restarting an already playing Director. Add Seek(float) with playback-state preservation and local duration bounds.
- Add native Timeline regression tests, real Udon VM receive tests and animated Timeline texture loopback coverage in the Windows Mono Player validation.

### Streaming

- Reject FFmpeg source/output dimension mismatches before process startup, and stop publishing if the source size changes. Validate every RGBA32 readback before row flipping or writing; do not resize TSMP pixels implicitly.
- Isolate FFmpeg processes, buffers, output diagnostics and GPU callbacks per publishing session so stopped sessions cannot submit into a restarted publisher.
- Replace writer interruption with cooperative shutdown and process termination for blocked pipes. Dispose session resources after the writer finishes, detect unexpected process exits and restore background execution on failure or shutdown.

## 0.2.0

- Promote 0.2.0 out of beta with the SDK-optional Unity support and shared Controller workflow introduced in 0.2.0-beta.1.
- Improve humanoid Continuous interpolation by converting received world rotations to local targets, handling skipped ancestors and applying root rotation only once (PR #1 by AYANO-TFT).
- Reserve existing Network IDs before assigning IDs to new objects or resolving duplicates.
- Refresh binding, humanoid rig, Animator parameter and codec configuration caches when their contents change, including same-length replacements. Check decoder binding configuration once per network frame.
- Preserve configured codec instances when Setup adds, removes or reorders codec sources, or changes the instance root.
- Read actual Timeline state in native Unity. Add Play, Pause, Resume and Stop controls for explicit Udon playback-state tracking instead of inferring state from time deltas.
- Keep the Decoder payload buffer at its actual size between readbacks instead of reallocating it to 4096 bytes before every header.
- Give each decoder binding its own received array buffer so updating one field cannot overwrite another field or recipient. Reuse buffers for same-length updates within each binding.
- Prevent duplicate automatic encoding in the SDK-free Editor: the native Encoder drives itself, while Setup delegates editor encoding only when UdonSharp is present. Manual encoding and automatic setup preparation are unchanged.
- Restore discrete blendshape values on receipt when animation or another script has changed the Renderer since the previous packet.
- Use the same bounded Animator selection for packet sizing and writing. Limit parameters and layers to 255 entries, reject unrepresentable layer indices, and enforce selection limits in the Inspector.
- Consume native Encoder RPC repeats only after successful frame output, preserving pending events when payload construction, capacity checks or codec writing fail.
- Include Stream ID in the Decoder RPC deduplication key so independent streams can reuse event IDs without losing calls.

## 0.2.0-beta.1

- Support installation, native component bindings, encoding, decoding, and Windows Mono Player builds without VRCSDK/UdonSharp.
- Keep SDK-dependent player capture, proxy APIs, and editor tooling conditional while preserving the existing VRChat/UdonSharp paths.
- Use one shared Controller prefab with automatic component, codec, binding, and per-controller resource preparation.
- Preserve existing Controller references through the Legacy prefab with its original GUID.
- Add repeatable native GPU loopback, Player, UdonSharp, SDK world build, and shared workflow validation.
- Update English, Korean, and Japanese installation and setup documentation.
- Use Luma4 0.0.3-beta.3 or newer for the shared SDK-neutral codec template.
- IL2CPP, managed stripping, and an uploaded VRChat client session remain outside the verified configuration.

## 0.1.0

- Promoted the package version out of beta.
- Added the complete TSMP sample assets under `Samples`.

## 0.0.3-beta.4

- Added inspector error boxes for TransSync variable ID collisions.
- Kept detailed TransSync collision logs in the Unity console.
- Fixed UTF-8 encoding for non-ASCII BMP characters in synced strings.

## 0.0.3-beta.3

- Fixed UTF-8 string encoding for three-byte BMP characters, including variation selectors used by emoji sequences.

## 0.0.3-beta.2

- Merged the latest main branch updates into the release branch.
- Added the TransSync values debug canvas sample update.
- Updated localized README and documentation homepage content.

## 0.0.3-beta.1

- Beta release metadata for VPM distribution.
- Includes core runtime, encoder, decoder, setup tooling, shared network behaviours, and codec authoring APIs.

## 0.0.2

- Fixed intermittent editor compile failures during UdonSharp define transitions.

## 0.0.1

- Initial beta package release.

## Initial Package Split (Historical Entry)

- Initial package split with encoder and decoder runtime included in core.
