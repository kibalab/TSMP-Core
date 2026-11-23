# Adaptive Calibration LUT Validation

Use Unity 2022.3.22f1 and a dedicated project. Reference the working Core, Luma4,
RGB16, RGB20 and Color256 packages through local UPM file dependencies. Keep the
codec source checkouts: GPU comparison uses their frozen pre-G01 shader sources
in `Validation~/Gpu/LutBaselines`, outside the distributed packages.

Do not run this in a working scene. The harness creates an empty validation
scene and exits Unity. No VRChat SDK is needed for Gpu, Play or Player.
Use a separate SDK/UdonSharp project for Udon.

```powershell
./Run-Validation.ps1 -UnityPath '<Unity.exe>' -ProjectPath '<validation-project>' -ResultsDirectory '<results>' -Mode Gpu
./Run-Validation.ps1 -UnityPath '<Unity.exe>' -ProjectPath '<validation-project>' -ResultsDirectory '<results>' -Mode Udon
./Run-Validation.ps1 -UnityPath '<Unity.exe>' -ProjectPath '<validation-project>' -ResultsDirectory '<results>' -Mode Play
./Run-Validation.ps1 -UnityPath '<Unity.exe>' -ProjectPath '<validation-project>' -ResultsDirectory '<results>' -Mode Player
```

- Gpu compares original, fallback and LUT shader output using actual GPU
  readback. It covers exact/noisy/flat inputs, both orientations, automatic
  and explicit sample sizes, partial output, allocation reuse, resizing,
  missing preparation materials, shared materials and lifecycle cleanup.
- Gpu times 32 repeated passes per marker, including the LUT preparation
  pass. The A/B submissions are separated by readback completion, alternate
  order, warm up for ten frames and report fifteen-frame medians. GPU samples
  require a positive timestamp and exactly one recorded sample block. Wall
  time includes submission and synchronization but not per-pass C#/Udon
  material setup. Add `-SkipTiming` to run correctness checks only.
- Udon compiles all client programs and executes the real codec bytecode
  with the editor Udon VM. It exercises RenderTexture allocation, preparation
  blits, branch selection and cleanup, then compares GPU-decoded bytes.
  This is not a test inside a running VRChat client.
- Play and Player send 27 frames through the actual decoder header/payload
  readback sequence, switch sampling modes and codec options, check header
  CRC/frame index and compare raw payload bytes. They also assert whether
  the selected shader uses its LUT. The decoder uses payload-readback-only
  mode; these tests do not claim to validate variable/RPC dispatch.
- Player builds Windows x64 with Mono and managed stripping disabled, then
  launches the executable with D3D11. The build report and Player log remain
  in the result directory. Local shader multi-compile variants must survive
  the build; no temporary test shader replaces a package shader.

Never use `-nographics` for these checks. GPU measurements are device-specific
and must not be presented as VRChat client or Android timings.

The frozen RGB16 Refine shaders remain comparison controls. Their LUT paths
were slower in the expanded G01 measurements and are not enabled or shipped.

For the color codec receive regression, import the production-feedback Core,
RGB16, RGB20 and Color256 packages in a plain Unity project, add
`Editor/ColorCodecReceiveValidation.cs` to its `Assets/Editor`, and run
`-batchmode -quit -force-d3d11 -executeMethod ColorCodecReceiveValidation.Run`.
It compares GPU readback with seeded payloads and zero padding on an odd-width
block grid, including RGB16 three-bit symbols and Color256 interleaved rows.
