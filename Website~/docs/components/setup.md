---
title: TSMPSetup
---

# TSMPSetup

Use `TSMPSetup` to prepare a scene for TSMP.

This is the component you should visit whenever something about the TSMP scene changes. It connects the encoder, decoder, codec, render textures, network IDs, and generated binding data.

## When you need it

Run setup whenever the scene structure changes. This keeps encoder, decoder, Luma4 references, network IDs, and binding tables aligned.

Click `Apply Setup` after:

- Adding or removing a `TSMPNetwork*` component.
- Changing which object should be encoded or decoded.
- Replacing the encoder output render texture.
- Replacing the decoder source texture.
- Changing codec selection.
- Changing manual network IDs.
- Moving TSMP components into or out of prefabs.

If the encoded texture changes but receiver objects do not move, run `Apply Setup` before debugging anything else.

## Common workflow

1. Drag `TSMPController.prefab` into the scene.
2. Assign texture and scene references in the Reference tab.
3. Confirm Luma4 is selected in the Codec tab.
4. Add TSMP sync components to objects.
5. Click `Apply Setup`.
6. Enter Play mode or test in VRChat.
7. Watch `TSMPDebugCanvas` while moving or interacting with synced objects.

## Tabs

| Tab | Use it for |
| --- | --- |
| Reference | Assign encoder, decoder, textures, render textures, and shared scene references. |
| Codec | Select the Luma4 handler, view codec package metadata, and refresh codec discovery. |
| Bindings | Review synchronized behaviours, generated bindings, and network IDs. |
| Debug | Run manual encode actions and inspect setup diagnostics. |

## What to assign first

For a normal prefab setup, check these fields before pressing `Apply Setup`:

| Field group | What to assign |
| --- | --- |
| Encoder | The `TSMPEncoder` in the controller prefab. |
| Decoder | The `TSMPDecoder` in the controller prefab, or your receiver decoder. |
| Encoder output | The render texture that will be captured or displayed. |
| Decoder source | The texture that contains the received TSMP image. |
| Payload byte texture | Decoder work texture used while reading payload bytes. |
| Codec | The Luma4 handler. |

If the decoder is receiving from OBS or another capture path, `Decoder source` should be that captured texture, not the raw encoder output texture.

## Apply Setup

`Apply Setup` does more than save settings. It updates runtime data used by the encoder and decoder:

- Assigns selected codec references.
- Calculates frame layout and payload capacity.
- Resizes TSMP render textures when enabled.
- Builds encoder and decoder binding tables.
- Assigns or refreshes network IDs.
- Configures codec decode materials.
- Creates runtime codec instances when needed.

After applying, check the Bindings tab. If a component you expected to sync is not listed, the encoder will not send it.

## Safe defaults

Start with:

- Automatic codec discovery enabled.
- Luma4 selected.
- Automatic setup enabled.
- Generated network IDs.
- Default block size and sample size.
- `TSMPDebugCanvas` visible during tests.

Lock down manual IDs and custom references only after the basic stream works.

## Common setup mistakes

| Symptom | Likely setup issue |
| --- | --- |
| Encoder runs but `payload=0` | No enabled TSMP network component was found, or setup was not reapplied. |
| Decoder sees frames but applies nothing | Bindings do not match receiver components or receive interpolation is `None`. |
| Luma4 is missing | Codec package or catalog was not imported/discovered. |
| Data disappears after adding players | Payload capacity is too small for the new data set. |
| Editor output uses old codec | Codec selection changed but setup/materials were not reapplied. |
