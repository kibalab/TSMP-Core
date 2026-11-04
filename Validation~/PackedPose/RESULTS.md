# Packed pose and completion follow-up, 2026-11-04

The representative packed component workloads are substantially smaller than
the earlier 128-independent-scalar stress case. This is a workload finding on
unchanged product code, not a measured new Runtime speedup. The fixed-header
copy candidate passes correctness but fails nine frozen adoption guards and is
withdrawn. This round retains measurement tools, regression coverage and reports.

## Actual packed component workload

Two fresh current-SDK Editor processes run six fixed cases, each with 16 warm
and 512 measured calls. Every row below pools 1,024 calls; percentiles are
nearest-rank from all raw observations, with no outlier removal. Encode includes
the real component BeforeEncode capture and GPU Luma4 submission. Decode applies
the original protected completed bytes through actual component bytecode,
excluding receiver GPU/capture/transport waits. Receive mode is Discrete and
refresh=0 isolates unchanged capture/comparison. Humanoids have real generated
17-bone Animators, no fingers; this excludes VRChat player API capture/skinning.

| Workload | Encode mean / p99 / max ms | Decode mean / p99 / max ms | Combined p99 / max ms |
| --- | ---: | ---: | ---: |
| Transform 1 changing | 0.3952 / 0.6298 / 0.9025 | 0.1880 / 0.2720 / 0.4560 | 0.8878 / 1.3585 |
| Transform 8 changing | 1.0128 / 1.2751 / 1.5018 | 1.0864 / 1.2516 / 2.1965 | 2.4921 / 3.3864 |
| Transform 8 unchanged | 0.6562 / 0.7978 / 0.8595 | No measured publication/application | 0.7978 / 0.8595 |
| Humanoid 1 changing | 0.5171 / 0.7267 / 1.7927 | 0.3939 / 0.4878 / 0.8633 | 1.1838 / 2.2759 |
| Humanoid 4 changing | 1.2190 / 1.3748 / 1.5516 | 1.4372 / 1.5566 / 1.7053 | 2.8869 / 3.1212 |
| Humanoid 1 + Transform 4 changing | 0.9049 / 1.2014 / 1.5525 | 0.9293 / 1.0990 / 1.6166 | 2.2850 / 2.8255 |

Each Transform is one 31-byte packed field. Each test Humanoid is one 123-byte
packed field including root motion; the mixed packet contains five fields.
Measured combined calls remain below 16.67 ms throughout. These controlled
figures cannot be transferred to full production avatar rigs or real frame FPS.
The earlier 128-scalar Decode medians around 25-27 ms describe another workload,
so their difference is not a product optimization percentage.

Cold work remains separate: each process's first Encode takes 54.0674/55.2803 ms.
Other new-case first Encode calls take 3.0647-3.8999 ms. The fixture constructs
fresh resources, marks Start completed and manually calls OnEnable; these values
are not a production Start benchmark. Production already prepares resources in
Start. No cold-path improvement is claimed. Fixed-size source/receiver arrays
are reused after first use, and a separate same-count target-replacement control
passes without a new binding array.

Independent formal audit passes 6,336 Encode calls, 5,282 protected packet/header
pairs, 20,080 field bodies, 1,205,392 field bytes and 127,792 decoded quaternions.
All 2,641 corresponding paired payloads and timestamp/header-CRC-normalized
headers match. Source position/scale and quaternion/bone-chain expectations are
recomputed separately from the production serializer. Actual receiver object
identity and Transform application remain checks by the frozen VM fixture.
The separate 72-call pilot also passes; it is not pooled into formal timing.

## Natural GPU completion observations

Four actual-GPU source A/B processes run Luma4 1KB/30Hz and 5KB/30Hz normal
cases, plus a labeled 5KB source60Hz/render60Hz stress case. Each case warms
two seconds, measures 12 seconds, retains a one-second publication tail and
drains pending work. Both variants use identical frame-count instrumentation.
The source and receiver share one paced Editor loop; configured 60Hz is not an
independent external video source. Achieved publication/application rates and
deadline misses remain part of the result.

The receiver records Time.frameCount inside each one-field packet callback.
Independent ordinal-to-dispatch checks inspect 28,641 top-level VM events and
7,080 applications, including warm/tail observations. Across 8,641 measured
arrival-frame observations there are **2,998 zero**, **5,642 single** and **one
double** accepted-packet completion frames. No single Decoder dispatch applies
two accepted packets: zero of 17,268 measured (21,570 total) Decoder dispatches.

The one double-completion frame is original run0's 60Hz stress frame1965,
IDs147/148, in two distinct GPU callbacks taking 1.8646/1.7418 ms. Total TSMP
net VM time in that frame is 5.6387 ms. The original stress maximum frame CPU
is 35.5276 ms elsewhere. This does not establish frequent synchronous two-slot
drain as the cause of observed worst times. Clean one-pair local observations
do not establish burst frequency with video jitter, many decoders or live VRChat.
Keep the two slots and avoid a new per-frame completion cap on this evidence.

## Fixed-header copy candidate: not adopted

Only two private 56-byte prediction-header loops in TSMPDecoder are replaced by
System.Array.Copy, with no new state/settings/buffers or protocol/default changes.
Native and actual client VM prediction/fallback, header/payload CRC rejection,
overlap ordering, cancellation and repeated-RPC deduplication preflights pass.
Decoder bytecode shrinks 138,380 -> 138,132 bytes. Packed completed-byte injection
does not exercise these prediction copies; only the actual GPU runs support
the performance decision.

All guards and ABBA order are frozen before any timing. Each source variant has
two processes, giving 24 measured seconds per case. Net Decoder event means
include Update and GPU callback event mix; frame CPU, CPU/s and delivery/age must
also be considered. Host nested correctness-observation ticks are retained and
subtracted once; the extra receiver frame write remains identical VM work in
both variants. All samples, including unfavorable original maxima, are kept.

| Actual GPU workload | Original/candidate Decoder mean ms | p99 ms | Maximum ms | Frame CPU maximum ms |
| --- | ---: | ---: | ---: | ---: |
| 1KB / source30 / render60 | 0.3119 / 0.3010 | 0.6659 / 0.6751 | 2.7446 / 1.3917 | 3.4939 / 2.1610 |
| 5KB / source30 / render60 | 0.6171 / 0.6205 | **1.9198 / 2.0840** | **2.1211 / 3.1599** | **4.3087 / 6.0160** |
| 5KB / source60 / render60 stress | 1.0688 / 1.0072 | 2.4027 / 2.0941 | 14.4365 / 2.2849 | 35.5276 / 5.2756 |

The favorable stress CPU values accompany fewer candidate publications:
1,430 -> 1,338 of 1,440 deadlines, with misses **10 -> 102**. Received published
IDs are 1,428/1,430 original and 1,338/1,338 candidate. Source-age p95 worsens
36.0818 -> 48.6452 ms. The normal 1KB candidate applies **719/720** published
IDs against original 720/720, and its source-age maximum rises 52.1898 ->
83.0827 ms. Normal 5KB delivery stays 720/720 both; its age maximum rises
49.0200 -> 65.0929 ms. These local publication/application observations are not
OBS/CDN network loss, and they do not prove the tiny source change caused host
pacing or every spike. They prevent claiming a repeatable safe improvement.

Nine guards fail: normal1KB complete-delivery and maximum-age; normal5KB
Decoder mean/p99/maximum, frame-CPU maximum and maximum-age; stress age-p95
and sender-deadline count. One primary Decoder mean must improve >=2% and all
others must not worsen; p99/max, frame CPU, age, bytecode and delivery guards
also apply. Normal cases require full published-cohort delivery. The intentionally
saturated stress case requires no worse loss fraction/deadline count than the
original. Scope clarifications and original policy versions were preserved
before the first Unity pilot, without observing results.

The original Decoder is verified in the repository, Native, current SDK and
minimum SDK projects. No candidate Runtime change is retained, and no timing
retry is performed. Fresh final Native GPU loopback (11 frames), current-SDK
16 and minimum-SDK3.9.0 13 client programs with automatic bindings pass.

## Evidence and next work

Evidence root: `F:/Unity/TSMP/Validation-Results/packed-pose-batch-20261104`.
`manifest.json` SHA256 is
`835ec6b81b489a4cf69a791e4f93514260a7ea9066bba0f910ed53b678819ac7`.
It pins all plans, sources and twelve executed harness files. Independent
packet/body, GPU completion/gate and pooled-summary scripts/results remain
beside all raw cases and source/config/program receipts.

Two external controller failures are preserved: exact-PASS parsing rejected the
actual successful `PASS: ...` Native result, then a missing parent directory
blocked product log creation before Unity launched. Only result parsing/path
handling changed; completed Packed/Native/VM checks were verified and never
rerun. No product A/B timing existed before either correction. The pure synthetic
0/1/2 completion-accounting and quaternion-oracle controls remain separate from
all natural observations.

The current evidence favors using the existing packed components and avoiding
unnecessary due independent fields. MinSendInterval can skip field serialization;
capture/callback savings depend on the Native/Udon path and other due fields on
the same target. Due OnChange fields still incur capture/comparison. Do not add a
CPU queue, slot cap or broad buffer policy to solve a rare maximum without a
relevant workload. Remaining useful measurements require representative full
rigs, actual VRChat player API capture, production refresh and video arrival
patterns. This automatic round does not establish their prevalence or FPS.
