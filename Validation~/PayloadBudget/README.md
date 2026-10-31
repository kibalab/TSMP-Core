# Independent payload transmission budget

`TSMPEncoder.targetPayloadBytes` limits the network payload in each output frame independently of the growing backing buffer. Its default zero preserves the existing raster-capacity policy. Positive values below the eight-byte network header normalize to eight; the raster and 65,535-byte protocol limits still apply. `effectivePayloadBudgetBytes` reports the captured limit for the latest encode.

The existing priority/change/minimum-interval scheduler chooses complete fields that fit. Equal-priority fields rotate after successful output. An oversized field remains deferred and is not marked sent; raising the target or making the field smaller can send its latest value. There is no field fragmentation. A low budget can indefinitely defer a large field, and strict priority can still delay lower-priority data. Assess delivered original bytes, source age and event coverage rather than packet success alone.

New RPCs that exceed a positive target are rejected at enqueue. If the target is reduced after enqueue, the existing head RPC remains pending without consuming repetitions, while eligible small state fields can still be sent. Raising the target resumes it. Later queued RPCs remain behind that head. Zero-target legacy capacity-reduction handling is unchanged. Manual writes respect the limit without advancing offsets/counts on failure; an already assembled frame exceeding a subsequently reduced target is rejected before output/commit.

No sender palette, raster mapping, packet format, CRC, receiver or scheduling metadata changes are required. This feature establishes an actual size bound; it does not establish a measured improvement through OBS, VRCDN or a live VRChat client.

## Validation

Install the changed Core package in dedicated Unity 2022.3 projects before running. The runner copies only the test sources and records the installed runtime hashes. The SDK VM mode needs an isolated Worlds SDK project; it freshly compiles client bytecode and executes the real Encoder through the editor Udon VM. Neither mode logs into a VRChat client or publishes a stream.

```powershell
./Validation~/PayloadBudget/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<Native project>' -Results '<new empty Native results>'
./Validation~/PayloadBudget/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<VRC project>' -Results '<new empty Udon results>' -Vm
```

Native controls compare 64 inputs byte for byte against `EncoderNativeFrameBuilder.cs` frozen from Core `f85426f6416e62f2d011da18a89263d1eab87a31`, changing only its class name and Native-only guard. The frozen test source SHA256 is `7d604d1cc18580b1408152132200f5e66f4e1baf8a3d43136c08428d4ec9fa7c`. Other controls cover actual output size, backing-buffer growth, large-field deferral, latest values, equal-priority fairness, live target changes, RPC queue preservation, repetition counts, and manual offset/count rollback. Test output reports fixture bytes and assertions; those fixture sizes are not transport-goodput measurements.
