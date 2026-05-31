---
title: Built-in Network Components API
---

# Built-in network components

TSMP includes several `TSMPNetworkBehaviour` components that cover common VRChat world synchronization cases. They all share `networkId`, receive interpolation, active-state checks, and setup binding.

## Common behaviour

| Feature | Meaning |
| --- | --- |
| `networkId` | Stable ID used to match sender and receiver components. |
| `receiveInterpolation` | `None`, `Discrete`, or `Continuous` received-value handling. |
| Active state | Disabled components or inactive GameObjects do not participate. |
| `TSMPBeforeEncode()` | Captures component state before the encoder reads `[TransSync]` fields. |
| `OnTSMPVariableReceived()` | Applies decoded state after a frame is received. |

## Component overview

| Component | What it synchronizes |
| --- | --- |
| `TSMPNetworkTransformSync` | Transform position, rotation, scale, compression range, and optional Rigidbody state. |
| `TSMPNetworkHumanoidPoseSync` | Selected humanoid bones plus root motion position. |
| `TSMPNetworkBlendShapesSync` | Selected blendshape weights for one `SkinnedMeshRenderer`. |
| `TSMPNetworkVrchatAvatarPoseSync` | VRChat player tracking pose for avatar-pool playback. |
| `TSMPNetworkAnimatorSync` | Animator parameters and layer-related state supported by the runtime path. |
| `TSMPNetworkTimelineSync` | PlayableDirector timeline time/play state where supported by Udon. |
| `TSMPNetworkGameObjectToggle` | Toggle event through TSMP RPC. |
| `TSMPDebugCanvas` | Text UI for frame counters, bitrate, loss, and header metadata. |

## Choosing a component

Use built-in components when the data model matches your object. Write a custom `TSMPNetworkBehaviour` when:

- You need a different packed byte format.
- You need to synchronize several fields as one packet.
- You need a custom receive interpolation policy.
- You need to run logic from a TSMP RPC.

## Binding rule

After adding or removing network components, run `Apply Setup`. The encoder and decoder do not discover fields every frame in runtime worlds; setup creates explicit binding tables for performance and Udon compatibility.
