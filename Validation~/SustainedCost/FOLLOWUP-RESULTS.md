# CPU placement and Luma4 readback follow-up, 2026-11-15

The smaller-payload prefix experiment was **not adopted**. Core Runtime remains
unchanged. The separate Luma4 shader startup fix is recorded in the codec repository.

## CPU placement

On this i9-13900K, logical CPU2 is a P core and CPU16 is an E core. With identical
source and five-second windows, Encoder mean was 2.304ms on CPU2 versus 5.460ms
on CPU16; combined VM mean/frame was 3.348 versus 7.892ms. Both passed content
and independent packet checks with unchanged inputs. These diagnosis runs used
the older UnitySupport codec source, so they are not current-codec performance
baselines. Earlier historical runs lack CPU telemetry; their placement cannot
be recovered. Placement reproduces a major source of variance, not every spike.

Affinity is optional validation instrumentation, restored after each case. No
production affinity, power plan or driver setting was changed.

## Prefix experiment

Latest Luma4 source `a1be05e`, with common shader `9d269e57`, was used in every
fresh A1/B1/B2/A2 process. Unity2022.3.22f1, SDK3.10.4-beta.2, D3D11, **Gamma**,
RTX4090; main thread pinned to logical CPU2, 2s warmup and 30s measurement.
Source/render targets were30/60Hz. Every run published900 measured states.
The workload remains Aoi51 bones, twelve cubes and four state fields.

Times below are net top-level VM elapsed time per observed frame, not total
render-frame cost. Source age includes holding the last applied value.

| Run | VM mean / p99 / max ms | Received / published | Source-age p99 / max ms |
| --- | ---: | ---: | ---: |
| A1 | 3.314 / 6.561 / 11.957 | 898/900 | 50.467 / 79.588 |
| B1 | 3.370 / 6.454 / 8.305 | 897/900 | 50.402 / 79.725 |
| B2 | 3.373 / 6.678 / 13.590 | 897/900 | 63.447 / 81.949 |
| A2 | 3.322 / 6.444 / 8.116 | 900/900 | 50.471 / 63.443 |

The candidate accepts an already captured Luma4 prefix when the actual protected
payload shrinks. Four controlled shrink cases reduced actual SDK callbacks2→1
with identical bytes/state/RPC. Whole-session fallback/publication declined
116/991→58/991 in both pairs, exactly50%. However, VM cost/publication increased
1.70% and1.52%; cost/applied state increased1.81% and1.75%. B2 source-age p99 rose
12.976ms, exceeding its frozen2.524ms allowance. Therefore the original
exact-size guard is retained, without threshold changes or performance reruns.

Application-latency p99 was36.478–36.738ms across the four runs; maxima were
36.592–66.273ms. No measured VM frame exceeded16.67ms; the11.11ms exceedance
counts were1/0/2/0. Individual maxima were reported, not selected as a new gate.

All raw packet/CRC/17-field checks passed. Each run's3,698 manifest inputs stayed
unchanged; only the intended Decoder source and loaded bytecode differed between
variants. Every measured VM endpoint was CPU2. Native Player and compiled client
Udon passed prediction-shrink, prediction-regression, overlap-regression and full
DecoderSnapshot with the combined candidate and shader. No corrupted state or
RPC was accepted.

The first formal A1 attempt, using the original shader, exceeded the resource
guard before measurement. It is preserved separately; the replacement order and
common shader were recorded before these four valid runs. Earlier measurements
using the stale UnitySupport codec are excluded from the candidate decision.

Raw evidence: `F:/Unity/TSMP/Validation-Results/prediction-followup-20261115`;
`criteria-original-frozen.json`, `fixed-shader-decision.json`, and A1r/B1r/B2r/A2r.
Actual VRChat, rendering/PhysBones, FPS and OBS/CDN transport remain untested.
