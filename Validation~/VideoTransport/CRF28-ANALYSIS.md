# CRF28 rejection analysis

Recorded 2026-10-28. CRF28 damages the grayscale Luma4 samples and their calibration references enough to cross symbol decision boundaries. The decoder correctly rejects the changed bytes: the saved GPU run rejects 11 frames at header CRC and one at payload CRC. The controlled comparisons isolate the failure to the lossy AVC encoding/reconstruction path. YUV420 conversion alone preserves every symbol in this fixture.

## Evidence and model validation

Original evidence is preserved at `F:\Unity\TSMP\Validation-Results\payload-crc-20261028\video-protected`. The final independent analysis is at `F:\Unity\TSMP\Validation-Results\crc-performance-crf28-20261028\compression-final`:

- `result.json`: profile totals, source/script SHA256 hashes and GPU/model comparison counts.
- `frames.csv`: per-frame CRC values, byte errors, symbol errors and calibration measurements.
- `symbol-errors.csv`: every changed symbol, coordinates, calibration references and crossed midpoint.
- Per-control directories: FFmpeg commands, logs, videos and decoded PNGs.

`Analyze-Compression.py` reads the actual codec shader and Core source hashes used by the saved Native project. Its numerical model follows the shader's RGB channel mean, central 4x4 samples in each 8x8 block, average of two calibration blocks per symbol, nearest measured calibration value and low-nibble-first byte order. The existing GPU outcomes and header validity match **48/48** model results. All **12/12 stored/computed CRC pairs** printed in the saved GPU rejection logs also match exactly. The script requires both comparison counts and all matches; missing comparisons or disagreement cause a nonzero exit. Compression profiles with rejected frames remain valid characterization results.

The CPU calculations use float64 means; GPU float32 arithmetic can differ at exact ties. The matching outcomes and rejection CRC numbers constrain that limitation for these saved frames. Newly encoded control profiles were evaluated with this CPU model. No Unity process was started for this analysis.

## Controlled results

All sequences contain the same 12 changing grayscale PNGs at 1280x720, block8/sample4. Payload lengths cycle through 32, 2495 and 5375 bytes. H.264 uses `libx264`, `veryfast`, nominal file rate 30. Counts below are CPU-model results; the first four profiles additionally have the saved GPU validation above.

| Profile | Exact frames | Header CRC rejection | Payload CRC rejection after valid header | Header symbol errors | Payload symbol errors |
| --- | ---: | ---: | ---: | ---: | ---: |
| PNG source | 12/12 | 0 | 0 | 0 | 0 |
| FFV1 RGB | 12/12 | 0 | 0 | 0 | 0 |
| H.264 CRF18, YUV420 | 12/12 | 0 | 0 | 0 | 0 |
| H.264 CRF28, YUV420 | 0/12 | 11 | 1 | 35 | 7,606 |
| FFV1 YUV420 conversion control | 12/12 | 0 | 0 | 0 | 0 |
| H.264 QP0, YUV420 | 12/12 | 0 | 0 | 0 | 0 |
| H.264 CRF28, YUV444 | 0/12 | 11 | 1 | 35 | 7,465 |
| H.264 CRF28, YUV420, deblocking disabled | 0/12 | 10 | 2 | 37 | 8,266 |

Symbol totals include offline analysis of bodies whose headers the real decoder rejected before processing. They are diagnostic counts, not applied messages. The CRF28 YUV420 sequence has 28 changed header bytes and 6,822 changed payload bytes across 31,608 payload bytes. A rejected GPU CSV row's `actualBytes=4096` is retained internal readback state, not proof of a decoded 4096-byte body.

The source has equal R/G/B channels everywhere. Both YUV420 lossless controls have at most one RGB value of roundtrip error and zero symbol changes. Changing CRF28 to YUV444 still fails every frame. Disabling deblocking also fails every frame. These controls rule out chroma subsampling or deblocking alone as sufficient explanations for this failure.

## A measured decision-boundary crossing

The fixed source palette is `24 + 14*i` for symbols 0 through 14, followed by 232. Nominal adjacent gaps are 14, with the last gap 12. The shader classifies against measured calibration values, so the actual boundaries move with compressed calibration blocks. There is one header byte sequence starting at block row 2; the three reserved header rows provide capacity and do not repeat the header. This Luma4 decoder has no repeated-header vote, refine pass or error correction.

In CRF28 **frame 1**, the header remains byte-exact. Only the low nibble of payload byte 0 changes, at block `(0,5)`:

| Quantity | Value in 0..255 RGB mean units |
| --- | ---: |
| Expected symbol | 1 |
| Original symbol-1 luma | 38 |
| Recovered 4x4 sample mean | 43 |
| Measured symbol-1 calibration | 32.75 |
| Measured symbol-2 calibration | 49.625 |
| Resulting 1/2 decision midpoint | 41.1875 |
| Sample beyond that midpoint | 1.8125 |

The original 1/2 midpoint is 45. The sample changes by only +5, but the damaged calibration lowers the boundary to 41.1875, causing **1 -> 2**. Expected payload CRC `2190255915` becomes `1267969940`, exactly matching the GPU log. The minimum adjacent calibration gap shrinks from the source minimum 12 to 7.625. The calibration remains monotonic; monotonicity alone would not prevent this case.

Payload byte 0 is `NetworkVersionMajorOffset`: this mutation changes NetworkFrame major version **1 -> 2**. The network parser would also reject that unsupported version if payload CRC were absent. This frame demonstrates CRC detection before parsing; it does not demonstrate that removing CRC would apply this damaged body. The separate valid-mutation integrity test is the evidence for format-preserving variable corruption in legacy frames.

Frame 2 independently shows direct header sample damage: byte 45's high nibble changes **4 -> 5**, with sample mean 90 crossing its measured midpoint 86.5. This changes a byte in the payload CRC field, and header CRC rejects it. Frame 3 changes the low nibble of header CRC byte 53 **3 -> 4**, with mean 73 above its measured midpoint 71.875. CRC fields travel through the same lossy symbol channel as the rest of the header.

Using the uncompressed fixed palette would correct frame 1, but still leaves **6,345 payload symbol errors and 35 header symbol errors** across the CRF28 sequence. That comparison does not justify removing calibration as a general fix. Compression changes both local samples and the references; an accurate global color transform cannot remove all local compression errors.

## Interpretation and next action

The measured failure is a loss of symbol separation during lossy AVC encoding/reconstruction, including damaged calibration references. FFmpeg exposes CRF as quality control and QP as constant quantization control in its [libx264 implementation](https://www.ffmpeg.org/doxygen/trunk/libx264_8c_source.html). The QP0 and conversion controls, plus continued failure without deblocking, support attribution to lossy coding/quantization and mode decisions. Individual transform coefficients were not instrumented, so this analysis does not assign a particular coefficient as the source of each changed pixel.

Keep CRC rejection enabled. CRF18 is the measured passing setting for this exact sequence; QP0 is another passing offline control with substantially different encoding cost. A production transport should be characterized with its actual encoder, bitrate limits, motion, resolution and source path before choosing a quality setting. Any future change to block size, symbol density, calibration robustness or header redundancy should be measured as a separate codec change.

These 0.4 seconds of synthetic data certify no live delivery rate, source age, network behavior, OBS/Spout pipeline, Udon VM cost, VRChat client behavior, avatar capture or state/RPC application. The control experiments add CPU-model evidence rather than new GPU or real-client certification.

## Reproduction

Requires Python with NumPy/Pillow and FFmpeg with `libx264`/`ffv1`. Choose a new empty results directory; existing evidence is never overwritten.

```powershell
python -B './Validation~/VideoTransport/Analyze-Compression.py' `
  --evidence 'F:\Unity\TSMP\Validation-Results\payload-crc-20261028\video-protected' `
  --results '<new-empty-results-directory>' `
  --ffmpeg 'C:\Windows\ffmpeg.exe' `
  --codec-package 'F:\Unity\TSMP\Validation-Results\payload-crc-20261028\projects\Native\Packages\com.kibalab.tsmp.codec.luma4'
```
