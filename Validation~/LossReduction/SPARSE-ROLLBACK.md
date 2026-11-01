# Sparse-only rollback, 2026-10-31

The observed-byte Sparse recovery experiment has been removed at the user's
request. It was introduced by `eea6a84` and measured at Core `9069850`. This is a
new commit on `codex/tsmp-reliability`; existing history is retained and main is
not merged or reset.

## Scope

- Restore `TSMPDecoder.cs`, its Inspector and the Core README exactly to
  `f85426f6416e62f2d011da18a89263d1eab87a31`, before Sparse was introduced.
- Remove the Sparse helper, metadata, public option/counters, per-slot baseline
  copies, difference scans, candidate search and Sparse-only C# validation probes.
- Remove Sparse integration cases, runner options and production CSV counters.
  Retain the original recovery safety tests, explicit Gamma/Linear selection,
  optional raw GPU dumps, package source hashing and RenderTexture cleanup.
- Preserve whole-payload Refine recovery, default two payload retries, default-off
  header retries, CRC optimizations and the earlier codec/low-bit fixes.
- Preserve the independent Encoder `targetPayloadBytes` feature unchanged.
  Exactly its four implementation files differ from the pre-Sparse Core package.
- Retain Python analyzers, FEC fixtures and original measurements as historical
  evidence. FEC is validation only and adds no production execution cost.

## Reason and tradeoff

The archived paired run recovered 23 extra payloads out of 20,439 observations
(about 0.11 percentage points), including 12 out of 7,200 CBR observations
(about 0.17 points) and no extra OBS successes. Successful 5,375-byte fixtures
cost 4.318 ms for two differing positions, or 6.742 ms for a saturated successful
search in the editor Udon VM, including copy/scans/search/full CRC. The observed
8,192-byte saturated successful-search maximum was 10.362 ms. These finite VM
fixtures are not actual VRChat frame-time measurements or absolute time bounds.

Rollback removes that extra CPU path and its buffers. It also gives up those
Sparse-only successes; the archived comparison's previous decoder accepted
17,790 observations versus Sparse's 17,813. The full transport corpus was not
replayed again for this rollback. Exact source restoration and focused integration
checks establish scope; no new whole-decoder speedup or FPS percentage is claimed.

## Post-rollback automatic verification

Evidence root: `F:/Unity/TSMP/Validation-Results/sparse-rollback-20261031`.

- `source-scope.json`: PASS. Decoder/Inspector/README match the pre-Sparse
  revision; all four Encoder budget files match `9069850`; no remaining Sparse
  C# references in the current runtime or Refine harness.
- `core-safety-gamma-final/result.txt`: PASS, **910 assertions**, including all
  four codecs, protected header/payload rejection, budgets zero/one/two,
  prediction/combined paths, independent slots, state/RPC atomicity, cancellation
  and nine actual compressed OBS cases still recovered by existing Refine.
- `udon-compile-final/*-Udon-result.txt`: PASS, sixteen TSMP client programs,
  bindings enabled.
- `decoder-heap/result.json`: PASS. Sparse compiled symbols, managed fields and
  helper type are absent. Two payload retries, header recovery off, existing
  counters and per-slot request options remain. Decoder bytecode is **135,744 B**,
  versus 152,116 B before rollback (16,372 B / 10.76% smaller). Bytecode size is
  not a runtime speed measurement.
- `decoder-heap/independent-vrc-audit.json`: PASS, seven checks; 252 selected Core
  sources match the repository, and all 24 captured project configuration files
  are unchanged. Native/VRC manifest, package lock and player settings also match
  `project-settings-before.json` in `project-settings-after.json`.
- Python/PowerShell syntax and Git whitespace checks pass. Independent diff
  review confirms the removal is limited to Sparse and its supporting validation.

Initial launches encountered Unity's stale compiler source list referencing the
deleted helper. The first VRC inspector also contained a validation-only namespace
mistake, corrected before the final run. Failed results remain separate; fresh
strict runs pass without suppressing compiler diagnostics or deleting caches.
Obsolete dedicated-project probes were archived outside Assets.

No live VRChat/VRCDN run, main merge, push, release or stream publication was
performed. Original pre-rollback evidence directories were not overwritten.
