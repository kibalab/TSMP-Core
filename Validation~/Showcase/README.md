# README showcase recording

The short recording shows two separate Native Unity Player processes. The
sender animates a Humanoid and simulates 12 Rigidbody cubes under gravity, with
real floor and cube-to-cube contacts. The shipped TSMP
Encoder produces a Luma4 texture. A recording-only localhost TCP bridge carries
the complete uncompressed RGBA image to the receiver. The shipped Decoder reads
that image through actual GPU readbacks and applies the pose and values.

The three panels show the sender camera, the actual received encoded texture
and the receiver camera. The sender camera is a presentation sidecar; its pixels
are never used to update receiver objects. Pose, 12 cube Transforms, sequence, time,
message and seeded data bytes travel only inside TSMP frames. Receiver motion
has no local dance animation driver. Clothing and hair use the installed official
VRChat SDK PhysBone solver independently in each Player. A plain monochrome label row reads
`Sender --> Frame stream --> Receiver` above the three live views. Process
identities, frame counters and CRC diagnostics stay in the recording logs.
Receiver cubes are kinematic with gravity and collision detection disabled;
only decoded TSMP poses move them. Decoded time never drives receiver motion.
Both roles journal actual cube positions, rotations and scales for independent
comparison by sequence and object ID; neither role reads these journals.
The imported avatar's applied bone rotations and hips position are checked
against the actual decoded packed pose. The default generated 17-bone actor is
still available when no private avatar is supplied.

The sequence runs live, deliberately pauses the encoded-image link from seconds 4 to 6.5 while
the sender continues moving, then resumes. During the pause the receiver retains
its last valid pose. All 17 fields use the existing Always mode for coherent
whole-state demo frames and reliable late connection. The Humanoid pose holds
during the pause; local clothing and hair can continue settling. No product default or
Runtime implementation is changed. Seeded data bytes are a real TransSync field
that makes the encoded texture's activity visible. The proof byte array is 256 B.
Twelve cubes are released at 0.23-second intervals and recycled through the same
pool. Between releases, Unity Rigidbody gravity and contacts determine their
motion; there is no scripted falling trajectory or independent receiver solver.

During the interruption, the affected headers show `[PAUSED]` and a plain
`PAUSED (recovery demo)` caption appears above the encoded frame. These are
recording-only status labels. The stream indicator follows actual preview-only
packets and disappears when fresh pixels arrive. The receiver indicator remains
until a CRC-verified frame from after resumption has actually applied; delayed
pre-pause GPU results cannot clear it. The sender continues throughout.
`pause-visibility.csv` records the exact displayed flags at each capture call.

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

The renderer places the three views side by side in a dedicated 1440x528 texture,
with a 48-pixel label row at the top.
The sender and receiver cameras each occupy 480x480; the complete 640x360 encoded
frame fits in the middle 480x270 area, preserving its aspect ratio with light gray
space above and below. The scene background and floor also use light colors.
Capture does not include other desktop windows or depend
on the host display size. FFmpeg encodes H.264
with wall-clock input timestamps and a constant30fps output; slow captures are
duplicated rather than sped up. Readback/capture overhead is part of recording
and is not a benchmark. The video is silent.

`recording-result.txt` requires actual CRC-verified state application, exact
seeded bytes and applied Humanoid/all12 cube poses, plus a hold longer than one second
and subsequent resumption. `receiver-trace.csv` retains observed state/time/frame
and holding flags. `sender-cube-poses.csv` and `receiver-cube-poses.csv` retain
every cube pose by applied sequence. `sender-physics.txt` requires12 dynamic
bodies, actual floor/cube contacts, repeated releases and downward speed above1m/s.
Build logs and process identities remain with each take.
Review extracted frames for visibility, layout, real motion, pause and resume
before publishing. A successful data check alone cannot detect a black capture.

The README uses a compact animated GIF linked to the MP4. This keeps the preview
usable without a hosted attachment. Publishing or merging is separate from
preparing the media on the working branch.

## Private Aoi assets and PhysBones

The optional Aoi recording uses the author's Hayakawa Aoi v1.02 prefab and
Kevin Iglesias's `HumanF@Dance06 - Loop` (Heel Toe / Happy Feet). Raw model,
motion, textures, shaders and SDK binaries stay outside this repository.
The author-configured prefab is required; the raw FBX has different wardrobe
visibility and material defaults. Original asset C# scripts were excluded from
the private Native import. Outline tangents are reconstructed from UV2/UV3 on
cloned meshes, preserving the supplied shader code.

Supply a reviewed private Assets directory and a Native template with an
embedded private `Packages/com.vrchat.base` copy plus TSMP Core/Luma4. Use
Unity 2022.3.6f1 for this Aoi version. The additional runner arguments are:

```powershell
-PrivateAssetsDirectory '<private Assets>' -AvatarPrefabPath 'Assets/Models/Hayakawa_Aoi/Prefabs/Hayakawa_Aoi.prefab' -DanceAssetPath 'Assets/Kevin Iglesias/Human Animations/Animations/Female/Social/Dance/Steps/HumanF@Dance06.fbx' -AoiSkirtPhysics
```

This option applies `AoiSkirtPhysicsPreset` to a Native-only prefab copy and
copies `OptionalSDK/ShowcasePhysBoneBootstrap.cs` into the private project.
The bootstrap creates the SDK managers and uses its automatic Player loop;
it does not implement or manually step a replacement solver. The builder sets
the SDK core DLL import targets for a Native Windows Player in that private
embedded SDK copy. Source VRChat projects and original prefabs are untouched.
Both live Players simulate their own clothing from the actual Humanoid pose.
Rendering and encoding happen after physics at the end of the frame.

For use in an existing VRChat avatar project, put the preset script in an Editor
folder, place Aoi in the scene, select its root, and run
`TSMP > Showcase > Apply Aoi Skirt Physics to Selected Avatar`. Save the adjusted
instance as a prefab variant. This changes only the existing thigh collider
radii (0.05 to0.055m), the back skirt radius (0.03 to0.04m) and its radius curve
`(0,0.25), (0.58,0.55), (1,1)`, while preserving the original18 skirt links,
forces, limits and avatar descriptor. It requires the original eight skirt
PhysBones and eight thigh colliders; it is not a general avatar preset.

Official SDK simulation checked480 frames of Dance06 and61 skirt transforms.
An independent BakeMesh triangle-intersection check at15 matching poses found
2,055 pairs without physics,1,470 with author physics and1,070 with this preset:
47.9% fewer than no physics,27.2% fewer than author physics. These are surface
intersection pairs, not visible area, depth or incident counts. Some upper rear
thigh/hip intersections remain; this is not a zero-clipping guarantee.
Connecting every leg collider to every skirt chain caused excessive lifting
and was rejected. Actual VRChat client upload/playback was not tested.

Model: [Hayakawa Aoi © 2024 WARPSTORE](https://booth.pm/ja/items/5405703).
Motion: [Human Dance Animations FREE / Kevin Iglesias](https://kevdev.itch.io/human-dance-animations-free).
The reviewed licenses allow rendered videos; raw assets are not redistributed.

## Recorded cube rain media, 2026-11-15

The retained take is 16.033333 seconds, 1440x528 H.264/yuv420p at 30 fps, with
481 encoded frames from 480 capture calls and a 5,718,496-byte MP4.
The 960x352 GIF preview runs for 8.5 seconds at 10 fps and is 3,845,427 bytes.
A static poster is also included in
`.github/assets`. English, Korean and Japanese README headers link the preview
to the MP4.

The independent recording audit passes 26 checks: distinct sender/receiver
processes, matching fixture/Core Runtime source copies, 481 CRC-verified trace
rows, 218 distinct recorded states in order, and about 2.64 seconds of holding
followed by resumed application. It compares 3,000 matching cube/state pairs
across 12 objects: position/scale error 0 and maximum compressed rotation error
0.056 degrees. All 12 cubes fall through more than 1.5 m in the visible scene,
excluding parked bodies and offstage falls. Source physics records 97 floor
contacts, 358 cube-to-cube contacts, 70 releases and 58 pool recycles.
Receiver physics records 12 kinematic bodies with gravity/collision detection off.
The Native fixture checks all 51 transmitted Humanoid bones, all seeded bytes
and 17 fields (the initial payload is 1,240 B). Both Players register 16/16
official PhysBones and record real, finite motion of 61 skirt transforms.
The pause status appears around video seconds 4.00 to 6.50; the receiver status
clears only after resumed CRC-verified frame 198 applies. During the pause the sender keeps dancing
while the receiver holds its last received body/cube poses and clothing settles.
The audit does not independently
recompute per-packet CRC from archived raw payloads; full byte/pose checks run in
the Native fixture. The recording makes no throughput or delivery-rate claim.

The recording, build logs, trace, keyframes and independent audit are kept
under `F:/Unity/TSMP/Validation-Results/readme-pause-label-20261115/take-01`.
Earlier recordings remain in their original evidence folders. Product code
and defaults are unchanged.
