# Offline video accuracy, 2026-10-28

These are historical results from before payload CRC protection. The current probe emits protected protocol 0.2 frames and requires verified payload CRC; see the [protected follow-up results](../PAYLOAD-INTEGRITY-RESULTS.md). The figures below remain unchanged.

Environment: Windows, Unity 2022.3.22f1, Gamma, Direct3D11, RTX 4090. Luma4 at 1280x720, block8, sample4. Each profile contains 12 changing frames with payload sizes cycling through 32, 2495 and 5375 bytes. H.264 uses `libx264`, `veryfast`, `yuv420p`. The nominal file rate is 30; twelve frames cover only 0.4 seconds and do not measure live throughput.

The final harness constructs valid NetworkFrames containing one raw-byte field, then performs actual GPU readback and parse-only decoding. Whole payloads are compared against their sender fixtures. No state or RPC is applied.

| Profile | Exact frames | Rejected frames | Accepted but changed frames | Verdict |
| --- | ---: | ---: | ---: | --- |
| PNG control | 12 | 0 | 0 | PASS |
| RGB FFV1 | 12 | 0 | 0 | PASS |
| H.264 CRF18 | 12 | 0 | 0 | PASS |
| H.264 CRF28 | 0 | 12 | 0 | FAIL |

The command correctly exits with failure because CRF28 does not meet byte accuracy. The failed profile is retained. Header or NetworkFrame parsing rejected all twelve frames in the final parse-only run; these results do **not** demonstrate corrupted application state or RPC execution.

A preceding exploratory run used arbitrary payload bytes and readback-only mode. Its PNG, FFV1 and CRF18 profiles also matched 12/12. At CRF28, nine frames failed header checks and three had valid headers but changed payload bytes (1039, 1181 and 8 differing bytes). Readback-only mode bypasses NetworkFrame parsing, so that observation must not be described as three successfully parsed or applied messages. The original harness, images, videos, arguments and per-frame CSV remain with that run.

A separate Native `PayloadIntegrity` characterization reproduced the format-preserving mutation contract: a valid Int32 42 was changed to 43 without changing routing/type/length or header CRC. The actual decoder parser and field dispatcher applied 43. This invokes the parser directly and is not evidence that the compressed-video fixture delivered a corrupted application value. Its `PASS` records reproduction of the integrity limit, not safe rejection. Evidence: `payload-integrity-final` under the same results root; the first fixture-preparation failure is retained in `payload-integrity`.

Evidence is retained outside the repository under `F:/Unity/TSMP/Validation-Results/reliability-20261028/video-transport-network` (final) and `video-transport` (exploratory). Both commands and their nonzero exit are recorded. Controls demonstrate accuracy for their exact finite fixtures, not a certified CRF18 production profile, arbitrary content, long GOPs, reconnection, OBS/Spout, a remote network or actual avatars.

## Follow-up

- Validate the actual intended encoder/decoder route with known sender bytes before documenting a supported external profile. Lowering compression is a candidate control, not a universal quality guarantee.
- Keep header validity, structural validity and sender byte equality separate. The existing protocol still has no whole-payload checksum.
- A payload integrity extension needs a separate wire-version/legacy compatibility design and a check before any state or RPC application. Do not introduce FEC, ACKs or a session protocol as a side effect of binding/tooling cleanup.
