---
title: Sync Components
---

# Sync components

Add these components to the objects you want TSMP to send.

After adding, removing, or moving any of these components, run `Apply Setup`.

## Transform sync

Use `TSMPNetworkTransformSync` for GameObject position, rotation, scale, and optional Rigidbody movement.

Use it for:

- Moving props.
- Physics objects.
- Simple scene objects that should follow the sender.
- Testing continuous receive interpolation.

Important settings:

| Setting | Meaning |
| --- | --- |
| Target | Transform to synchronize. Defaults to the component transform if empty. |
| Use Local Space | Sends local transform instead of world transform. |
| Sync Rigidbody | Also sends velocity and angular velocity when a Rigidbody exists. |
| Compression Mode | Reduces payload size at the cost of precision. |

Enable Rigidbody sync when physics drives the object. This helps avoid jitter on falling or gravity-driven objects.

## Humanoid pose sync

Use `TSMPNetworkHumanoidPoseSync` for a humanoid Animator rig.

Use it for:

- Mirroring animation from a humanoid character.
- Sending selected humanoid bones.
- Sending root motion in local or world space.

The Inspector shows a humanoid rig selector. Start with the default selection, then remove bones only when you need to reduce payload size.

If root movement looks correct but limbs feel stiff or delayed, verify:

- The animated body parts are selected.
- The receiver rig is humanoid and has matching bones.
- Payload capacity is not being exceeded.
- The transport is not dropping frames.

## VRChat avatar pose sync

Use `TSMPNetworkVrchatAvatarPoseSync` when you want to mirror live VRChat player pose data.

Use full mode when you want the receiver rig to follow the full humanoid pose without depending on an external IK package. Use a smaller tracking-point setup only when your receiving avatar rig has an IK solution.

This component can spawn or reuse avatar instances from a pool. Assign the avatar prefab and pool root before testing with multiple players.

Full mode needs more payload capacity than transform or blend shape sync. Watch `payload` and `loss` in `TSMPDebugCanvas` while testing with multiple players.

## Blend shape sync

Use `TSMPNetworkBlendShapesSync` for facial expressions or mesh shape keys.

Select the blend shapes you actually need. Sending every blend shape can quickly waste payload capacity, especially on avatars with many shape keys.

The receiver also uses its selected blend shape list as a whitelist. If a received key is not selected on the receiver, it is ignored.

## Animator sync

Use `TSMPNetworkAnimatorSync` for selected Animator parameters or layer state that should follow the sender.

Good candidates:

- Bool or trigger-like gameplay state.
- Small sets of visible parameters.
- Animator layer weights or normalized state when exact timing matters.

Avoid sending parameters that do not affect visible output.

## Timeline sync

Use `TSMPNetworkTimelineSync` for `PlayableDirector` or Timeline playback state.

Use it when a receiver should follow play/pause and timeline time. Keep the director setup consistent between sender and receiver.

## GameObject toggle

Use `TSMPNetworkGameObjectToggle` as a simple interactable object that toggles through TSMP RPC.

For loopback tests with `RPCTarget.All`, the local toggle happens immediately and the decoded toggle should happen again after the stream delay.

This is the recommended first functional test because it uses very little payload and makes delayed RPC behaviour easy to see.
