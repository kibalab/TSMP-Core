---
title: Built-in Network Components API
---

# Built-in network components

TSMP는 VRChat world에서 자주 쓰는 synchronization case를 위한 여러 `TSMPNetworkBehaviour` component를 제공합니다. 모두 `networkId`, receive interpolation, active-state check, setup binding을 공유합니다.

## Common behaviour

| Feature | 의미 |
| --- | --- |
| `networkId` | Sender와 receiver component를 matching하는 stable ID. |
| `receiveInterpolation` | `None`, `Discrete`, `Continuous` received-value handling. |
| Active state | Disabled component 또는 inactive GameObject는 참여하지 않습니다. |
| `TSMPBeforeEncode()` | Encoder가 `[TransSync]` field를 읽기 전에 state를 capture합니다. |
| `OnTSMPVariableReceived()` | Frame receive 후 decoded state를 적용합니다. |

## Component overview

| Component | 동기화 대상 |
| --- | --- |
| `TSMPNetworkTransformSync` | Transform position, rotation, scale, compression range, optional Rigidbody state. |
| `TSMPNetworkHumanoidPoseSync` | 선택한 humanoid bones와 root motion position. |
| `TSMPNetworkBlendShapesSync` | 하나의 `SkinnedMeshRenderer`에 대한 선택 blendshape weights. |
| `TSMPNetworkVrchatAvatarPoseSync` | Avatar-pool playback용 VRChat player tracking pose. |
| `TSMPNetworkAnimatorSync` | Runtime path가 지원하는 Animator parameters와 layer-related state. |
| `TSMPNetworkTimelineSync` | Udon에서 지원되는 PlayableDirector timeline time/play state. |
| `TSMPNetworkGameObjectToggle` | TSMP RPC를 통한 toggle event. |
| `TSMPDebugCanvas` | Frame counters, bitrate, loss, header metadata 표시용 text UI. |

## Choosing a component

Data model이 맞으면 built-in component를 사용하세요. 다음 경우 custom `TSMPNetworkBehaviour`를 작성합니다.

- 다른 packed byte format이 필요함.
- 여러 field를 하나의 packet으로 동기화해야 함.
- Custom receive interpolation policy가 필요함.
- TSMP RPC로 logic을 실행해야 함.

## Binding rule

Network component를 추가하거나 제거한 뒤 `Apply Setup`을 실행하세요. Encoder와 decoder는 uploaded world에서 매 frame field를 discover하지 않고, performance와 Udon compatibility를 위해 explicit binding table을 사용합니다.
