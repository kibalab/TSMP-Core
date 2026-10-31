# Color256 Encoder pixel parity

This correctness harness runs the actual Udon-branch codec method twice: managed
C# execution of the SDK proxy, and freshly compiled client bytecode after Play
entry. The dedicated Native project excludes that branch and cannot establish
this coverage. No performance, allocation, GPU-byte or live-client claim follows
from this harness.

Capture an immutable original reference before any codec edit:

```powershell
./Validation~/Color256Encoder/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated SDK project>' -Results '<fresh original reference>' -ExpectedSourceSha256 '<frozen original codec SHA256>'
```

After independent baseline audit and adopting the candidate, run:

```powershell
./Validation~/Color256Encoder/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated SDK project>' -Results '<fresh candidate results>' -ExpectedSourceSha256 '<candidate codec SHA256>' -Reference '<original reference>'
```

Twenty-five deterministic vectors check every managed Color32 array byte against
an independent fixed integer palette and coordinate oracle. Nineteen vectors
also compare actual client VM output. They include all256 symbols, empty and
1KB/5KB payloads, row boundaries, partial rows, capacity/over-capacity, short and
zero block arrays, dirty extra tails, narrow calibration geometry and a small
full-pixel fallback. Large full-pixel fallback and invalid-count/short-array
exception parity use managed execution to avoid irrelevant VM watchdog/error
cases. Partial mutations before managed exceptions are retained and compared.

Initial pixels have a deterministic sentinel, so complete output comparison also
checks untouched header/static/end/tail regions. Those are sentinel regions in
this isolated codec test, not a newly generated protected frame. The separate
SustainedCost Encoder->GPU->Decoder A/B provides complete packet/header/application
coverage for the actual product path.

The oracle uses explicit R/G levels24/54/83/113/143/173/202/232 and
B levels24/93/163/232. It never calls the codec's conversion or coordinate helper.
Every original managed/oracle/VM buffer, payload, result/exception, vector plan,
source/bytecode hash and configuration receipt is retained. Candidate runs must
match the entire immutable original buffer, including exception partial writes.
