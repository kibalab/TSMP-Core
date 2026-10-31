# Structural preflight validation, 2026-10-31

The earlier Native reproduction accepted the payload CRC but applied a normal
state/RPC prefix before rejecting malformed remaining structure. The fix moves
complete structure validation before application, without payload copies, a new
CRC calculation, staging buffers or a protocol change.

## Automatic checks

Evidence: `F:/Unity/TSMP/Validation-Results/followup-performance-20261031/frame-structure`.

- `native-final/result.txt`: PASS, **1,257 assertions**. Actual protected Native
  completion rejects malformed outer tails, later messages, variable entries and
  RPC arguments/tails before state/RPC or binding/dedup cache effects. The same
  event ID in an intact retransmission executes once, then deduplicates normally.
- `core-safety-gamma/result.txt`: PASS, **910 assertions**, including four codecs,
  budgets zero/one/two, predicted/combined paths, separate slots, lifecycle and nine
  saved compressed OBS inputs still recovered by existing Refine.
- `vrc-final/result.txt`: PASS, **3,858 assertions** in actual compiled client
  bytecode after Play Mode entry. Ready-slot byte injection invokes Decoder Update,
  protected payload CRC, structural validation and actual bound field/RPC dispatch.
  Native and Udon include parse-only malformed-RPC rejection and CRC-negative
  controls. This is not a GPU/readback-driver, transport or actual VRChat run.
- `udon-compile/*-Udon-result.txt`: PASS, sixteen TSMP client programs and Setup
  bindings, using the existing full-client compilation validation.
- The 1,012-input reference corpus includes seeded framing mutations, logical
  length/dirty capacity tails, zero messages, maximum declared count and zero RPC
  arguments. Warmed Native preflight allocation is **0 B in 1,000 calls**; this
  does not establish Udon allocation behavior.

Decoder client bytecode is **138,380 B**, versus 135,744 B at the Sparse-only
rollback, an increase of 2,636 B. The helper traverses existing message/entry
headers twice for application mode; bulk value bytes are skipped by their length.
Readback-only behavior and the earlier trusted-header prediction cache are retained.
There is no claim of arbitrary callback rollback or additional semantic validation
inside UTF8/string-array values.

## Cost of the added structural pass

SDK editor Udon VM, five warm and thirty measured calls per method/fixture.
Event dispatch is included; setup, CRC, copy, application and graphics are excluded.
All timed inputs have one message. The existing CSV column `messages` denotes
variable entries, including the RawBytes field used to pad the same total length.

| Logical payload | Entries | Mean | p95 |
| --- | ---: | ---: | ---: |
| 32 B | 1 RawBytes | 0.020723 ms | 0.026400 ms |
| 2,495 B | 1 RawBytes | 0.020467 ms | 0.020400 ms |
| 5,375 B | 1 RawBytes | 0.020070 ms | 0.020200 ms |
| 5,375 B | 64 Int32 + 1 RawBytes | 0.360053 ms | 0.370400 ms |
| 5,375 B | 256 Int32 + 1 RawBytes | 1.422570 ms | 1.563900 ms |
| 8,192 B | 1 RawBytes | 0.020310 ms | 0.021500 ms |

Noop means are 0.001093–0.001173 ms and are not subtracted. This is a real added
cost, proportional to entry count; the one-field result must not be extrapolated
to hundreds of scalar fields or presented as whole-decoder/VRChat frame time.

The first VRC attempt compiled but constructed a ready Udon manager in Edit Mode,
which is unsupported. It remains under `vrc/`. The harness was corrected to enter
Play Mode and recompile for the client before VM construction; production sources
were unchanged between attempts. Final harness sources and hashes are preserved
with the final runs. No compiler errors were suppressed.
