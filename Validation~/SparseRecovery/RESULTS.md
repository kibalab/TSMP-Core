# Sparse receiver recovery, 2026-10-31

**Historical measurements at `9069850`, before the Sparse-only rollback.** This
feature is no longer implemented or configurable in the current runtime. Existing
Refine recovery remains. See [the rollback record](../LossReduction/SPARSE-ROLLBACK.md)
for the decision and post-rollback validation. Original results below are retained
without changing their measured outcomes.

The receiver now recovers **23 additional complete original payloads** from the
same 20,439 saved observations, five from the previously screened 900-input
sequence, and **four from an untouched new 900-input sequence**. No previously
exact output was lost and no wrong payload was accepted in these finite tests.
The additional recovery uses the existing three GPU outputs and adds **no GPU
pass or readback**. Failure-only CPU work has a measurable cost, including up to
10.36 ms observed in the saturated 8,192-byte Udon editor-VM test below.

## Adopted change

`recoverSparsePayload` defaults to true. After ordinary decoding and both
existing recovery modes fail, the receiver can combine observed byte values if
the protected payload is at most 8,192 bytes and the outputs differ at no more
than eight positions. Each hypothesis changes at most two bytes of an observed
candidate. The maximum is 384 hypotheses, including duplicates. Exactly one
distinct CRC-matching candidate is required, followed by the usual complete
payload CRC validation before acceptance, state application or RPC delivery.

This is bounded mixing of already observed byte candidates. It does not export
new GPU top-two symbol/confidence values. CRC collisions remain possible when
the original is outside the candidate set; zero wrong acceptance here is a
finite observation, not proof for arbitrary data.

The option and recovery budget are captured per request. Two decoder slots have
independent baseline buffers and flat slices of disagreement metadata. Search
scratch is reused only during synchronous completion. Baselines are allocated
and copied only on supported protected-payload failure; they retain at most
8,192 bytes per slot. Budgets zero/one, unsupported materials, unprotected packets
and larger payloads keep their previous behavior. Normal successful frames do
not copy/scan/search. CRC polynomial differences and XOR hypotheses avoid
hundreds of complete CRC calculations, and equal eight-byte runs use exact word
comparisons. Sender pixels, palettes, wire format and the CRC algorithm remain
unchanged.

Runtime work unit: `eea6a84896980349dbc2e868e4be5c654eb4b7a2`. The baseline is
Core `f85426f6416e62f2d011da18a89263d1eab87a31` and the same prior codec package
commits used by the preceding robustness report.

## Actual production results

Unity 2022.3.22f1, Windows, D3D11/Gamma, RTX 4090. The actual Native TSMPDecoder
requires protected payload CRC and compares every complete output with an
independently saved pre-raster payload. Parse-only replay applies no state/RPC;
separate integration tests cover application. Datasets contain controls and
deliberately difficult profiles, so aggregate rates are not service settings or
live delivery rates.

| Same input observations | Prior recovery exact | Sparse exact | Additional original bytes in complete exact payloads |
| --- | ---: | ---: | ---: |
| Main 66-profile replay, 20,439 observations | 17,790 | 17,813 | 73,036 B |
| Previously screened seed, 900 observations | 470 | 475 | 7,549 B |
| Untouched seed 2026103102, 900 observations | 473 | 477 | 10,397 B |

Main complete exact original bytes improve 46,103,478 -> 46,176,514 B. The
previously screened sequence improves 1,025,650 -> 1,033,199 B, and the fresh
sequence 1,036,015 -> 1,046,412 B. The new seed was generated only after the
8-position/2-replacement/8,192-byte policy had been selected. It uses 90 native
720p frames per codec at 30 fps, 32/2,495/5,375-byte payloads, PNG controls and
x264 CBR3.5M, NVENC CBR3.5M, CRF18 and CRF28 profiles. The older 900-input set
was used during candidate screening and is explicitly not an untouched holdout.

Every previously exact observation stays exact. Wrong accepted bytes and sparse
searches on ordinary previously successful frames are zero in all three audits.
Extra payload readbacks are unchanged: main 4,093, screened seed 673 and fresh
seed 641. Header recovery remains off. Main uses 536 sparse searches and 16,050
XOR hypotheses, with an observed maximum of 159 hypotheses in one input. The
corresponding fresh figures are 83 searches, 2,694 hypotheses and a maximum of
108. These counters are CPU hypotheses, not new GPU decoding attempts.

The older paired sequence reproduces all 900 previous production verdicts,
byte mismatches, frame identities and retry counts with sparse recovery disabled.
Raw GPU screening has 3,068 residual inputs and 9,204 forced mode outputs. It
finds 32 additional exact payloads, but four have invalid received headers and
do not count as production recovery. Its six already-exact forced payloads
likewise do not imply an accepted frame. Actual production yields the 23+5
gains above, rather than the larger raw diagnostic total.

## Failure-only Udon processing cost

Client bytecode runs in the SDK editor Udon VM, with other owned measurements
stopped. Each final case/path has five warm and thirty measured samples. The
full path below includes baseline bulk copy, both disagreement scans, candidate
search, and a complete CRC on a recovered payload. It excludes allocation,
GPU/readback waiting and application work, and is not actual VRChat FPS.

| Successful recovery fixture | Hypotheses | Mean | p95 | Maximum observed |
| --- | ---: | ---: | ---: | ---: |
| 32 B, two differing positions | 24 | 0.381 ms | 0.389 ms | 0.389 ms |
| 2,495 B, two differing positions | 24 | 2.186 ms | 2.355 ms | 2.461 ms |
| 5,375 B, two differing positions | 24 | 4.318 ms | 4.621 ms | 4.644 ms |
| 8,192 B, two differing positions | 24 | 6.319 ms | 6.672 ms | 8.035 ms |
| 5,375 B, saturated search and unique winner | 384 | 6.742 ms | 7.305 ms | 7.309 ms |
| 8,192 B, saturated search and unique winner | 384 | 9.561 ms | 10.336 ms | 10.362 ms |

These are finite fixture times, not a universal upper time bound. The separate
saturated 8,192-byte rejection fixture averages 4.672 ms, p95 5.173 ms; it has
no winner and skips the final complete CRC, so it is not the cost of the most
expensive successful path. The setting can be disabled when failure-time CPU
latency matters more than these additional recoveries. Normal success takes no
sparse search or additional checksum.

Initial bytewise/suffix prototypes and timing pilots remain in the evidence.
The first implementation's linear-length suffix advance was rejected for cost;
the retained polynomial-power implementation passes the same arithmetic tests.
`udon-vm-final` overlaps CPU audit work and is retained as a pilot. Final timing
uses `udon-vm-final-quiet` and the targeted
`udon-vm-budget-success-quiet`; their policy/runtime sources are identical.

## Correctness and limits

- Pure C# checks pass 2,907 assertions: 1,152 single-byte CRC-difference vectors,
  149 comparisons with independently enumerated complete candidate payloads,
  bounds, slot offsets and rejection without mutation. A constructed pair of
  distinct CRC-matching payloads must be rejected as ambiguous.
- Final Udon VM coverage passes 2,102 assertions for the three entry paths and
  another 352 for the two saturated successful cases. Fresh probe bytecode is
  19,316 bytes. This is automatic VM execution, not a VRChat client run.
- Root integration checks pass 1,630 assertions, including 28 actual compressed
  sparse recoveries applied through a real RawBytes binding, captured option
  changes, budgets zero/one/two, prediction/combined paths, both slots with
  different sizes, source mutation, cancellation and duplicate application.
- Gamma and Linear Snapshot suites each pass their 216 changing-source cases
  plus existing integrity/application integration checks. The first concurrent
  Gamma attempt timed out at the first header readback and is retained. Serial
  reruns pass; the exact cause is not isolated and no runtime change or image
  rebaselining was used to obtain those passes.
- Full UdonSharp compilation/bindings pass for 16 TSMP programs. Existing main
  refs, shader names and codec GUIDs remain unchanged; no merge or push occurs.
- Compiled Decoder heap inspection passes 11 assertions: sparse recovery true,
  retry budget two, header recovery false, captured-slot arrays length two,
  positions/mode-one arrays length 16, CRC array length six and both baseline
  buffers null before failure. Decoder bytecode is 152,116 bytes. The initial
  inspector queried the SDK's unloaded in-memory program property and is
  retained as a failure; explicitly loading its serialized program before the
  fresh inspection passes without changing runtime code.

CRF28, black/missing video frames, RGB20's ordinary H.264/NV12 failure and
information already collapsed by compression retain their previous limits.
Already passing OBS nonblack observations remain passing; observation counts
include repeated/held frames and are not unique event/update throughput.
Actual VRChat/VRCDN, source age, player frame time, other devices/APIs and IL2CPP
were not measured. Sender payload budgets and experimental FEC have separate
validation and must not be credited as gains of this receiver change.

## Evidence

Evidence root: `F:/Unity/TSMP/Validation-Results/sparse-recovery-20261031`.

`audit-main/summary.json`, `audit-screened-holdout/summary.json` and
`audit-fresh/summary.json` independently recompute verdicts, original bytes,
regressions and bounded counters from actual production CSVs.
`prior-holdout-replay-audit.json` compares all 900 older baseline observations.
`mix-screen-header-crc` consumes only the protected header checksum when
generating offline candidates; source payload bytes audit the selected result.

`residual-probe/raw-dumps.csv` maps every saved GPU `.bin` to independent
input/header/payload paths. Runtime hashes in all four replay runs identify
the frozen/raw and installed/production sources. Explicit package resolution
was subsequently added to make hashing clear for external file dependencies;
the original runs already contain their resolved runtime hashes. Frozen baseline
archives remain unchanged. `production-source-receipt.json` is a supplementary
source snapshot captured during replay after runtime freeze, rather than a
pre-run receipt. Original failed/pilot logs are retained.

Final helper costs are independently summarized in
`udon-vm-final-quiet-summary.json` and
`udon-vm-budget-success-quiet-summary.json`. `decoder-heap-final/result.txt`
records the actual compiled defaults and storage layout.

Use [README.md](README.md) for reproduction and the prior
[receiver robustness report](../RefineRobustness/RESULTS.md) for the baseline.
