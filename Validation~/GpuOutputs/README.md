# Shader receive channels

Use an isolated Unity 2022.3 project with the working Core and Luma4 packages. Copy `InspectView.shader` into `Assets` for both checks. Use a graphics device, not `-nographics`.

- Plain Unity: put `GpuOutputValidation.cs` in `Assets` (not `Editor`). Run `-batchmode -force-d3d11 -executeMethod GpuOutputValidation.Run`.
- VRChat Worlds: compile UdonSharp for the client, put `Editor/GpuOutputUdonValidation.cs` in `Assets/Editor`, and run `-batchmode -force-d3d11 -executeMethod GpuOutputUdonValidation.Run` with ClientSim enabled.

For the plain Unity check, also copy `TypedFields.shader` into `Assets`. A production writer/decoder round trip exercises all 15 wire types plus RGBA, float4 and row-major matrix conventions. The 32 output pixels each represent one assertion group (including empty values, missing keys, wrong types, invalid indices and Unicode); every channel must read back as 255. The ClientSim check uses the typed Int32 getter on an explicit non-default channel.

Both methods enter Play Mode and exit the editor themselves. Omit `-quit`. The fixtures create scenes and assets in the validation project.

The native check encodes Luma4 frames, reads them through the public include in an independent material and compares the GPU bytes to the original NetworkFrame and field. It checks unaligned little-endian values, full-width IDs, ordinary and predicted layouts, prediction fallback, overlap, duplicate filtering, CRC rejection, malformed trailing bytes, parse-only mode, retained and empty fields, repeated keys, stream changes, channel changes and opt-out. A 66-field frame spans directory rows. Two live decoders publish distinct values for the same key; disabling one must leave the other readable.

The SDK check serializes a decoder and codec in a scene and invokes the backing UdonBehaviour in ClientSim. Its consumer material reads an Int32 from channel 1, then checks it again after the next frame contains only a different key. No field binding or output component is present. This is not an uploaded-world test.
