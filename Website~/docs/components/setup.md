---
title: TSMPSetup
---

# TSMPSetup

Use `TSMPSetup` to prepare a scene for TSMP.

It connects the encoder, decoder, codec, render textures, network IDs, and generated binding data. The same controller prefab and inspector work with or without the VRChat SDK.

## When you need it

Scene instances prepare themselves automatically after import and when the hierarchy or setup settings change. Preparation also runs before saving the scene, entering Play mode, and building. You do not need a conversion menu or a separate SDK-free setup step.

Automatic preparation handles changes such as:

- Adding or removing a `TSMPNetwork*` component.
- Changing which object should be encoded or decoded.
- Replacing the encoder output render texture.
- Replacing the decoder source texture.
- Changing codec selection.
- Changing manual network IDs.
- Moving TSMP components into or out of prefabs.

If the encoded texture changes but receiver objects do not move, check network IDs in the Debug tab and the decoder source. `Apply Setup` is available as an optional manual refresh, not a required step after every edit.

## Common workflow

1. Drag `TSMPController.prefab` into the scene.
2. Assign scene and source-texture references in Setup, and working render textures in Output.
3. Confirm Luma4 is selected in the Codec tab.
4. Add TSMP sync components to objects.
5. Check that the expected objects appear in the Debug tab's Network IDs list.
6. Enter Play mode or test in VRChat.
7. Watch `TSMPDebugCanvas` while moving or interacting with synced objects.

## Tabs

| Tab | Use it for |
| --- | --- |
| Setup | Assign encoder, decoder and source-texture references. |
| Codec | Select the Luma4 handler, view codec package metadata, and refresh codec discovery. |
| Frame | Set dimensions, block size, sampling and orientation. |
| Output | Assign and size output and byte render textures. |
| Auto | Configure automatic preparation and editor encoding. |
| Debug | Review capacity and network IDs, and run a manual encode. |

## What to assign first

The prefab supplies encoder, decoder, codec, and work-texture references automatically. Check or replace these fields for your scene:

| Field group | What to assign |
| --- | --- |
| Encoder | The `TSMPEncoder` in the controller prefab. |
| Decoder | The `TSMPDecoder` in the controller prefab, or your receiver decoder. |
| Encoder output | The render texture that will be captured or displayed. |
| Decoder source | The texture that contains the received TSMP image. |
| Payload byte texture | Decoder work texture used while reading payload bytes. |
| Codec | The Luma4 handler. |

If the decoder is receiving from OBS or another capture path, `Decoder source` should be that captured texture, not the raw encoder output texture.

The default controller uses its encoder output as the decoder source for a local test. Installing the VRChat SDK does not change this setting. Assign the received image explicitly when setting up an external stream.

Package render textures and materials are templates. Each controller gets editable copies in `Assets/TSMPGenerated`; keep these generated assets with the scene in version control. Duplicating a controller gives it separate work textures. User-assigned textures outside the package are retained.

## Apply Setup

Automatic preparation and the optional `Apply Setup` button update the same runtime data:

- Assigns selected codec references.
- Calculates frame layout and payload capacity.
- Resizes TSMP render textures when enabled.
- Builds encoder and decoder binding tables.
- Assigns or refreshes network IDs.
- Configures codec decode materials.
- Creates runtime codec instances when needed.

After preparation, check network IDs in Debug and the Encoder/Decoder binding diagnostics if expected data is missing.

## Safe defaults

Start with:

- Automatic codec discovery enabled.
- Luma4 selected.
- Keep the controller in the scene so its setup references can be prepared.
- Generated network IDs.
- Default block size and sample size.
- `TSMPDebugCanvas` visible during tests.

Lock down manual IDs and custom references only after the basic stream works.

## Common setup mistakes

| Symptom | Likely setup issue |
| --- | --- |
| Encoder runs but `payload=0` | No enabled TSMP network component was found within the configured binding scope. |
| Decoder sees frames but applies nothing | Bindings do not match receiver components or receive interpolation is `None`. |
| Luma4 is missing | Codec package or catalog was not imported/discovered. |
| Data disappears after adding players | Payload capacity is too small for the new data set. |
| Editor output uses old codec | Check the selected codec, the encoder reference, and whether you are viewing this controller's output texture. |
