# README showcase recording

The short recording shows two separate Native Unity Player processes. The
sender animates a real 17-bone Humanoid and a Transform, then the shipped TSMP
Encoder produces a Luma4 texture. A recording-only localhost TCP bridge carries
the complete uncompressed RGBA image to the receiver. The shipped Decoder reads
that image through actual GPU readbacks and applies the pose and values.

The three panels show the sender camera, the actual received encoded texture
and the receiver camera. The sender camera is a presentation sidecar; its pixels
are never used to update receiver objects. Pose, Transform, sequence, time,
message and seeded data bytes travel only inside TSMP frames. Receiver motion
has no local animation driver. Actual process IDs, encoded/applied counters and
CRC diagnostics supply the labels. The decoded time is used only for checking
expected root/cube positions and rotation, not for animating the receiver.

The sequence runs live, pauses the encoded-image link from seconds4 to6.5 while
the sender continues moving, then resumes. During the pause the receiver retains
its last valid pose. All six fields use the existing Always mode for coherent
whole-state demo frames and reliable late connection. No product default or
Runtime implementation is changed. Seeded data bytes are a real TransSync field
that makes the encoded texture's activity visible.

The recording is a local Native Unity example. It does not depict VRChat login,
Udon execution, OBS/Spout, a CDN, compressed-network transport or performance
measurements. The exported H.264 presentation video is not the TSMP transport;
it should not be treated as a pixel-exact decoder test source.

## Reproduce

Use an isolated Native Unity project as a template. The runner copies only its
Packages and ProjectSettings into a new work directory, imports this harness,
builds the standalone Player, starts its two roles and preserves the recording,
logs and trace. Existing validation projects and recordings are left in place.

```powershell
./Validation~/Showcase/Run-Recording.ps1 -TemplateProject '<Native validation project>' -WorkDirectory '<new empty destination>' -UnityEditor '<Unity.exe>' -Ffmpeg '<ffmpeg.exe>'
```

The renderer composes a dedicated 1600x900 UI texture so capture does not include
other desktop windows or depend on the host display size. FFmpeg encodes H.264
with wall-clock input timestamps and a constant30fps output; slow captures are
duplicated rather than sped up. Readback/capture overhead is part of recording
and is not a benchmark. The video is silent.

`recording-result.txt` requires actual CRC-verified state application, exact
seeded bytes and expected root/cube poses, plus a hold longer than one second
and subsequent resumption. `receiver-trace.csv` retains observed state/time/frame
and holding flags. Build logs and process identities remain with each take.
Review extracted frames for visibility, layout, real motion, pause and resume
before publishing. A successful data check alone cannot detect a black capture.

The README uses a compact animated GIF linked to the MP4. This keeps the preview
usable without a hosted attachment. Publishing or merging is separate from
preparing the media on the working branch.

## Recorded media, 2026-11-04

The retained take is 16.066667 seconds, 1600x900 H.264/yuv420p at30fps, with
482 captured frames and a 4,236,485-byte MP4. The 960x540 GIF preview runs for
8.5 seconds at12fps and is 2,961,453 bytes. A static poster is also included in
`.github/assets`. English, Korean and Japanese README headers link the preview
to the MP4.

The independent recording audit confirms distinct sender/receiver processes,
source copies matching the built project, all recorded CRC flags true, ordered
state/header identities, an independently recomputed cube trajectory and a
2.4668-second hold followed by resumed application. During that hold the sender
image changes while receiver image/state remain fixed. It does not independently
recompute per-packet CRC from archived raw payloads; full byte/pose checks run in
the Native fixture. The recording makes no throughput or delivery-rate claim.

Raw takes, source/transport controls, logs, trace, keyframes and audit are kept
under `F:/Unity/TSMP/Validation-Results/readme-showcase-20261104`. Initial capture
failures remain separate: a metadata member typo, a late-connected receiver
missing an initial unchanged pose, a display-size mismatch, and a black hidden
window capture. Existing Always mode and a dedicated render target fix the
recording setup; product code and defaults are unchanged.
