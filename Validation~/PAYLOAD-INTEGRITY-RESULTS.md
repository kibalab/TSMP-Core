# Payload integrity follow-up, 2026-10-28

Production Encoder frames now carry a payload CRC32 in the existing 56-byte header. A checksum failure is rejected before payload parsing, state assignment or RPC dispatch. This closes the demonstrated legacy case where a value could change from 42 to 43 while the header CRC remained valid. It detects corruption; it does not recover lost data or authenticate a sender.

## Contract and compatibility

- Protected frames use protocol minor 0.2, flag bit 0 and bytes 44–47 for little-endian CRC32. Header CRC still covers bytes 0–51, including this extension. The NetworkFrame body and codec IDs are unchanged.
- Production Encoder always writes the checksum. Low-level legacy header APIs remain available.
- Decoder verifies protected payloads in both readback-only and parse/apply modes. `requirePayloadCrc32` defaults to false for legacy senders; enable it when missing-checksum frames must be rejected. That policy is captured per request/slot.
- `lastPayloadCrcPresent` reports the extension; `lastPayloadCrcVerified` reports completed payload verification. Header-only and skipped frames are not verified payloads. A skipped predicted frame may still refresh header metadata used for prediction.
- Existing readers can ignore the extension, but do not gain payload protection. This compatibility was reviewed in source; a previous-release receiver was not exercised through the new GPU path.

## Environment and evidence

Windows, Unity 2022.3.22f1, Direct3D 11, Gamma, RTX 4090, VRChat Worlds SDK 3.10.4-beta.2. Native and SDK projects embed package copies: 111 Core source/shader/assembly files were compared against each project (222 SHA256 comparisons, zero mismatches; `source-parity.json`). No package versions, metadata GUIDs or serialized prefab/scene assets changed. Udon tests execute client-target compiled programs in the SDK Editor VM with real GPU callbacks. They are not VRChat client tests.

Raw evidence is preserved outside the repository at `F:/Unity/TSMP/Validation-Results/payload-crc-20261028`. Final documentation typecheck and all three locale production builds passed; their checks are at `F:/Unity/TSMP/Validation-Results/payload-integrity-20261028/docs-final`. Earlier implementation evidence remains at `F:/Unity/TSMP/Validation-Results/reliability-20261028`.

| Check | Final observation |
| --- | --- |
| Protocol and parser boundaries | Standard CRC32 vector `0xCBF43926`, empty payload, dirty buffer tail, bounds, minor/flag, strict/legacy and intact retry passed |
| Native functional suite | 12 passed; Inspector initially failed because two test avatars shared ID 0. Unique fixture IDs corrected that cause; the focused Inspector recheck passed all eight paths. The original failed run remains preserved |
| SDK suite | 9/9 passed: import, SDK initialization/preparation, client compilation, Workflow, Configuration, TransSync VM, RPC queue VM and Snapshot |
| Native Snapshot | 216 changing-input cases plus protected payload tests passed |
| Udon Snapshot | 16 changing-input cases, new protected payload tests, capacity/lifecycle cases, eight cancellation cases and 30 frame-window cases passed |
| Protected payload coverage | Four codecs, readback-only/parse modes, state/RPC rejection, same-frame/event retry, prediction/fallback/combined paths, both bad-slot orders and different lengths, strict missing-checksum and legacy compatibility passed |
| Continuous Native/Udon | Production checksum, pixel-damaged payload rejection before field/RPC, intact retry, prediction and overlap regressions passed |
| Runner compiler controls | Synthetic compiler failure blocked dependent steps; Inspector compile failure prevented GUI invocation. PowerShell parsing passed |

Initial evidence also retains an EditorEncoding harness dependency error and an SDK preparation shutdown timeout. The former was fixed by making codec material cloning local to that harness. SDK preparation now exits on a deferred Editor callback and is no longer launched with competing `-quit` behavior. Compiler errors are identified from the original log and stop dependent checks instead of opening a hidden Inspector error dialog.

The first new Continuous Udon regression exceeded the VM's fixed ten-second watchdog on cold shader preparation at `VRCGraphics.Blit`. Both the baseline and protected measurements use the same host preparation: client compilation after Play entry, then native draws of cloned materials before VM assertions/profile timers. Preparation duration is recorded separately. The watchdog was not increased. These results establish prepared steady-state behavior, not cold-start performance.

## Udon performance cost

Sequential A/B runs used the same host preparation, Luma4, 1280×720, 4,096 value bytes / 4,121 payload bytes, a 60-Hz target and twelve measured seconds. The baseline retained the earlier CRC-free runtime and recorded its source hashes. `ResourceProfile.Measure` manually invokes publication/decoding for this timing comparison, irrespective of the automatic scheduling variable. No second benchmark ran concurrently.

| Compiled VM scope, mean per call | CRC-free | Protected |
| --- | ---: | ---: |
| Encoder.EncodeNow | 0.460 ms | 3.570 ms |
| Decoder.DecodeNow | 0.376 ms | 0.383 ms |
| Decoder readback callback | 0.427 ms | 3.615 ms |

The added Encoder and callback CPU work totals approximately 6.30 ms per frame in this same-process loopback, approximately 3.1 ms at each endpoint. This is not a measured 6.30-ms increase in end-to-end video latency. These scopes exclude the enclosing Host timings to avoid double counting. Source review found one payload CRC scan per direction and a reused table, consistent with an O(N) Udon loop cost rather than accidental repeated payload hashing or table construction. Both runs published and received 721/721 frames at approximately 60 Hz with no busy ticks. This is a substantial CPU cost despite successful local delivery. It is not evidence that a populated world has that much spare CPU time. Scoped allocation counters were unavailable (`-1`); zero observed GC collections is not proof of zero allocations.

The protected automatic scheduling recheck used separate twelve-second cases:

| Case | Published/received after drain | Apply Hz in window | p95 latency | Corrupt / out of order |
| --- | ---: | ---: | ---: | ---: |
| 640×360, 32 value bytes, 30-Hz wall-clock publication | 359/359 | 29.75 | 66.85 ms | 0 / 0 |
| 1280×720, 4,096 value bytes, every update at a 30-Hz target | 361/361 | 29.92 | 67.04 ms | 0 / 0 |

The first case missed one sender deadline. This is distinct from a published frame that the receiver lost. The earlier manual-scheduling comparison is also preserved; it is not substituted for the automatic result.

## Offline compressed video

Protected protocol 0.2 frames were tested through FFmpeg at 1280×720, Luma4 block 8 / sample 4, with twelve synthetic changing frames and payload lengths 32/2,495/5,375 bytes per profile. The receiver required the checksum and used parse-only mode.

| Profile | Exact | Rejected | Accepted corrupt / wrong frame |
| --- | ---: | ---: | ---: |
| PNG control | 12/12 | 0 | 0 / 0 |
| FFV1 RGB | 12/12 | 0 | 0 / 0 |
| H.264 CRF 18, veryfast, YUV420p | 12/12 | 0 | 0 / 0 |
| H.264 CRF 28, veryfast, YUV420p | 0/12 | 12 | 0 / 0 |

The aggregate video probe correctly remains **FAIL**, because CRF 28 did not deliver exact frames. One rejected CRF 28 frame retained a valid header and failed the new payload CRC; eleven failed header CRC. No rejected frame was counted as successful delivery. These finite offline results do not certify arbitrary CRF 18 streams, live OBS/Spout, MediaMTX, pacing, network behavior or avatar capture.

## Next decisions

Keep the existing GPU LUT, GPU Luma4 path, snapshot ownership and two-slot reuse. Do not add ACK/retransmit/session/FEC machinery on the basis of these local tests. First decide the required payload size and publication rate against the measured checksum cost; any CRC optimization should have a separate measured A/B change. A live VRChat validation remains outside this automated-only run. Linear color space, IL2CPP, Quest and new four-codec protected regressions in a standalone Player were not added to the validated scope.
