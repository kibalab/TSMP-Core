# Packed Transform and Humanoid workload

This SDK Editor fixture runs the actual client Encoder, Decoder, Luma4 codec,
TransformSync and HumanoidPoseSync programs. Source preparation changes only
Transform/bone values; the Encoder's ordinary BeforeEncode dispatch performs
capture once inside the Encode timer. Decoder time includes the shipped receive
component applying bytes to real Transforms and Animator bones.

Freeze an explicit plan before timing, then use a dedicated SDK project:

```powershell
./Validation~/PackedPose/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated SDK project>' -Plan '<frozen plan.json>' -Results '<fresh results>'
```

The plan is `{"cases":[...]}`. Each case declares `name`, `transforms`,
`humanoids`, `unchanged`, `warm` and `samples`. The bounded follow-up uses
Transform 1/8 changing, Transform 8 unchanged, Humanoid 1/4 changing and mixed
one Humanoid plus four Transforms. Sixteen warm calls precede 512 measured calls
per case in each of two fresh processes. A separate small pilot checks the
fixture before formal source/plan/harness hashes are frozen.

Default RotOnly Transform packing is 31 bytes without a Rigidbody. Each test
Humanoid uses a real generated 17-bone Avatar and Animator: its ordinary packet
is 123 bytes, including root motion. Finger inclusion keeps its shipped default,
but the test Avatar contains no fingers. Actual VRChat player API capture,
production avatars, skinning, fingers and populated-world work are outside this
fixture. One packed component is one TransSync field; 128 independent scalar
bindings are a different workload.

Each changed packet is published through the actual GPU Luma4 Encoder. Original
protected bytes then enter the real compiled Decoder as one completed slot;
receiver capture/GPU/transport waiting is excluded. Every body/CRC/header is
checked. Receive checks compare exact source/receiver bytes, fixed-size storage
reuse, Transform position/scale, and every test bone's world rotation plus root
position. Position tolerance is 0.002 units and quaternion tolerance 0.6 degrees.
A separate untimed same-count public binding-target replacement checks that the
replacement is captured and published.

Refresh is zero only to isolate unchanged capture/comparison cost. Receive mode
is Discrete. Normal production refresh, interpolation, source cadence and
binding-change frequency are not measured. First/warm calls remain in the
evidence, with no cold-path speedup claim.

`samples.csv` retains frame and raw Stopwatch boundaries for source preparation,
Encode and Decode, publication/application counts, array identity observations,
and incomplete rows on failure. `bindings.csv` records field identities, lengths
and the test bone hierarchy. Every emitted packet/header is buffered in bounded
storage and written after its case, alongside executed fixture, program,
configuration and product-source receipts. Host checks and source setters are
outside VM timers but still consume process work; these timings do not establish
total frame time or zero allocation.

The fixed-header copy product experiment uses actual GPU `SustainedCost` runs.
This fixture's completed-slot injection leaves header-layout prediction disabled,
so its performance does not establish that candidate's effect.

See [results](RESULTS.md) for the retained measurements and decision.
