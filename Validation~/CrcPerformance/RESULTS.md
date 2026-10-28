# CRC32 optimization results, 2026-10-28

The selected implementation uses cached slicing-by-eight tables for production Encoder and Decoder CRCs. The CRC polynomial, byte coverage, output value and wire format are unchanged. Integrity checks remain before payload parsing and field/RPC application.

## Change and cost

`Crc32Runtime.EnsureTable(null)` and `TableSize` retain the existing 256-entry contract. `EnsureFastTable` creates or reuses one 2,048-entry array containing eight tables; `EnsureTable` preserves an existing fast array. `Compute` accepts either representation, uses eight-byte slicing for the fast array, and covers remaining bytes with the original recurrence. The basic-table fallback uses eight-byte loop unrolling. Input range validation avoids integer addition overflow.

Encoder prepares its fast cache in `EnsureResources` in both Native and Udon branches. This includes Editor preview and direct calls before `Start`. Decoder prepares or reuses the same cache in initialization, header and payload handling. Header helpers preserve the fast array rather than replacing it with a basic table. No payload copy or frame-dependent checksum cache was introduced.

Retained primitive-array storage increases from 1,024 to 8,192 bytes: **7 KiB per initialized component cache**, or 14 KiB for one Encoder and one Decoder. The two Decoder slots share its cache. Repeated preparation returns the existing array. These are array storage figures, excluding managed object/VM overhead. Native table creation is logged, including reflection overhead in the benchmark, but Udon cold table initialization was not timed.

## Recorded environment and evidence

Windows 11, Core i9-13900K, RTX 4090, Unity 2022.3.22f1, Direct3D11/Gamma for GPU tests, Worlds SDK 3.10.4-beta.2, client-target compiled Udon in the SDK Editor VM. Actual VRChat client execution remains excluded by the requested automated-only scope.

Evidence root: `F:/Unity/TSMP/Validation-Results/crc-performance-crf28-20261028`.

- `micro-slicing8`: one VM with frozen original baseline and production events; 20 warm calls and 60 measured alternating pairs per size. Heap input/result access is outside timing. Dispatch-only control is recorded without subtraction.
- `profile-before-final` and `profile-after-resources`: final old/new production comparison, run sequentially without another benchmark or the FFmpeg analysis running. Each uses a 12-second prepared steady-state Luma4 workload, 1,280×720, 4,096 value bytes / 4,121 payload bytes, 60-Hz target.
- `statistics-audit.json`: independently recalculated means and p95 reductions.
- `final-source-parity.json`: 111 Core source/shader/assembly files matched each Native/SDK project, 222 SHA256 comparisons with zero differences. `profile-after-resources/runtime-source-hashes.post-run.json` explicitly identifies the final source hash audit as captured after the run.
- `candidates/slicing8-native-result.json`: independent bitwise CRC correctness for the selected source. Its SHA256 is `D92A09AE51E36AEFE1C068EE2DD0219659F7AB58EB3F9293276B96E70B917BB8`.

Earlier unroll-only and partial-integration measurements remain preserved. `profile-after-final` had a fast Decoder but the test adapter skipped Encoder `Start`, leaving that Encoder on the basic table. Moving preparation into both Encoder resource paths corrected the integration, including direct invocation and preview. That intermediate result is not used as the final both-endpoint measurement. Earlier `profile-before` / `profile-unroll8` exploratory runs are also excluded from the final comparison.

## CRC-only compiled VM timings

All values are milliseconds per custom event, including common VM dispatch. The two algorithms run in the same compiled probe with separate basic/fast table fields. Every measured output matches the independent bitwise CRC32 reference.

| Valid bytes | Baseline mean | Fast mean | Mean reduction | Baseline p95 | Fast p95 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 32 | 0.031740 | 0.027737 | 12.61% | 0.035400 | 0.035300 |
| 1,024 | 0.745867 | 0.562625 | 24.57% | 0.789900 | 0.593300 |
| 4,121 | 2.833175 | 2.139918 | 24.47% | 3.052800 | 2.302000 |
| 6,750 | 4.565178 | 3.445762 | 24.52% | 4.879700 | 3.616500 |

The 32-byte p95 is effectively unchanged in this sample. The larger size is a general CRC workload, not a TSMP capacity guarantee. CRC-only values must not be substituted for full Encoder or Decoder timings.

## Production compiled VM timings

| Scope | Baseline mean | Optimized mean | Mean reduction | Baseline p95 | Optimized p95 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Encoder.EncodeNow | 3.617933 | 2.836894 | 21.59% | 3.863600 | 3.029900 |
| Decoder GPU readback callback | 3.524231 | 2.787411 | 20.91% | 3.738200 | 2.940900 |
| Decoder.DecodeNow submission | 0.383476 | 0.375256 | 2.14% | 0.457500 | 0.439900 |

Encoder and callback CPU work together decrease from 7.142163 to 5.624305 ms, approximately **1.518 ms / 21.25%** in this same-process fixture. The enclosing Host scopes are not added, because they include the inner work. This is CPU processing time, not a measured video/network latency improvement. Serialization, dispatch and other processing remain included in the production scopes.

Both runs published and received **721/721** frames at approximately 60 Hz, with zero busy ticks. Source/profile preparation, compilation, cold shader work and table creation precede these measurements. This is one sequential before/after run, with a separate paired microbenchmark; it is not a statistical significance claim, real-client FPS result, populated-world budget, long-running guarantee or Native performance measurement. Scoped allocation counters report `-1` when unavailable; observed collection counts do not establish zero allocations.

## Correctness and integration validation

- 27,762 pure C# checks against an independent bitwise IEEE CRC32 reference passed: known vector, short offsets/tails, long slices including 65,535 bytes, random data, invalid ranges/table sizes, overflow and table migration/identity. Basic, directly generated fast and migrated fast tables were checked.
- 80 independent reference / Native / frozen Udon baseline / production Udon cases passed, including `123456789 = 0xCBF43926`, offset/dirty-tail, null/empty, invalid ranges/tables and seeded random cases.
- Native `PayloadIntegrity` passed legacy characterization and protected mutation/retry boundaries.
- Native Snapshot passed 216 changing-source cases plus four-codec CRC, capacity, lifecycle and frame-window coverage.
- Udon Snapshot passed 16 changing-source cases, four-codec protected readback/parse tests, field/RPC rejection and intact retry, prediction/fallback/combined paths, both bad-slot orders, strict/legacy policy, eight cancellation cases and 30 frame-window cases.
- Native and Udon Continuous prediction/overlap regressions passed with the real production Encoder, pixel-damaged state/RPC rejection and intact same-frame/event retry.
- The final GPU video probe reproduced 36/36 exact PNG/FFV1/CRF18 controls with verified CRC and 12/12 rejected CRF28 frames. Its aggregate remains **FAIL** for transport accuracy; no accepted corrupt/wrong frame occurred. Optimization did not make failed video profiles count as delivered.

See the [CRF28 causal analysis](../VideoTransport/CRF28-ANALYSIS.md) for the separate compression investigation. The integrity and performance changes do not add error correction or make CRF28 reliable.
