# Color256 attribution and polling follow-up, 2026-11-01

The current optimized Color256 has measurable duplicate-work savings in a
source30/render90 configuration, but this does **not** justify a universal
polling limit. Three alternating comparisons reduce total VM elapsed time per
second by **5.73–7.29%** with every measured publication and RPC delivered.
Higher-rate and lower-render controls retain missing field IDs, and a jitter
control increases source-age p99. Product polling defaults remain unchanged.

Nested codec timing identifies payload conversion as roughly60–65% of the
observed Encoder call in these two processes. No GPU writer, CRC removal,
retry change or product source overlay was introduced. The remainder is not
separately attributed to CRC, serialization, comparison or texture upload.

## Initial current-code polling and attribution

Evidence: `F:/Unity/TSMP/Validation-Results/workload-followup-20261101/`
`color256-followup/vrc-initial`.

The frozen15-case plan measures144 seconds in one fresh Unity2022.3.22f1
SDK Editor process, D3D11/Gamma, i9-13900K/RTX4090. Each case uses new client VM
instances, two warm seconds, an ongoing one-second publication tail, and real
VRC GPU readback callbacks. This is one process with alternating paired case
order, not three independent fresh-process repetitions.

**56,237 assertions PASS**. The raw analyzer passes35,892 top-level events,
16,345 frame rows,392 unchanged product sources,23 unchanged configuration files,
and exact executed-harness receipts. Every accepted field is checked byte by
byte against its received ID, including two applications in one Decoder call.
The independent stage audit also verifies82,730 nested interval receipts,
containment/nonoverlap, gross/net arithmetic and6,295/6,295 full-field checks.

| Pair | Every-render VM ms/s | Poll60 VM ms/s | Reduction | Source-age p95 old→poll ms |
| --- | ---: | ---: | ---: | ---: |
| 0, every-render first | 248.445 | 230.336 | 7.29% | 44.893→39.028 |
| 1, poll first | 242.878 | 226.287 | 6.83% | 44.804→38.905 |
| 2, every-render first | 243.065 | 229.129 | 5.73% | 44.917→38.996 |

All six paired cases publish and receive360 measured5,120-byte fields each:
**2,160/2,160**. Each executes9/9 finite four-copy RPCs:54/54 total. Published
field ID sets match within each pair, and sender deadline misses are zero.
Frame VM p99 reductions are0.200/0.088/0.100ms; this is a modest frame-time gain.
Polling is explicitly60Hz, independent of the30Hz source and90Hz render target.

The broader delivery controls remain part of the result:

| Control | Measured fields received/published | Sender deadline misses |
| --- | ---: | ---: |
| Source60/render90, every-render | 473/480 | 0 |
| Source60/render90, poll30 | 240/480 | 0 |
| Source60/render90, poll60 | 469/476 | 4 |
| Source30/render60, every-render | 237/240 | 0 |
| Source30/render60, poll30 | 239/239 | 1 |
| Jitter, every-render / poll30 | 240/240 each | 0 each |
| Hold plus new stream, every-render / poll60 | 225/225 each | 0 each |

Source60/poll30 intentionally under-samples and demonstrates why low source age
alone does not prove complete delivery. The two other source60 results do not
establish a causal polling regression from one short control each. At render60,
poll30 increases frame VM p99 from7.561 to9.117ms. In the jitter control it
reduces total VM time but increases source-age p99 from55.975 to69.919ms. During
the deliberate half-second source hold, poll60's age p95 is144.843 versus
133.724ms; these age percentiles include the intentional hold.

All **108/108 accepted RPCs execute once** across the entire initial matrix.
The restart cases execute IDs1/2/3 once in stream1 and again in stream2. The
frozen cadence queues RPCs before and after the held interval, not inside it;
this is not a queue-during-hold test or compressed-network-loss test. Finite
copies still do not provide acknowledgement-backed delivery.

Across all controls, measured fields total4,748/5,005 and complete warmup/tail
traces total6,295/6,642. Intentional under-polling and the other observed missing
IDs are retained; the whole experiment is not described as lossless. PASS means
valid measurement and accepted-byte/order checks, not every adoption guard passes.

## What the Encoder timing establishes

The six paired cases pool2,160 actual EncodeNow calls at **4.998ms mean**.
In a representative first case, whole EncodeNow averages5.066ms; nested codec
payload writing averages3.021ms and query0.009ms, leaving2.036ms of other work.
Codec calls account for59.8–60.7% across the six pairs.

Timers wrap actual nested codec client VM custom events. The outer Encoder
already includes those events once. Measured nested observer bookkeeping is
subtracted once, and raw gross/net intervals are retained. The first case's
gross5.082ms becomes net5.066ms; only0.016ms is subtracted. Adjacent-clock and
host-sleep positive controls validate the clock, not allocation accounting.

The earlier Color256 paired optimization report measured about9.2ms at5KB.
Despite matching original product sources and bytecode, the current initial
process measures about5ms. **This historical absolute timing difference is not
explained and is not a new product speedup.** The next control checks whether
the codec observer itself materially changes execution.

The full-field receiver check enforces length5,120. Block-symbol encoding is
configured through the existing default and assigned block-expansion material;
the private palette-path flag is not separately logged for each frame. Current
source and the earlier full-buffer/client-VM parity evidence establish that
implementation, without a new raster-equivalence claim from these timings.

## Observer control and post-binding-fix GPU confirmation

Evidence: the sibling `color256-followup/vrc-wrapper-control` directory. Its
separately frozen plan runs ON/OFF/OFF/ON for8 measured seconds each in one fresh
process. OFF removes the codec VM wrapper completely by supplying role:null;
outer Encoder/Decoder timing and receiver checks remain active. It changes no
polling cadence, input size or RPC policy.

This process uses the final binding-cache Core candidate. Its source differs
from the initial polling run. These two experiments are not pooled, and their
absolute difference is not assigned to the binding fix.

**12,186 assertions PASS**, with9,238 top-level events,3,959 frame rows,
392 unchanged sources and23 unchanged configurations. All four cases receive
**960/960 measured5KB fields and24/24 RPCs**, with zero sender deadline misses.

| Observer | Case mean EncodeNow ms |
| --- | ---: |
| ON, first | 6.5393 |
| OFF, second | 6.4912 |
| OFF, third | 6.5604 |
| ON, fourth | 6.5873 |

The two ON cases pool6.5633ms versus OFF6.5258ms, a difference of **0.0375ms
(+0.575%)**. This does not show a multi-millisecond observer effect in the
current candidate process. It does not resolve the older9.2ms absolute result.
ON payload writing averages4.262/4.304ms, about65% of whole EncodeNow, with
another2.27ms outside codec custom events. A future GPU writer must improve
whole-call and frame behavior; this fraction is not its predicted speedup.

## Reproduction receipts and decision

The initial plan SHA256 is
`1ac6f2f9f3703652a9fe537505f02a704a74adafe6454c20abe5ee65fd186c48`;
the observer-control plan is
`30632381da68773a996105fc3fa325fd14d57961a9cf0c0ec2037aed60eba30a`.
Both runs freshly compile client programs after entering Play Mode and load
the serialized client programs. First host-step or async-observer exceptions
are propagated to a failed result, rather than merely logged by Unity.

After measurement, one trailing whitespace-only line in the repository driver
was cleaned up. Archived executed harness bytes and their receipts are retained.

| Program | Initial SHA256 | Observer-control SHA256 |
| --- | --- | --- |
| Encoder | `90c142e388c90447ec92e9a670c11544034b78ab79a53bf5d13d90c1bf308b8c` | `428c70ce2917eed0a77ff7f78e646a0445e2900732b4a6c97e28048168c7d144` |
| Decoder | `1cf211baab412d4d22c80396f3a3852c5ece8e29ebbd9c4fd8b0847c9fc91f42` | Same |
| Color256 | `bc1b66f25419fc3ed5145038c8233b7d904617973eb61fece0075cf3ee8b3930` | Same |

Color256 source remains
`63cf1f4d725774bc923e9cc2f302b89d633640d823ed5fa9bb91329e42e64bfd`.
The control Encoder source is
`8292b7b659f6ef8c2daee6dd27c6ee44af4319086717bc0706cb29297828af2c`.

The product polling default, CRC checks and retries are retained. The observed
30/90 polling opportunity can be revisited with actual frame-arrival information
and broader delivery/age evidence. Codec payload work is a plausible target for
an optional GPU prototype, but no new writer or architecture is justified by a
stage fraction alone. These are SDK Editor VM/GPU measurements, not populated
world/live VRChat FPS, Quest, OBS or CDN results. No allocation-free claim follows.
