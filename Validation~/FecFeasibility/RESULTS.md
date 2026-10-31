# FEC feasibility, 2026-10-31

**The measurement-only Color256 FEC prototype restored all ninety original
payloads in both tested x264 CBR 3.5 Mbps and CRF18 sequences.** This is actual
production-raster/video/GPU corruption followed by a Python reference FEC receiver.
It is not an implemented TSMP runtime FEC path, Udon performance result, live
service test, or production protocol allocation.

## Actual encoded-path comparison

Unity 2022.3.22f1 / Direct3D11 / Gamma / RTX 4090. Independent NumPy PCG64 seed
2026103102 generates ninety source payloads cycling 32/2,495/5,375 bytes at thirty
frames per second. None equals its corresponding prior-seed payload. Both actual
codec writers receive those same original bins. Five frozen package archives bind
the source versions; 184 selected package-source comparisons match the archives.

The table names both baselines: the **frozen prior decoder's three complete global
decode modes** and the **actual current SparseRecovery production receiver** on
the same ninety originals. The latter is independently joined by profile/file and
original length from `production-fresh/production-frames.csv`. The experimental
receiver tries the same three shader modes and can additionally correct the FEC
packet; it does not perform sparse mixing after FEC failure. Ordinary received
header mode zero must pass CRC and match the independent reference exactly before
any payload result counts. It then requires metadata correction/CRC, body RS,
original-payload CRC and the protected outer packet CRC after complete re-encoding.
Every accepted original and reconstructed packet matches independent source bytes.

| Codec / x264 veryfast profile | Frozen three-mode baseline | Current SparseRecovery | v2 body RS16 + metadata RS16 | v2 body RS32 + metadata RS16 |
| --- | ---: | ---: | ---: | ---: |
| Color256 / CBR 3.5M | 54/90 | 55/90 | 90/90 | 90/90 |
| Color256 / CRF18 | 32/90 | 33/90 | 90/90 | 90/90 |
| RGB16 R4/G4/B4 / CBR 3.5M | 24/90 | 25/90 | 57/90 | 60/90 |
| RGB16 R4/G4/B4 / CRF18 | 2/90 | 3/90 | 13/90 | 22/90 |

All five hundred forty PNG control observations (baseline and both v2 settings,
both codecs) restore every byte. Wrong accepted originals/packets are zero in
the finite experiment. V2 loses no previously exact observation against the
frozen three-mode baseline. Against the current production receiver, all RS16
profiles and both Color256 RS32 profiles retain previous successes. **RGB16 RS32
CRF18 gains twenty cases but loses one current SparseRecovery success** (net +19).
Therefore v2 must not be described as preserving every current success across all
settings. All received headers are exact in these profiles.
RGB16's CRF18 successes are exclusively 32-byte payloads; its larger payloads
remain unusable here. The result does not establish CRF28 behavior.

Against current SparseRecovery, Color256 complete-original-byte retention rises
from 29.15% to 100% for CBR and 8.76% to 100% for CRF18. RGB16 CBR retention rises
from 3.45% to 32.47%/38.05% for
RS16/32; its CRF18 retention remains below 0.3%. Retention counts only bytes in
fully restored originals, not correctly guessed fragments.

The v2 choice followed a concrete weakness in v1 on these same sequences; it is
an exploratory refinement, not a new independent holdout for the metadata change.
Fresh source generation preceded the new sparse policy's validation, and its
results were withheld from sparse policy selection until that policy was frozen.

## Cost and compatibility

Each ninety-frame sequence carries 237,060 original bytes. V2 transmits 257,100
bytes with body RS16 (**+8.45%**) or 276,780 with RS32 (**+16.75%**), including the
fixed thirty-six-byte protected bootstrap. This is raw packet overhead, not a
percentage increase in a fixed-CBR video rate. A short 32-byte original becomes
84/100 bytes, so the mixed-data averages hide substantial short-payload overhead.

Measured video packet rates in Mbps, excluding container/network overhead:

| Codec / profile | Baseline | v2 RS16 | v2 RS32 |
| --- | ---: | ---: | ---: |
| Color256 / CBR 3.5M | 3.28760 | 3.28848 | 3.28957 |
| Color256 / CRF18 | 2.46400 | 2.68449 | 2.87369 |
| RGB16 / CBR 3.5M | 3.28382 | 3.28315 | 3.28501 |
| RGB16 / CRF18 | 1.60014 | 1.73276 | 1.85649 |

The CBR comparison uses identical encoder arguments apart from input/output paths;
its exact achieved rates differ slightly. CRF18 is a quality setting and is not
a fixed-bandwidth comparison. Three-second sequences do not establish a sustained
service cap or arbitrary-content reliability.

V1 uses experimental payload type `0xF100`/layout `0xFEC1`. V2 uses `0xF101`/
`0xFEC2` and a fixed RS(36,20) metadata bootstrap. These are unallocated measurement
markers. The real previous TSMPDecoder rejected twelve pristine v1 and twelve v2
inputs with valid received headers as unsupported payload types, before requesting
payload recovery. A production extension still needs a protocol decision, bounded
Udon/GPU computation, state/RPC integration, cancellation and source-age validation.
No runtime package or wire format was changed by this experiment.

## Why protect metadata separately

V1 left twenty metadata bytes protected only by CRC. Color256 CBR improved
54 -> 86/87 and CRF18 32 -> 81/81, but each setting lost one formerly successful
observation in each profile. All remaining Color256 v1 rejections and every
paired regression in both codecs were metadata failures. Its body parity could
not help a receiver that could not trust the parity/length description.

V2 corrects the fixed bootstrap before reading those fields. In the measured
Color256 files this removes the remaining failures. RGB16 still has body errors
beyond the tested correction capacity. No further algorithm variants were selected
after this focused metadata experiment.

## Conditional screening and reference checks

Before the actual encoded experiment, 3,068 old residual inputs supplied actual
mode-two GPU payload bytes. A perfect-parity replay screens 16/32/48 parity bytes
per shortened RS word and two old-error assignments. For the 489 CBR residuals,
RS16's conditional bound covers 404 contiguous or 436 round-robin cases; RS32
covers 449/467. The old holdout's corresponding 106 cases give 71/78 and 82/92.
These are **conditional bounds, not observed delivery gains**: parity is assumed
perfect and the old payload errors are reused. Header-invalid raw probes are
separately counted. The actual encoded experiment above replaces those assumptions
with actual parity corruption, compression and received-header gating.

The independent-generator RS selftest passes 1,220 assertions: thirty generator
versus syndrome-equation parity checks, 575 within-distance corruption cases and
300 beyond-distance cases with outer CRC handling. Envelope checks pass 1,242
assertions, including metadata/data/parity damage, length limits and version
rejection. Conditional-analysis checks pass seven assertions including changed-
dump rejection; 288 sampled old-error reference decoding replays also complete.

## Evidence

Root: `F:/Unity/TSMP/Validation-Results/fec-feasibility-20261031`.

- `fresh-seed2026103102`: independent bins, actual normal codec rasters, exact
  encoding commands, immutable source audit and sparse validation input plan.
- `residual-screen`: actual old GPU dump hashes, spatial diagnostics and explicitly
  conditional perfect-parity results.
- `fec-codec-fixtures`, `fec-matrix`, `encoded-gpu`, `encoded-comparison-retry`:
  initial v1 rasters, videos, raw GPU exports and original-byte verification.
- `fec-v2-codec-fixtures`, `fec-v2-matrix`, `encoded-v2-gpu`,
  `encoded-v2-comparison`: focused v2 equivalents. Its combined manifest reuses
  unchanged original baseline exports with explicit derivation hashes.
- `encoded-v2-current-comparison-final`: complete reference FEC replay with the
  actual current SparseRecovery production CSV joined; its SHA and paired
  gains/losses are recorded separately from the frozen three-mode comparison.
- `legacy-reject-gpu`, `legacy-v2-reject-gpu`: actual previous-decoder rejection.
- `v1-sources`, `v2-source-snapshot`: retained source versions bound to their
  respective source-audit hashes. Initial incomplete analysis folders remain.
- `independent-fec-audit.json`: an independent audit of raw dumps, source and
  packet provenance, exact encoding arguments and accepted original/packet bytes.
- `independent-fec-current-join-audit.json`: all 1,620 current-comparison rows
  matched by source SHA, length and frame ID, including all eighteen summary
  rows' complete bytes and paired gains/losses.

All finite GPU probes have zero input errors and zero CRC-accepted wrong outputs.
The first run has 1,620 observations/6,480 raw shader probes; v2 adds 1,080
observations/4,320 raw probes, reusing 540 unchanged baseline observations for its comparison. These counts
are observations, not applied network events or live update frequency. No owned
Unity measurement process remains running. See [README.md](README.md) to reproduce.
