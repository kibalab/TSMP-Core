# Offline video roundtrip

Run in a dedicated SDK-free Unity 2022.3 project with Core and Luma4 installed:

```powershell
& './Validation~/VideoTransport/Run-Validation.ps1' -UnityEditor $unity -Project $project -Results $newResults -Ffmpeg $ffmpeg
```

The harness writes 12 changing Luma4 frames at 1280x720, block8, sample4. Synthetic payload lengths cycle through 32, 2495 and 5375 bytes. Each is a structurally valid NetworkFrame containing one raw-byte field, decoded in parse-only mode without applying receiver state or RPC. The larger lengths match the byte loads of the candidate Full16/Fingers16 profiles; they do not simulate avatar capture or multi-user CPU costs.

Each frame passes through PNG control, RGB FFV1 control, H.264 CRF18 and H.264 CRF28 (`libx264`, `veryfast`, `yuv420p`, nominal file rate 30). FFmpeg restores the files to PNG, and the actual TSMP GPU decoder reads them back. `frames.csv` compares the entire received payload with the known sender bytes and records `EXACT`, `REJECTED` or `CORRUPT`. Header validity alone does not count as success. Any rejected or changed frame makes that profile and the command fail; failed profiles remain useful measurements and must not be omitted.

The result directory contains source images, encoded files, restored images, exact FFmpeg arguments/version and Unity logs. Use a new directory on every run. This is a finite, offline byte-accuracy experiment. It does not measure live frame rate, source age, network loss, OBS/Spout, VRChat client/Udon, state/RPC application or real avatars. Passing these settings does not certify arbitrary encoder settings or transports. No protocol bytes or runtime integrity behavior are changed.

## Payload mutation characterization

Run `Validation~/Run-Validation.ps1 -Step PayloadIntegrity` in a dedicated Native project (with the usual UnityEditor, Project and Results arguments). This directly exercises the decoder's parser and field application: a valid Int32 value is changed from 42 to 43 while routing, type, length and header CRC stay intact. The current legacy contract accepts and applies 43. `PASS` means this integrity limitation was reproduced; it does not mean the mutation was safely rejected. The suite labels this step `Characterization`. No GPU, video, Udon or RPC is involved in this separate test.
