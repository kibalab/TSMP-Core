---
title: Sync Components
---

# Sync components

TSMP로 보낼 오브젝트에 다음 컴포넌트를 추가합니다.

이 컴포넌트를 추가, 제거, 이동한 뒤에는 `Apply Setup`을 실행하세요.

## Transform sync

`TSMPNetworkTransformSync`는 GameObject 위치, 회전, 스케일, 선택적 Rigidbody 움직임에 사용합니다.

사용 예:

- 움직이는 소품.
- 물리 오브젝트.
- 송신자를 따라가야 하는 단순 씬 오브젝트.
- Continuous receive interpolation 테스트.

중요 설정:

| 설정 | 의미 |
| --- | --- |
| Target | 동기화할 Transform. 비어 있으면 component transform을 사용합니다. |
| Use Local Space | World transform 대신 local transform을 보냅니다. |
| Sync Rigidbody | Rigidbody가 있으면 velocity와 angular velocity도 보냅니다. |
| Compression Mode | 정밀도를 조금 포기하고 payload size를 줄입니다. |

물리가 오브젝트를 움직이는 경우 Rigidbody sync를 켜세요. 중력으로 떨어지는 오브젝트의 jitter를 줄이는 데 도움이 됩니다.

## Humanoid pose sync

`TSMPNetworkHumanoidPoseSync`는 humanoid Animator rig에 사용합니다.

사용 예:

- 휴머노이드 캐릭터 애니메이션 미러링.
- 선택한 humanoid bones 송신.
- Local 또는 world space root motion 송신.

Inspector에는 humanoid rig selector가 표시됩니다. 처음에는 기본 선택을 사용하고, payload size를 줄여야 할 때만 bone을 제거하세요.

Root movement는 맞지만 팔다리가 딱딱하거나 지연된다면 다음을 확인하세요.

- 애니메이션되는 body part가 선택되어 있는지.
- Receiver rig가 humanoid이며 대응 bone을 가지고 있는지.
- Payload capacity를 초과하지 않는지.
- 전송 경로가 frame을 drop하지 않는지.

## VRChat avatar pose sync

`TSMPNetworkVrchatAvatarPoseSync`는 실시간 VRChat player pose data를 미러링할 때 사용합니다.

Receiver rig가 외부 IK package 없이 전체 humanoid pose를 따라가야 한다면 full mode를 사용하세요. 더 작은 tracking-point 설정은 수신 avatar rig에 IK solution이 있을 때 사용합니다.

이 컴포넌트는 pool에서 avatar instance를 spawn하거나 재사용할 수 있습니다. 여러 player 테스트 전에 avatar prefab과 pool root를 지정하세요.

Full mode는 transform 또는 blend shape sync보다 더 많은 payload capacity가 필요합니다. 여러 player와 테스트할 때 `TSMPDebugCanvas`에서 `payload`와 `loss`를 확인하세요.

## Blend shape sync

`TSMPNetworkBlendShapesSync`는 얼굴 표정 또는 mesh shape key에 사용합니다.

실제로 필요한 blend shape만 선택하세요. 모든 blend shape를 보내면 특히 많은 shape key가 있는 avatar에서 payload capacity를 빠르게 낭비합니다.

Receiver도 선택한 blend shape list를 whitelist로 사용합니다. 수신된 key가 receiver에서 선택되어 있지 않으면 무시됩니다.

## Animator sync

`TSMPNetworkAnimatorSync`는 선택한 Animator parameter 또는 layer state가 sender를 따라야 할 때 사용합니다.

적합한 대상:

- Bool 또는 trigger-like gameplay state.
- 보이는 결과에 영향을 주는 작은 parameter set.
- 정확한 timing이 중요한 Animator layer weight 또는 normalized state.

보이는 출력에 영향을 주지 않는 parameter는 보내지 마세요.

## Timeline sync

`TSMPNetworkTimelineSync`는 `PlayableDirector` 또는 Timeline playback state에 사용합니다.

Receiver가 play/pause와 timeline time을 따라야 할 때 사용합니다. Sender와 receiver의 director setup이 일관되어야 합니다.

## GameObject toggle

`TSMPNetworkGameObjectToggle`은 TSMP RPC로 toggle되는 간단한 interactable object입니다.

`RPCTarget.All` 루프백 테스트에서는 local toggle이 즉시 일어나고, decoded toggle이 stream delay 뒤 한 번 더 일어나야 합니다.

Payload가 매우 작고 delayed RPC 동작을 눈으로 확인하기 쉬우므로 첫 기능 테스트로 권장합니다.
