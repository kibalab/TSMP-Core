# Isolated Color256 Encoder parity, 2026-11-01

Original and candidate buffers are identical in all **25 managed vectors** and
**19 actual client codec VM vectors**. A separate fixed integer palette and
coordinate oracle agrees with both. This establishes the bounded writer change's
pixel/result/exception parity in these fixtures; performance adoption is a
separate end-to-end SustainedCost A/B decision.

Evidence:
`F:/Unity/TSMP/Validation-Results/next-improvements-20261101/color256-optimization`.

| Capture | Assertions | Color256 client bytecode | Source SHA256 prefix |
| --- | ---: | ---: | --- |
| `baseline-correctness` | 88 PASS | 15,696 B | `c96446cbba1c` |
| `candidate-correctness` | 208 PASS | 17,924 B | `63cf1f4d7257` |

The original capture completed before the source edit, after the old sustained
run and its 392 production source files were independently audited and archived.
Each capture freshly compiles client bytecode after Play Mode entry. Product
source receipts (392 files) and project configuration receipts (23 files) are
unchanged within each capture. The candidate increases the codec's bytecode by
2,228 B; the isolated harness does not claim a speedup or zero allocation.

The vectors cover every one of the 256 colors, payload sizes zero/one/1KB/5KB,
row wraps, partial rows, near/full/over capacity, dirty extra tails, short/empty
block pixel arrays and narrow calibration geometry. The generic full-pixel path
is checked at small VM size and at 1KB/5KB managed sizes. Invalid counts with
short sources, zero block pixels and short full-pixel arrays preserve both
exception type and all partial mutations before failure in managed execution.
Invalid cases deliberately do not run in the VM.

Whole-buffer comparisons include sentinel header/static/end/tail regions. This
codec-only harness does not generate or decode a complete protected raster;
actual Encoder->GPU->Decoder A/B supplies that integration coverage. Managed
execution is the SDK C# proxy's Udon branch, rather than the dedicated Native
project where that branch is excluded. Neither live VRChat FPS nor palette
initialization cost is measured here.
