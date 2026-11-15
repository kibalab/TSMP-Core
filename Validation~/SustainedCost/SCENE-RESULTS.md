# Aoi and cube measurements, 2026-11-15

See the [follow-up](FOLLOWUP-RESULTS.md) for CPU-placement diagnostics and the
latest-codec prefix experiment. The historical runs below used an older Luma4
UnitySupport source and are not the latest-codec baseline.

No new Runtime optimization was retained from this investigation. A two-condition
Transform candidate passed the scene's content checks but failed the frozen gates.
Same-source process variance is larger than the candidate's proposed benefit.

## Workload and checks

Each fresh process warms for 2 seconds and measures 30 seconds with source/render
targets of30/60Hz.
Aoi sends 51 bones, twelve Rigidbody cubes send packed Transforms, and four state
fields complete the 17-field workload. Actual compiled client Udon programs send
640x360/sample4 GPU Luma4 frames through real VRC readbacks and receiver application.
Unity2022.3.22f1, SDK3.10.4-beta.2, D3D11/Linear, i9-13900K/RTX4090.

Every run requires payload CRC, retains two readback slots and allows two payload
recovery attempts. Header recovery is disabled throughout.
The fixture checks every applied packet, cube pose and all 51 bone rotations.
The independent Python analyzer checks archived header/payload CRCs, typed fields,
state patterns and original/received byte equality. A PASS does not mean every
published state arrived. Target pose equality is a fixture check, not an
independent reconstruction from the exported binary archive.

## Unchanged product

Times are top-level Encoder plus Decoder VM elapsed milliseconds per observed
frame, with measured nested host observations subtracted once. Nested component
times are already included. These are elapsed durations, not hardware CPU cycles.

| Run | Mean / p99 / max ms | Above16.67ms | Received/published | Source-age p99 ms |
| --- | ---: | ---: | ---: | ---: |
| 01 | 6.206 / 13.275 / 15.461 | 0/1800 | 857/900 | 76.214 |
| 02* | 6.586 / 14.779 / 21.322 | 7/1795 | 854/900 | 80.109 |
| 03 | 3.459 / 6.677 / 7.288 | 0/1800 | 897/900 | 50.447 |
| 04 | 3.371 / 6.576 / 11.045 | 0/1800 | 895/900 | 50.488 |

*02 overlapped an unnecessarily expensive post-run analyzer. Its raw evidence is
preserved; 04 was declared as its replacement before measurement. The analyzer
now uses indexed parent lookup. Another user Unity Editor remained open throughout;
its CPU snapshots are retained. Source, loaded bytecode and 3,568 manifest inputs
match across these runs. The remaining approximately twofold process variance is
unexplained, so the fastest run is not selected as the universal baseline.

## Single candidate: not adopted

The private candidate only guards `GetWorldPosition`/`GetWorldRotation` with the
existing final Discrete Rigidbody-application condition. It adds no cache or
setting. Four new processes run A1/B1/B2/A2 after freezing the candidate and gates.

| Run | Mean / p99 / max ms | Above16.67ms | Received/published | Source-age p99 ms |
| --- | ---: | ---: | ---: | ---: |
| A1 | 6.943 / 18.596 / 29.667 | 25/1757 | 826/900 | 82.160 |
| B1 | 6.058 / 13.497 / 25.154 | 4/1797 | 838/899 | 76.401 |
| B2 | 6.510 / 15.297 / 25.074 | 9/1790 | 837/900 | 77.349 |
| A2 | 6.119 / 13.498 / 18.668 | 1/1800 | 838/900 | 78.126 |

A1/B1 improves the mean12.74%; A2/B2 worsens it6.39%. Their equal-weight improvement
is3.18%, below the frozen5% minimum. The second pair also loses one received state;
the first pair's11.11ms exceedance increases0.597 percentage points, above the0.5
limit. Content, exact two-guard source diff and bytecode checks pass, but the
performance result does not establish a consistent gain. Scratch source is restored;
repository Runtime source never changed. No alternative candidate was searched.

## Remaining investigation

Equal-priority rotation can split the four state fields across two messages,
changing this payload from1240 to1250B in3 of17 rotations. Prediction requires an
exact size match, so size transitions can add a payload readback. This is normal
fallback, not a CRC failure, and is not proven to cause all missing states or the
twofold process variance. A future experiment must preserve scheduling fairness,
CRC and complete-frame validation while measuring request counts, delivery and age.

Rendering, PhysBones, recording, actual VRChat login and OBS/CDN transport are
excluded. Native motion preparation is timed separately; automatic PhysX simulation
is not individually timed. Request-to-callback bounds include queue and scheduling
wait and are not GPU execution time. Raw evidence and all failed analysis attempts:
`F:/Unity/TSMP/Validation-Results/scene-optimization-20261115`, with the frozen
`prospective-criteria.json`, `baseline-summary.json` and `compare-decision-v2.json`.
