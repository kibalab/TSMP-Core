# Demo recording

Two Unity Players show the sender, encoded Luma4 frame and receiver side by side.
The sender runs a Humanoid animation and physics for 12 falling cubes. A localhost
connection carries the encoded texture; the receiver applies the decoded poses.
This is a local Unity demo, without VRChat, OBS or CDN testing.

Transmission pauses from 4 to 6.5 seconds to show holding and recovery. The sender
keeps moving. `[PAUSED]` stays on the receiver until new data has been applied.

## Record

Use Windows, Unity 2022.3, FFmpeg and a Native Unity template project with TSMP
Core and Luma4 installed, without the Worlds SDK. Choose a new output directory.

```powershell
./Validation~/Showcase/Run-Recording.ps1 `
  -TemplateProject '<Native Unity project>' `
  -WorkDirectory '<new directory>' `
  -UnityEditor '<Unity.exe>' `
  -Ffmpeg '<ffmpeg.exe>'
```

The runner copies the template's Packages and ProjectSettings, builds the Player
and records both processes. The default actor is a generated Humanoid.

## Aoi and PhysBones

For the README's Aoi recording, use Unity 2022.3.6f1 and a template containing an
embedded `Packages/com.vrchat.base` SDK copy. Prepare a separate Assets directory
with the original Hayakawa Aoi v1.02 prefab, materials and motion assets; exclude
the original asset C# scripts. Append these arguments to the command above:

```powershell
-PrivateAssetsDirectory '<private Assets>' `
-AvatarPrefabPath 'Assets/Models/Hayakawa_Aoi/Prefabs/Hayakawa_Aoi.prefab' `
-DanceAssetPath 'Assets/Kevin Iglesias/Human Animations/Animations/Female/Social/Dance/Steps/HumanF@Dance06.fbx' `
-AoiSkirtPhysics
```

`-AoiSkirtPhysics` applies the skirt preset to a prefab copy and installs the SDK
manager bootstrap. The builder configures the private SDK copy for a Windows
Player. Each process runs the SDK PhysBone solver for its own clothing and hair.

To apply the preset in an avatar project, copy `Editor/AoiSkirtPhysicsPreset.cs`
into an Editor folder, select an Aoi scene instance and use
`TSMP > Showcase > Apply Aoi Skirt Physics to Selected Avatar`.
Save the instance as a prefab variant. The preset sets thigh collider radii to
0.055 m and the back skirt radius to 0.04 m, with curve
`(0, 0.25), (0.58, 0.55), (1, 1)`. Some upper rear skirt intersections remain.

Model: [Hayakawa Aoi © 2024 WARPSTORE](https://booth.pm/ja/items/5405703).
Motion: [Human Dance Animations FREE / Kevin Iglesias](https://kevdev.itch.io/human-dance-animations-free).
These assets and SDK binaries are not included in this repository.

## Output

The recording is a silent 16-second H.264 MP4 at 1440×528 and 30 fps. The README
uses a GIF preview linked to the MP4 in `.github/assets`.

The output directory contains the video, build and process logs,
`recording-result.txt`, pose journals and `pause-visibility.csv`.
Check the recording result and review the video before replacing README media.
