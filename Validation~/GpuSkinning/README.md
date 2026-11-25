# Humanoid GPU skinning checks

For measured receiver CPU time, GPU pass timings, memory and reproduction instructions, see the [receiver profile](PROFILE.md).
For a single stream carrying many independently moving performers, see the [crowd receiver comparison](CROWD.md).

Use disposable Unity 2022.3 projects with the working Core and Luma4 packages and the Shapell sample model included in Core. Use a graphics device; do not use `-nographics`.

- **Plain Unity:** copy `SkinValidation.cs` into `Assets/Editor` and `SkinProbe.shader` into `Assets`. Run `-batchmode -force-d3d11 -executeMethod SkinValidation.Run`. The test bakes two proxies, saves the scene, enters Play Mode, and exits with a nonzero code on failure. Do not pass `-quit`.
- **VRChat Worlds:** install the SDK, compile UdonSharp for the client, and copy `SkinWorldValidation.cs` into `Assets/Editor`. Run `-batchmode -force-d3d11 -executeMethod SkinWorldValidation.Build` without `-quit`. This creates a test scene and invokes the SDK's local world builder, **not** the uploader. The log records the bundle path.

The renderer test captures the existing sender's v4 pose, encodes a real Luma4 frame and decodes it into channel 2, with no CPU binding for the GPU receiver. A separate CPU receiver applies the same pose to the reference rig. The probe draws points with the shipped vertex include into an ARGBFloat target and reads world positions back for comparison with `SkinnedMeshRenderer.BakeMesh`. It checks local/world Hips motion, a rotated and scaled receiver, missing human joints in the packet, an auxiliary bone, four blended weights, and rest geometry before input and after an unsupported pose version. It also checks normals and tangents at the single-influence vertices. The model check samples 256 vertices across the packaged Shapell Body mesh in both root modes. These are correctness checks, not timing benchmarks or tests in an uploaded world.
