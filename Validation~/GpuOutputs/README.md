# GPU receive outputs

Use an isolated Unity 2022.3 project with the working Core and Luma4 packages. Use a graphics device, not `-nographics`.

- Plain Unity: put `GpuOutputValidation.cs` and `InspectView.shader` in `Assets` (not `Editor`). Run `-batchmode -force-d3d11 -executeMethod GpuOutputValidation.Run`.
- VRChat Worlds: compile UdonSharp for the client, put `Editor/GpuOutputUdonValidation.cs` in `Assets/Editor`, and run `-batchmode -force-d3d11 -executeMethod GpuOutputUdonValidation.Run` with ClientSim enabled.

Both methods enter Play Mode and exit the editor themselves. Omit `-quit`. The fixtures create scenes and assets in the validation project.

## Coverage

The native check encodes valid NetworkFrame messages into Luma4 and verifies three GPU destinations: the entire frame, an unaligned RawBytes field, and an independent shader reading little-endian numbers, length, type and full-width stream/frame IDs. It covers ordinary readback, both predicted layouts, prediction fallback, two in-flight slots, duplicate filtering, invalid CRC, malformed trailing bytes, parse-only mode, a missing field, a different network ID, repeated and empty fields, disabled passes and a decoder without outputs. Texture widths deliberately differ and are not powers of two.

The SDK check serializes the decoder-to-output reference in a scene, starts the backing UdonBehaviour in ClientSim and checks the four bytes of a transmitted Int32 in the destination texture. This is not an uploaded-world test.

Verified with Unity 2022.3.22f1, Direct3D 11, RTX 4090, and Worlds SDK 3.10.4-beta.2. The batch ClientSim player input controller also logged an unrelated GetMovementInput exception; the serialized Udon GPU output check completed successfully.
