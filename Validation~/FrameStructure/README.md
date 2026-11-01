# NetworkFrame structure before application

The decoder validates the complete declared message structure before applying any
field or dispatching any RPC. The preflight uses the existing entry/type/length
contracts, rejects malformed later messages/entries/arguments and trailing bytes,
and respects the logical payload length rather than backing-buffer capacity.
Parse-only mode now validates RPC structure too.

The additional pass walks headers and lengths; it does not scan or copy complete
values, allocate staging arrays, decode strings or compute another CRC. The
existing payload CRC check still precedes it. Binding caches and RPC deduplication
are reached only after successful structure validation. The existing trusted-header
prediction-layout cache behavior is retained. Readback-only mode still does not
parse messages.

This is a framing boundary, not a generic transaction. It does not roll back an
arbitrary user callback's side effects or exceptions, and does not introduce a new
UTF8/string-array semantic rejection policy.

Install the changed Core sources in dedicated Unity 2022.3 Native/Worlds SDK
projects, then run:

```powershell
./Validation~/FrameStructure/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<Native>' -Results '<fresh Native results>'
./Validation~/FrameStructure/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<VRC>' -Results '<fresh VM results>' -Vm
./Validation~/FrameStructure/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<Native>' -Results '<fresh allocation-only results>' -AllocationOnly
```

Native invokes the actual private protected-payload completion path. Udon freshly
compiles client bytecode after Play Mode entry, injects ready payload readback
bytes into an actual decoder slot and calls its Update event. Both exercise real
CRC and state/RPC application, including a corrected retransmission after a
rejected frame and subsequent duplicate suppression. These are targeted completion
tests, not GPU/raster/video/VRChat-client measurements.

The helper is also compared with an independently framed parser on 1,012 inputs,
including seeded mutations and zero-message/max-count/zero-argument controls.
Warmed Native allocation checks cover 1,000 helper calls after an 8,192-byte
positive allocation control kept alive across the counter read. A failed control
reports `VerifiedAllocatedBytes=-1` (unavailable), never a verified zero.
`-AllocationOnly` repeats this probe without rerunning the structural corpus or
completion regressions. VM timings include event
dispatch and isolate the new structural pass; they exclude input setup, CRC,
payload copying and application. The raw CSV column named `messages` records
**variable-entry count**, including the final RawBytes field; every timed fixture
contains one NetworkFrame message. Sizes remain constant when comparing 1/65/257
entries. Noop is recorded separately and is not subtracted.

See [RESULTS.md](RESULTS.md) for the measured costs and retained initial failure.
