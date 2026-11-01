# Additional loss reduction, 2026-10-31

The independent Encoder transmission-size target remains implemented. The Sparse
byte-combination experiment was rolled back on 2026-10-31 because its small
recovery gain did not justify the measured Udon CPU cost. Existing whole-payload
Refine recovery and CRC optimizations remain. See [the rollback record](SPARSE-ROLLBACK.md).
A separately measured FEC prototype remains a validation receiver and experimental
packet format, with no production execution cost.

The detailed receiver measurements below describe frozen pre-rollback Core
`9069850`; their results are preserved as historical evidence.

All work stays on `codex/tsmp-reliability`. No main merge, push, release or live
VRChat/VRCDN run was performed. The five repository baselines and package archives
are preserved under `F:/Unity/TSMP/Validation-Results/loss-reduction-20261031`.

## Rolled-back receiver experiment

After the ordinary decode and both existing recovery modes fail their protected
payload CRC, the receiver can combine at most two observed byte alternatives.
The union of differing positions must contain at most eight bytes, the payload
must be at most 8,192 bytes, and the captured GPU retry budget must be two. It
enumerates at most 384 hypotheses using CRC differences, accepts only one distinct
matching candidate, then performs the usual full payload CRC before application.
No sender pixels, codec shaders or production wire fields change.

At the measured snapshot, `recoverSparsePayload=true` was the default; false disabled this additional step.
The option is captured independently for each pending snapshot. Baseline buffers
are allocated/copied only after a protected failure on a material supporting the
existing recovery modes. Budget zero/one and unsupported Direct/RGB20 materials
do not activate the sparse path. At most one retained 8 KiB baseline per slot plus
small metadata/scratch arrays is added; array object overhead is additional.

Actual Native production, strict CRC, complete equality with independently saved
pre-raster originals:

| Inputs | Previous exact | Final exact | Additional complete original bytes |
| --- | ---: | ---: | ---: |
| Existing 66 profiles / 20,439 observations | 17,790 | 17,813 | 73,036 B |
| Previously screened 900 observations | 470 | 475 | 7,549 B |
| Untouched new-seed 900 observations | 473 | 477 | 10,397 B |

Previously correct observations lost and wrong accepted originals are zero in
these finite runs. The before values already include the previous Refine work.
The existing mixed dataset's rejections decrease 2,649 -> 2,626, a modest further
improvement; controls and deliberately difficult profiles are mixed in that total.
Missing/black observations and most severe compression failures remain.

GPU retry readbacks remain exactly 4,093 / 673 / 641 respectively, including
per-observation parity with the prior policy. No additional GPU pass/readback is
introduced. Previously successful observations perform no sparse search. The main
run makes 536 searches and 16,050 XOR hypotheses, with at most 159 in an observed
case. Its retained complete-original bytes rise 46,103,478 -> 46,176,514.

The new input uses PCG64 seed 2026103102, production Color256/RGB16 writers and
32/2,495/5,375-byte payloads. Its 90 source payloads differ from the corresponding
previous seed. PNG, x264 CBR 3.5M, NVENC CBR 3.5M, CRF18 and CRF28 files form the
900-observation comparison. Selection parameters were frozen before its results
were inspected. These are offline observations, not unique applied events or Hz.

## Historical processing cost of the removed feature

Measurements execute freshly compiled Udon client bytecode in the editor VM on
this Windows machine. After five warm samples, thirty samples are measured per
case/path in a quiet window. These costs exclude allocation, GPU work, callback
waiting, application work and actual VRChat frame timing.

| Successful sparse fixture, copy + two scans + search + final full CRC | Mean | p95 |
| --- | ---: | ---: |
| 32 B, two differing positions | 0.381 ms | 0.389 ms |
| 2,495 B, two differing positions | 2.186 ms | 2.355 ms |
| 5,375 B, two differing positions | 4.318 ms | 4.621 ms |
| 8,192 B, two differing positions | 6.319 ms | 6.672 ms |
| 5,375 B, all 384 hypotheses and successful full CRC | 6.742 ms | 7.305 ms |
| 8,192 B, all 384 hypotheses and successful full CRC | 9.561 ms | 10.336 ms |

The last two are deliberately saturated successful-search fixtures, not a proven
upper time bound. Their observed maxima are 7.309 / 10.362 ms. A rejected saturated
search skips the final CRC and therefore must not be used as the total worst-cost
example. Normal successes did not run this work. This extra CPU path has now been
removed; these measurements are not current decoder timings.

The first linear suffix implementation was too costly in the VM. Logarithmic CRC
arithmetic and exact eight-byte equal-run scans replace it. Earlier/pilot runs are
retained; the report uses quiet final and targeted-success measurements. No claim
of increased whole-decoder or VRChat FPS follows from these results.

## Implemented transmission target

`targetPayloadBytes` is independent of backing-buffer growth. Zero preserves the
previous policy; positive values constrain the complete network payload, including
its eight-byte network header and existing raster/protocol limits. The limit is
captured for the current encode operation. Existing priority/rotation handles
eligible fields. Oversized fields remain deferred, and queued RPCs retain their
repeat budget after a target reduction, resuming when the target increases.

Native 336 and compiled Encoder VM 163 assertions pass. Zero-target output matches
the frozen previous frame builder for 64 inputs. A controlled fixture changes
147 B / three fields to 29 B / one field while eligible small fields alternate.
Large fields are not fragmented; a deferred head RPC can block later RPCs. This
feature has no measured video-loss percentage or automatic target-selection claim.
See [PayloadBudget/RESULTS.md](../PayloadBudget/RESULTS.md).

## Measured FEC prototype, separate from the production receiver

The focused prototype adds shortened Reed-Solomon body parity, spatial
interleaving and a protected fixed metadata bootstrap. Actual production writers,
encoded videos and GPU outputs feed a Python reference receiver, which requires
received-header integrity, metadata/body correction, original CRC, reconstructed
outer CRC and complete equality with independent originals.

| Same new-input x264 profile | Pre-rollback sparse receiver | FEC body RS16 | FEC body RS32 |
| --- | ---: | ---: | ---: |
| Color256 / CBR 3.5M | 55/90 | 90/90 | 90/90 |
| Color256 / CRF18 | 33/90 | 90/90 | 90/90 |
| RGB16 / CBR 3.5M | 25/90 | 57/90 | 60/90 |
| RGB16 / CRF18 | 3/90 | 13/90 | 22/90 |

Body RS16 adds 8.45% raw packet bytes across this mixed-size sequence; RS32 adds
16.75%, including metadata protection. A 32 B original becomes 84/100 B, so these
averages must not be applied to short packets. CBR encoder arguments match apart
from paths; exact achieved video packet rates differ. CRF comparisons have higher
achieved rates after adding parity and are not fixed-bandwidth comparisons.

The current-input comparison loses no previously successful Color256 observation.
RGB16 RS32/CRF18 loses one current sparse success while adding twenty others;
RS16 has no paired loss here. RGB16 CRF18 successes remain exclusively 32 B. Wrong
accepted originals/packets are zero. Thus the larger net success count does not
justify describing every FEC setting as regression-free.

V2 was selected after v1's metadata failures on these same sequences; it is an
exploratory refinement. Runtime FEC, Udon processing cost, state/RPC integration
and service latency are not established. Both experimental payload types are
rejected by the real existing decoder as unsupported. The prototype changes no
production protocol. See [FecFeasibility/RESULTS.md](../FecFeasibility/RESULTS.md).

## Pre-rollback verification and retained history

- Core integration passes 1,630 assertions, including 28 actual compressed sparse
  winners applying their complete original RawBytes fields, captured on/off policy
  in both directions, prediction/combined output and duplicate suppression.
- Serial Gamma and Linear Snapshot runs each pass 216 changing-source cases plus
  CRC/state/RPC, capacity, lifecycle, frame-window and Float32 integration checks.
- Full Udon client compilation passes for sixteen TSMP programs with bindings.
  Pure CRC/recovery math and actual helper VM checks also pass, including deliberate
  ambiguous-candidate rejection. Fresh Decoder heap inspection confirms the new
  default true, payload retry budget two, header recovery false and slot array
  sizes; bytecode is 152,116 B. These are automated checks, not live-client tests.
- `Audit-Results.py` independently recounts the three production comparisons and
  checks every retry budget, previous exact result and accepted-byte verdict.
  `Audit-Final.py` verifies the five main refs, branch/commit ancestry, absence of
  merges, protected package assets, existing GUIDs and dedicated-project sources.
- The first Core safety launch recorded a new-helper import/compilation-order
  error; a fresh unchanged-source run passes without suppressing compiler errors.
  The initial concurrent Snapshot run timed out at its first header readback;
  unchanged-source serial Gamma/Linear runs pass. The exact timeout cause is not
  isolated. All failed/pilot folders remain. Auxiliary Snapshot copy timings are
  excluded from recovery performance claims, including unavailable samples.
  An initial heap inspector did not load serialized bytecode in its fresh process;
  the corrected inspector loads it and passes eleven checks. Unity's incidental
  sample-asset serialization was restored to its baseline content, and the new
  helper's normalized default metadata was resynchronized with its unchanged GUID.

Primary evidence: `loss-reduction-20261031`, `sparse-recovery-20261031`,
`payload-budget-20261031` and `fec-feasibility-20261031` beneath
`F:/Unity/TSMP/Validation-Results/`. The final source audit and `summary.json` in
the overall evidence root bind the original pre-rollback commits and required
checks. Post-rollback evidence uses the separate `sparse-rollback-20261031` root.
