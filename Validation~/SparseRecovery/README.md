# Bounded sparse payload recovery

This receiver-only experiment combines byte values already observed in normal
decoding and the two existing recovery modes. It never invents arbitrary byte
values or reads a newer video frame. Sender pixels, palettes, protected packet
layout and the checksum remain unchanged.

After all three whole-payload candidates fail, `recoverSparsePayload` permits a
search only when the protected payload is at most 8,192 bytes and the candidates
differ at no more than eight byte positions. Each hypothesis replaces at most
two bytes of one of the three observed candidates. There are at most 384
hypotheses, including duplicate hypotheses. A single distinct CRC-matching
candidate is required, followed by the ordinary complete protected-payload CRC
validation before acceptance or state/RPC application. A CRC collision remains
possible; these finite tests cannot certify its absence for arbitrary inputs.

The feature requires `maxCrcRecoveryAttempts=2`. Budgets zero/one, unsupported
materials, unprotected packets and larger payloads retain their previous policy.
The option and budget are captured with each request. Baseline payload buffers
belong to the two decoder slots and are allocated/copied only after a supported
protected-payload failure. No new GPU pass or readback is added. Scratch arrays
are reused by synchronous CPU completion, while disagreement metadata has
separate flat slices for each slot. The retained baseline buffers are at most
8,192 bytes each.

CRC linearity computes single-byte replacement effects, and hypotheses combine
these effects with XOR. CRC-32 polynomial powers advance an effect over its
remaining suffix in logarithmic work; candidate enumeration does not recompute
the complete payload hundreds of times. Equal eight-byte runs are compared with
`BitConverter.ToUInt64` before inspecting individual differing bytes. Both input
words use the same byte order, so this equality does not depend on endianness.
Polynomial arithmetic was checked against full-payload CRCs and the principles
used by [zlib CRC combination](https://github.com/madler/zlib/blob/v1.3.1/crc32.c).

## Pure arithmetic checks

Run from the repository in PowerShell:

```powershell
Add-Type -Path @(
  (Resolve-Path 'Packages/com.kibalab.tsmp.core/Runtime/Core/Crc/Crc32Runtime.cs').Path,
  (Resolve-Path 'Packages/com.kibalab.tsmp.core/Runtime/Decoder/Helpers/SparsePayloadRecovery.cs').Path,
  (Resolve-Path 'Validation~/SparseRecovery/SparseRecoveryCases.cs').Path
)
[SparseRecoveryCases]::Run()
```

The independent reference enumerates actual complete candidate byte arrays and
computes full CRCs. Seeded tests compare decisions, exact bytes, finite budgets,
first/last positions, slot offsets, input limits and rejection without mutation.
A deliberately constructed CRC-null difference provides two distinct matching
candidates, which must be rejected as ambiguous.

## GPU outputs and production replay

The existing [Refine harness](../RefineRobustness/README.md) accepts
`dumpRawBytes:true`. It writes original GPU `.bin` outputs and `raw-dumps.csv`
with independent header/payload/input paths. `Analyze-ModeMix.py` screens the
finite candidate policy offline. Its raw-payload successes are diagnostic;
received-header failure still prevents production acceptance.

Production replay must compare actual TSMPDecoder outputs against independent
pre-raster bins, including previously correct observations and an untouched new
seed generated after candidate selection. The production CSV exposes per-input
sparse search, hypothesis and recovery counters. GPU readback wall times are not
GPU pass timings or live VRChat FPS.

## Udon validation

Copy `Udon/SparseRecoveryVmProbe.cs` and
`VRCEditor/SparseRecoveryVmValidation.cs` into a dedicated VRC project under
`Assets/Validation/SparseRecovery/Udon` and `Assets/Validation/SparseRecovery/Editor`.
Set `TSMP_SPARSE_VM_RESULTS` to a fresh directory, then launch Unity with
`-batchmode -nographics -quit -executeMethod SparseRecoveryVmValidation.Run`.

The runner compiles client bytecode and executes it with the SDK editor Udon VM.
Its three measured entry points isolate the search, add the baseline copy and
two disagreement scans, and finally include a full checksum on recovered bytes.
Each case/path records five warm and thirty measured samples. Timings exclude
buffer allocation, GPU work/readback and application logic, and do not represent
actual VRChat frame time. Stop other owned benchmark loads before timing.
