# RGB16 preparation follow-up, 2026-11-01

The prior **96,271.734 ms** observation timed a broad preparation of four
materials times three requested modes and one final ReadPixels. The actual
default RGB16 4/4/4 Refine decoder selects only the fixed Refine material. These
scopes must not be equated.

Two fresh Native Unity 2022.3.22f1 editor processes, D3D11/Gamma on RTX 4090,
retain all existing Unity and driver caches. Both pass, with **67/66 assertions**.
Evidence:
`F:/Unity/TSMP/Validation-Results/next-improvements-20261101/rgb16-preparation`.

| Scope | First measured call/group | Same-process second call/group |
| --- | ---: | ---: |
| Actual fixed Refine, ordinary mode, fixed-first process | **8.2960 ms** | **2.2547 ms** |
| Fixed Refine, recovery mode one, fixed-first | 4.8364 ms | 1.9469 ms |
| Fixed Refine, recovery mode two, fixed-first | 1.8982 ms | 1.9202 ms |
| Broad twelve-draw operation sum, after active path | 45.7222 ms | 13.1574 ms |
| Broad twelve-draw operation sum, broad-first fresh process | 34.8572 ms | 10.8334 ms |

The first actual fixed ordinary call splits into **0.4941 ms PrepareDecode**,
**0.1665 ms Blit submission** and **7.6354 ms flush plus full-byte comparison**.
It has the calibration LUT keyword disabled. Recovery modes one/two enable the
existing LUT path. All 64 flushed verdicts across 108 draws match the saved
5,375-byte original in the harness; 44 intermediate broad draws intentionally
have no readback verdict. The independent analyzer recounts those flags and all
operation sums, rather than independently reading the GPU bytes again.

In the first broad groups, final flushes consume 43.9118/31.7113 ms. They wait
for all preceding draws and cannot be assigned solely to the final variable
Refine shader. Per-material/mode `PrepareDecode`, decode Blit submission,
requested/actual mode and LUT state are retained; an additional two broad groups
per process deliberately flush each draw to make deferred cost visible. Across
all draws, PrepareDecode max is 2.4332 ms and decode submission max 0.1665 ms.

**The historical 96-second delay was not reproduced or resolved.** These are
fresh processes with existing caches, not empty-cache, first-install or actual
VRChat startup tests. The result provides no basis for claiming a 96s→8ms
product optimization, attributing that old delay to one shader, deleting caches,
or hiding startup with prewarming. Product shaders/defaults remain unchanged.

`independent-summary.json` passes the 54-draw schedule per process, timing
arithmetic, mode/LUT invariants, all recorded flushed verdicts and before/after
receipts for **248 production source/material/prefab/meta files plus three
project configuration files** in each run. Input hashes, copied harness sources,
per-draw CSVs and complete logs are retained. The original 96-second evidence is
unchanged. Measurements are host wall time; flush includes queued GPU wait,
ReadPixels and comparison, not a GPU execution-time measurement or TSMP's entire
first-frame Decoder latency.
