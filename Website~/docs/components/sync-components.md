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
| Sync Rigidbody | Sends and applies velocity and angular velocity when a Rigidbody exists, and applies received position/rotation through that Rigidbody. |
| Compression Mode | Reduces payload size at the cost of precision. |

Enable Rigidbody sync when physics drives the object. This helps avoid jitter on falling or gravity-driven objects.

Turn it off on a receiver to keep its local velocity and angular velocity. Transform position, rotation and scale still synchronize. This applies to both Discrete and Continuous reception. Disabling the option clears pending velocity interpolation targets; re-enabling it waits for another received packet for velocity updates. Mass, gravity and kinematic settings are local settings, not transmitted by this component.

## Humanoid pose sync

For a fixed stage rig with **Auto Resolve Bones**, enable **Cache Fixed Receive Rig** to reuse bone parents. Call `InvalidateReceiveRig()` after reparenting bones or editing the bone ID array in place. Leave caching off for a changing rig.

**Alternate Hand Updates** decodes both hands on the first valid pose, then alternates fingers on subsequent poses. **Alternate Include Wrists** also alternates the hands themselves. The body still updates every receipt; continuous interpolation still runs on both sides. Leave this off for precise hand contact. Neither option reduces the transmitted bytes.

**Excluded Send Bone Roots** skips the selected subtrees below Hips when sending. This does not filter received poses or require a physics component.

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

For a fixed selection, turn off **Validate Blend Shape Cache On Receive** to skip rechecking each index. After editing an existing index array in place, call `_InvalidateBlendShapeCache()`. Replacing the renderer, mesh or index array is detected automatically.

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

Assign a `PlayableDirector` with a Timeline asset on both sides. If the field is empty, the component looks for a Director on its own GameObject. Use matching Timeline content, duration, track bindings, wrap mode and update mode; these settings and assets are not transmitted.

| Receive Interpolation | Behaviour |
| --- | --- |
| `None` | Ignores incoming packets and cancels pending corrections. Does not stop local playback. |
| `Discrete` | Applies playback-state changes immediately and seeks when drift exceeds `Time Apply Threshold`, in seconds. |
| `Continuous` | Corrects playing-time drift between packets using `Continuous Interpolation Rate`. Looping timelines use the shortest time offset across the boundary. |

The first packet and every playback-state change apply the received position exactly, regardless of the threshold. Paused positions also apply immediately. Repeated Stop packets do not recreate the Director graph. A Director in Manual update mode remains manual: TSMP corrects received positions but does not supply a playback clock. Continuous correction does not remove video-stream latency or synchronize individual Timeline signals.

In ordinary Unity, the component reads the Director's actual playback state. Seeking while paused does not start playback, and sampling twice at the same time does not pause it.

In VRChat, control the sender through `TSMPNetworkTimelineSync.Play()`, `Pause()`, `Resume()` and `Stop()`, rather than calling these methods on the Director directly. Udon does not expose the Director's playback-state getter or PlayableGraph. The component therefore tracks these commands, using `playOnAwake` as the initial state. Direct external controls and automatic end-of-timeline state changes cannot be observed reliably; send the appropriate `Stop()` or `Pause()` command when your sequence ends. Timeline time is still read from the Director.

See [Timeline controls in the Scripting API](../scripting-api/network-components.md#timeline-controls).

## GameObject toggle

Use `TSMPNetworkGameObjectToggle` as a simple interactable object that toggles through TSMP RPC.

For loopback tests with `RPCTarget.All`, the local toggle happens immediately and the decoded toggle should happen again after the stream delay.

This is the recommended first functional test because it uses very little payload and makes delayed RPC behaviour easy to see.
