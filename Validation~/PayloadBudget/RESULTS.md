# Payload transmission budget validation, 2026-10-31

The Encoder now enforces an independent `targetPayloadBytes` limit. Growing the backing buffer no longer raises this transmission target. Zero remains the default and preserves existing output. This is a size-control feature; no video-transport loss reduction percentage is claimed by these tests.

Core implementation commit: `ff24a36` on `codex/tsmp-reliability`. No main merge, push, package release or wire-format change was performed.

## Final automated checks

Unity 2022.3.22f1 / Windows / D3D11. Dedicated Native and Worlds SDK projects use the prior final packages plus the four changed Encoder/helper/Editor sources. Decoder and codec sources were not changed by this work unit.

| Check | Result |
| --- | --- |
| Native suite | PASS, 336 assertions |
| Native zero-target output vs frozen prior frame builder | 64 inputs byte-identical, including Unicode RPC arguments and changing arrays |
| Fresh Udon client compilation and actual Encoder VM suite | PASS, 163 assertions; Encoder bytecode 139,052 B |
| Native/Udon target and scheduling fixture | Target 29 -> exactly 29 payload B / one field; unlimited -> 147 B / three fields |
| Equal-priority limited outputs | Small fields alternate while the larger field remains deferred |
| Deferred latest value and sent callback | Enlarging target sends the latest byte value 73 and commits it only after output |
| Queued RPC after target reduction | Three limited state outputs preserve both repetitions; enlarging target sends the two repetitions |
| Capture callback changes target | Current frame retains its captured limit; new target applies on the following frame |
| Manual writes | Failed field/argument writes do not advance offsets/counts; later retry/cancel remains valid |
| VRC configuration preservation | Manifest, lock, ProjectSettings and Encoder program asset meta hashes unchanged |

The eight-byte minimum includes the network-frame header. Values above raster capacity or the 65,535-byte protocol maximum do not bypass those bounds. Small targets may fit no complete fields and then produce no output. Oversized fields have no fragmentation or delivery guarantee; a later RPC cannot bypass a deferred head RPC. These are explicit limits of the current policy.

The 29 B / 147 B values describe serialized fixture sizes, not delivered original-byte throughput, useful update rate, source age or video loss. A smaller budget can delay data even when each produced packet is easier to transmit. OBS/VRCDN/live VRChat behavior and transport-aware target selection require separate measurements.

Evidence root: `F:/Unity/TSMP/Validation-Results/payload-budget-20261031`.

- `native-initial/result.txt`: initial 327-assertion PASS.
- `vrc-initial/result.txt`: initial 152-assertion PASS with 139,032 B compiled Encoder.
- `native-final/result.txt`: final 336-assertion PASS after adding captured-target controls.
- `vrc-final/result.txt`: final 163-assertion PASS after adding captured-target controls.
- `native-final/runtime-source-hashes.json`, `vrc-final/runtime-source-hashes.json`: installed four-source receipts.
- `vrc-initial/preserved-before.json`: configuration/GUID preservation baseline.
- `independent-audit.json`: complete result/source/configuration audit from `Audit-Evidence.py`.

No input/reference correction, corrupt-data acceptance, compiler-error suppression or timing substitution was used. Both initial and final evidence directories are retained. No live client or service was started.

See [README.md](README.md) for behavior and reproduction.
