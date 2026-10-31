# RGB16 preparation scope and timing

This Native Editor diagnostic distinguishes the actual default fixed 4/4/4
Refine material from the broad RefineCost warmup (four materials times three
requested modes). It changes no product algorithm or default. Use a dedicated
Native project with the matching Core/RGB16 package sources and no concurrent
owned GPU benchmark. Existing Unity and driver caches must remain intact.

```powershell
./Validation~/Rgb16Preparation/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<Native>' -InputImage '<saved clean RGB16 720p PNG>' -Payload '<independent original 5375B payload>' -Results '<fresh root>/fixed-first' -Order fixed-first
./Validation~/Rgb16Preparation/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<Native>' -InputImage '<same PNG>' -Payload '<same original>' -Results '<fresh root>/broad-first' -Order broad-first
python ./Validation~/Rgb16Preparation/Analyze-Results.py --results '<fresh root>'
```

Each fresh editor process runs the active fixed material twice at modes zero,
one and two; two broad twelve-draw groups with only the final draw flushed; and
two twelve-draw groups deliberately flushed after each draw. The second process
starts with broad draws. The active ordinary path must match every saved payload
byte. The analyzer independently recounts recorded flushed verdicts and timings;
it does not re-read GPU pixels or claim an independent byte-level GPU oracle.

`prepareMs` is actual `PrepareDecode` host time (including a possible calibration
Blit). `blitSubmitMs` is the following decode Blit host time.
`flushAndCompareMs` includes queued work, synchronous 8KB ReadPixels and the full
5,375-byte comparison. These are host wall times, not GPU execution time. In a
broad final-flush group the last flush includes outstanding work from all twelve
draws, so it cannot be attributed to the final material. Per-draw flushing changes
scheduling intentionally to aid diagnosis. Configuration and row bookkeeping are
outside these operation timings; broad host-wall notes include them.

Source image upload is flushed before measurement. No explicit shader warmup
occurs. Material/shader, requested/actual mode, calibration LUT keyword, input
hashes, immutable harness copy, package source hashes and configuration receipts
are retained. A fresh editor process with existing caches is not a first-install
or empty-cache test, and Native shader setup is not actual VRChat startup.
