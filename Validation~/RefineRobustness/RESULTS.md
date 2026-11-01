# Receiver robustness improvements, 2026-10-31

The final default receiver recovers **262 additional complete original payloads** from the same 20,439 saved observations, and **33 additional payloads** from 900 observations generated with a new random seed. Previously correct observations remain correct; no wrong CRC-valid payload was accepted in these finite tests. Sender pixels, palettes, protected packet layout and the CRC algorithm are unchanged.

The improvement is receiver recovery, not a measured increase in live FPS. Actual VRChat, service ingestion/playback, live source age and end-to-end update throughput were not measured.

## Shipped behavior

- Ordinary decoding remains first. A protected payload CRC failure can trigger at most two alternate decodes of the **same slot-owned snapshot**. Both budget and header policy are captured with each request. A recovered payload must pass CRC validation before state/RPC application or frame acceptance.
- RGB16 Refine fits a full-RGB affine forward model from the existing channel-axis calibration blocks. Recovery modes use YCoCg luma weights two and four. Four model texels are appended to the existing calibration LUT only for recovery; ordinary Refine retains its no-LUT policy. Invalid/unavailable fits retain the previous model.
- Color256 Robust Refine keeps its existing candidate neighborhood and uses alternative luma weights four/eight during recovery. In CPU screening, existing full-palette search did not add recoveries beyond the equally weighted local candidate, so no larger search was adopted.
- Luma4 has optional whole-pilot linear-fit and nominal-palette calibration hypotheses. **Header recovery defaults off** (`recoverHeaderCrc=false`). Across the main and new-seed experiments, 68 recovered headers all subsequently failed payload CRC; none added a complete payload. Keeping this opt-in avoids work without losing a measured complete recovery.
- `maxCrcRecoveryAttempts=2` is the default payload budget; zero disables recovery. Default failures add at most two readbacks. Enabling optional header recovery can add another two and delay later FIFO slots.
- RGB16 variable Direct and Refine now correctly assemble bytes spanning three/four low-bit symbols. The former two-symbol limit failed valid 3/5-bit layouts even on pristine rasters. The existing >=8-bit bulk path, symbol mapping and sender packing are unchanged.

## Same-file production comparison

Unity 2022.3.22f1, Windows, D3D11/Gamma, RTX 4090. The actual Native TSMPDecoder requires payload CRC and compares every received payload byte against independently saved pre-raster bins. Parse-only measurements apply no application state or RPC. Separate integration tests exercise that boundary.

The baseline uses frozen prior packages. Its complete 66-profile replay matches all 20,439 historical observations. The final defaults repeat the complete dataset after installing the committed runtime changes. A further 119-input/238-observation disabled/enabled comparison checks the disabled path against the prior decoder.

| Dataset | Before exact | Final exact | Additional exact |
| --- | ---: | ---: | ---: |
| Same 66 profiles / 20,439 observations | 17,528 | 17,790 | 262 |
| New seed, 10 profiles / 900 input observations | 437 | 470 | 33 |
| Saved OBS RGB16, known nonblack observations | 1,203/1,212 | 1,212/1,212 | 9 |

The main dataset preserves 45,369,175 -> 46,103,478 complete original payload bytes, an additional **734,303 B**. It includes lossless controls and easy profiles, so its aggregate 83.333% -> 84.682% byte retention is not a transport recommendation. The CBR subset preserves 85.651% -> 88.498% of payload bytes.

Selected same-file conditions, structured/random combined, **complete exact payloads / 600**:

| Codec / native raster / encoder | Before | Final | Complete-payload byte retention before -> final |
| --- | ---: | ---: | ---: |
| Color256 / 720p / x264 veryfast CBR 3.5M | 362 | 429 | 27.83% -> 45.28% |
| Color256 / 720p / x264 veryfast CBR 6M | 585 | 595 | 95.42% -> 98.30% |
| Color256 / 720p / NVENC CBR 3.5M | 593 | 600 | 98.17% -> 100% |
| RGB16 / 720p / x264 veryfast CBR 3.5M | 306 | 336 | 22.78% -> 28.22% |
| RGB16 / 720p / x264 veryfast CBR 6M | 556 | 564 | 89.46% -> 91.84% |
| RGB16 / 720p / NVENC CBR 3.5M | 588 | 599 | 97.32% -> 99.998% |
| RGB16 / 1440p / NVENC CBR 10M Full | 593 | 595 | 99.00% -> 99.34% |
| RGB16 / 1440p / NVENC CBR 10M Limited | 588 | 593 | 97.84% -> 99.00% |
| Color256 / 720p / x264 CRF18 | 122 | 232 | 0.87% -> 10.29% |
| RGB16 / 720p / x264 CRF18 | 6 | 8 | 0.012% -> 0.016% |
| Color256 / 720p / x264 CRF28 | 10 | 11 | 0.020% -> 0.022% |
| RGB16 / 720p / x264 CRF28 | 0 | 0 | 0% -> 0% |

Both codecs retain 600/600 at the tested 720p NVENC 6M profile. Color256's successful 1440p NVENC 10M conditions remain successful. These are finite-file outcomes; same bitrate does not imply identical encoding behavior across OBS/FFmpeg presets or arbitrary scenes.

OBS observation counts include held/repeated frames. They must not be presented as unique update Hz or nine newly delivered events. Black observations remain rejected. Source IDs unobserved in the original recordings cannot be reconstructed by this work.

## Independent new input

The holdout uses NumPy PCG64 seed 20261031, 90 native 720p frames at 30 fps, with 32/2,495/5,375 total payload bytes. None of its 90 payloads equals the previous seed's corresponding payload. Actual production Color256/RGB16 writers preserve those bytes and stamp their normal protected headers. Each codec has PNG controls plus x264 CBR3.5M, NVENC CBR3.5M, CRF18 and CRF28: 900 input observations, each tested with recovery disabled/enabled.

Color256 NVENC3.5 improves 87 -> 90/90, x2643.5 improves 44 -> 53/90, CRF18 18 -> 31/90 and CRF28 2 -> 3/90. RGB16 NVENC3.5 improves 84 -> 89/90, x2643.5 21 -> 22/90, CRF18 1 -> 2/90 and CRF28 remains zero. Complete original bytes improve 963,397 -> 1,025,650 (+62,253 B). This small additional sequence is a holdout check, not independent certification of arbitrary content or a live service.

## Processing cost

GPU measurements compare uniquely named frozen shaders with the installed normal/recovery materials. Custom GPU markers contain 32 draws, including calibration preparation in every draw. Each of 24 cases has ten warm frames and thirty valid paired samples, alternating AB/BA. Independent audits recompute all mean/median/p95 values from raw nanoseconds and marker counts.

Normal-pass GPU mean changes versus frozen shaders are **+0.167–0.219% for Color256**, **+0.283–0.443% for fixed RGB16**, and +0.331–0.623% for the common header. Their measured p95 increases are respectively 0.370–0.372%, 0.189–1.565% and 0.238–0.689%.

Variable RGB16's forced 4/4/4 diagnostic path has a real regression after correcting sub-byte assembly: **56.58–56.81 -> 63.33–63.35 us**, or +11.488–11.958% mean; p95 increases 14.55–15.49%. This is approximately 6.5 us per draw on this GPU. Ordinary 4/4/4 production selects the fixed shader, not this forced variable path. These two source-size cases do not establish the cost of every variable-bit layout.

| Additional recovery pass, including its preparation | Measured GPU time per pass |
| --- | ---: |
| Color256 modes one/two | 0.37269–0.37300 ms |
| RGB16 fixed modes one/two | 0.43995–0.44003 ms |
| RGB16 variable modes one/two | 0.48722–0.48736 ms |

These are additional passes, not an entire recovered frame or the total decoder. GPU pass time excludes CPU callback/CRC/application work and asynchronous readback waiting; no percentage applies to total VRChat FPS. Two retries can incur two such passes plus their waits. Normal successful frames perform no retry.

Disabling the unproductive header stage reduces extra readbacks in the main dataset from **5,756 -> 4,093 (-28.892%)** and in the holdout from 912 -> 673 (-26.206%), with identical complete-payload acceptance in both. These percentages describe additional retry readbacks, not total processing time.

A simpler generic byte-assembly helper was also measured as a shadow candidate. It did not remove the variable normal-pass regression and was not installed. Its first pair had higher and more variable measured times; the cause was not isolated. Absolute times from separate pairs must not be cross-compared. The retained implementation has the complete production and mode-coverage validation above; the rejected candidate remains measurement-only under `Candidates/Optimization`.

## Correctness and limits

- Final Core safety checks pass 910 assertions, including default header-off policy, captured mutable policies, per-stage limits, two independent slots, source changes, both bad-slot orders, prediction/combined paths, disable/re-enable, rejection before state/RPC and intact retransmission of the same frame. All nine saved OBS failures also recover through the actual receiver.
- Gamma and Linear recovery-mode coverage each pass 144 normal controls and 864 forced diagnostic draws: nine codec configurations, source ARGB32/Float, samples one/four, both orientations and 32/1,023-byte payloads. Forced mode zero matches actual production bytes. Each run has 128 exact non-alias normal controls; the known 8/8/4 alias setting accounts for 16 rejected controls and 48 diagnostic mismatches. Luma4 sample-one exercises the no-LUT path. All 864 candidates completing is not a claim that every candidate is exact.
- Final Snapshot integration passes in Gamma and Linear, each with 216 changing-source cases plus CRC/state/RPC, prediction/fallback, capacity/dirty-tail, cancellation, frame-window and Float32 precision checks. Auxiliary texture-copy timings are separate from the refinement GPU measurements; one such timing is unavailable in the Linear run and is not substituted or used for a speed claim.
- UdonSharp compilation passes for 16 TSMP programs with Setup bindings. The fresh Decoder bytecode contains default payload budget two, header recovery false, captured request policy and per-slot arrays. This is compilation/binding validation, not a live Udon/GPU benchmark.
- Selected source files match the Native and VRC projects; existing GUIDs, shader names, main refs and source ownership are preserved. No merge, push, package release or protocol-version change was performed.

CRF28 remains unusable for most tested payloads. RGB20's previously measured ordinary H.264/NV12 failures are not repaired. Missing/black frames and palette colors that have become indistinguishable cannot generally be reconstructed without extra information. RGB16 8/8/4 already maps 256 levels into only 209 integer Color32 values and is an explicit alias diagnostic. RGB16 affine recovery requires a supported ARGBFloat LUT. Other devices, graphics APIs, IL2CPP, live VRChat/VRCDN, source age and exactly-once delivery over a service need separate validation.

## Validation history and evidence

Evidence root: `F:/Unity/TSMP/Validation-Results/refine-robustness-20261031`.

CPU diagnosis selected candidates but was not treated as authoritative: float ties caused discrepancies with some compressed GPU outcomes. The final recovery totals use actual GPU/production paths and independent source bytes.

The initial `mode-gamma` run exposed the existing low-bit byte-assembly bug. `mode-gamma-final` then exceeded the harness's 30-second correctness deadline during first use of the updated shader. The harness was given a Camera frame pump, a 180-second correctness deadline and bounded raw-data diagnostics. A shader compiler was observed consuming about 170 CPU seconds during the subsequent cold compilation; `mode-gamma-final2` completed successfully. The first final Linear Snapshot run passed its 216 source cases before exceeding its own 60-second deadline in a recovery case; the test deadline was extended to 180 seconds and `snapshot-linear-final2` passed completely. These records are retained, with no replacement of original pixels, headers or payload bins. The exact contribution of frame pumping versus cold compilation is not isolated. Performance markers retain their separate timing procedure.

Initial header-enabled `production-after`, `heldout/production`, `gpu-timing` and their audits remain experimental evidence. Final default policy uses `default-production`, `default-heldout`, `default-disabled-sample`, `gpu-timing-final`, `core-recovery-final`, `mode-gamma-final2`, `snapshot-gamma-final`, `snapshot-linear-final2` and `mode-linear-final`. Source manifests, copied candidate sources, independent CSV audits and failed runs remain preserved.

Principal independent summaries are `default-recovery-independent-audit.json`, `default-heldout-independent-audit.json`, `default-gpu-timing-summary.json/.md`, `default-policy-cost-comparison.json`, `source-audit-final.json` and `udon-compile-final`. Condition-level results are in `default-recovery-condition-comparison.csv/.md` and `default-heldout-profile-comparison.csv/.md`.

Use [README.md](README.md) to reproduce in dedicated projects. Prior comparison context is in [CODEC-TRANSPORT-RESULTS.md](../VideoTransport/CODEC-TRANSPORT-RESULTS.md); the earlier pixel-equivalent speed optimizations are in [ColorRaster/RESULTS.md](../ColorRaster/RESULTS.md).
