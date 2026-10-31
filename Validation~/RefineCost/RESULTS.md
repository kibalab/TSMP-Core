# Refine full-path processing cost, 2026-10-31

The current two-retry receiver costs about **3.9–4.2 ms of accumulated decoder
VM CPU work** for the selected clean 5,375-byte inputs, about **7.4–7.5 ms** when
the first alternate mode succeeds, and **10.6–10.7 ms** when the second succeeds.
These are sums over submission and multiple asynchronous callbacks, not single
callback spikes or actual VRChat frame times. Product defaults were not changed.

## Scope and checks

Core `b5f35755c99025646f9ede923905daab33caa5f4`, after the structural-preflight
fix and Sparse rollback; Unity 2022.3.22f1, Direct3D11/Gamma, RTX 4090. The actual
freshly compiled client Decoder, codec and RawBytes receiver programs execute in
the SDK Editor Udon VM with real VRC GPU readback callbacks. Native source helpers
only prepare cloned resources and independently validate results.

The fixed matrix has nine raster conditions and budgets zero/one/two, with five
warm and thirty measured invocations per condition/budget: **945 invocations,
810 measured**. All use independently saved 5,375-byte NetworkFrames containing
a complete 5,350-byte RawBytes field. Clean conditions share original payload
content across codecs. Recovery conditions were selected from earlier Native
observations, so this is a representative diagnostic, not a new accuracy holdout.

- PASS **7,303 assertions**: original header/payload/frame equality, full state
  application once on success, no application on failure, exact retry limits,
  header retries off and separate ordinary duplicate-on controls.
- PASS independent audit of sample counts, every timed VM event, arithmetic sums,
  budget verdicts, input hashes and nearest-rank quantiles.
- PASS **360 installed Runtime/Editor source hashes** stable before/after.
- PASS **23 captured VRC configuration files** unchanged, including manifest,
  package lock and existing ProjectSettings files.
- The run passes on its first launch; there are no omitted failed measurement
  runs. Other owned measurement loads were stopped; unrelated user Unity remained.

## Current default two-retry results

All times are milliseconds. The CPU column sums the decoder's `Interpret` work
for submission and every callback once, including nested codec/receiver work and
synchronous driver calls. Callback count is per invocation after warmup. Latency
ends at the final callback VM `Interpret` return, before remaining outer event
dispatcher bookkeeping; it includes asynchronous waits.

| Selected 5,375-byte condition | Exact / 30 | CPU mean / p95 | Callbacks | Wall latency mean / p95 |
| --- | ---: | ---: | ---: | ---: |
| Luma4 clean | 30 | 3.919 / 4.231 | 1 | 16.683 / 24.626 |
| Color256 clean | 30 | 4.083 / 4.581 | 1 | 15.985 / 16.799 |
| RGB16 clean | 30 | 4.206 / 5.189 | 1 | 16.539 / 24.282 |
| Color256 first alternate succeeds | 30 | 7.412 / 7.808 | 2 | 24.702 / 26.159 |
| RGB16 first alternate succeeds | 30 | 7.497 / 8.279 | 2 | 25.731 / 32.836 |
| Color256 second alternate succeeds | 30 | 10.596 / 11.438 | 3 | 34.204 / 41.572 |
| RGB16 second alternate succeeds | 30 | 10.706 / 10.918 | 3 | 33.382 / 35.113 |
| Color256 all modes fail | 0 | 10.336 / 10.869 | 4 | 41.220 / 42.809 |
| RGB16 all modes fail | 0 | 10.527 / 11.087 | 4 | 41.350 / 42.878 |

Successful repeated conditions retain prediction after warmup. Repeated failures
invalidate it, adding the separate header readback. This explains the different
callback counts; the harness does not force all policies onto an artificial
common path. The measured clean mean submission cost at budget two is
0.492 / 0.546 / 0.588 ms for Luma4 / Color256 / RGB16. Remaining CPU is callbacks.

Individual callback p95 for the second-mode successes is **3.648 / 3.615 ms**
(Color256 / RGB16), and its observed maxima are 4.466 / 3.741 ms. The largest
individual callback anywhere in the measured matrix is 5.154 ms. These observed
samples do not bound arbitrary callback cost. Summed CPU does not establish how
callbacks would coincide within render frames under concurrent-slot workloads.

## Budgets zero, one and two

Mean accumulated VM CPU in milliseconds; letters identify exact acceptance (E)
or rejection (R). Each cell represents thirty invocations of that same condition.

| Condition | Budget 0 | Budget 1 | Budget 2 |
| --- | ---: | ---: | ---: |
| Luma4 clean | 3.884 E | 3.922 E | 3.919 E |
| Color256 clean | 4.080 E | 4.086 E | 4.083 E |
| RGB16 clean | 4.144 E | 4.040 E | 4.206 E |
| Color256 first alternate succeeds | 3.843 R | 7.301 E | 7.412 E |
| RGB16 first alternate succeeds | 3.821 R | 7.487 E | 7.497 E |
| Color256 second alternate succeeds | 3.596 R | 6.861 R | 10.596 E |
| RGB16 second alternate succeeds | 3.791 R | 7.122 R | 10.706 E |
| Color256 all modes fail | 3.801 R | 6.900 R | 10.336 R |
| RGB16 all modes fail | 3.866 R | 7.200 R | 10.527 R |

Reducing the budget saves work for damaged inputs, including irrecoverable ones,
but sacrifices alternate-mode successes. Clean inputs perform no retries, and
these measurements show no systematic clean-path benefit from reducing its
configured budget. Differences in this single thirty-sample session are not
statistical significance claims.

The independent reanalysis of the **previous** 20,439-observation Native CSV
counts 17,528 ordinary successes, 125 first-mode successes and 137 second-mode
successes, with 4,093 extra payload readbacks. CBR gains split 72 / 68; OBS gains
split six / three. These are archived observation counts, not unique events or a
new full-corpus Udon measurement. A blanket one-retry policy would forgo a material
part of that measured recovery benefit. Keep defaults unchanged until a concrete
application's useful-update rate and latency requirements justify a different
existing budget.

## Preparation and limits

Input upload, compilation, initial graphics preparation, heap result checks,
file IO and the first five invocations are outside measured samples. The first
broad RGB16 graphics prewarm took **96,271.734 ms**; later RGB16 prewarms took
about 9–11 ms. This cost is preserved in `result.txt`, not hidden or included in
the steady-state quantiles. Its exact shader/driver/preparation cause was not
isolated. The benchmark therefore does not certify cold-start behavior in a real
client or claim that a live VM would tolerate that preparation delay.

No GPU timestamp is measured here. CPU/latency must not be re-labelled GPU time
or added to previous GPU pass timings as though they were disjoint frame costs.
The harness serializes requests, deliberately repeats fixed inputs with duplicate
skipping off, and does not measure two-slot saturation, changing-video delivery
Hz, long-term latest-state age, overlapping callbacks in a render frame, a
populated world, different GPUs or real VRChat/VRCDN performance. Host monitoring
and Editor scheduling can affect the recorded timing. No RPC workload is included
in these particular timing fixtures.

Evidence root:
`F:/Unity/TSMP/Validation-Results/followup-performance-20261031/refine-cost`.
`vrc-initial/samples.csv` and `vm-events.csv` retain all samples/events;
`independent-summary.json` contains every budget's mean/p50/p95/max, individual
callback quantiles and audit totals. `plan.json`, source snapshots, bytecode
hashes and before/after configuration receipts bind the measured source/inputs.
No runtime/default/protocol change, main merge, push or live client run was made
by this work unit. See [README.md](README.md) for reproduction.
