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
| Sync Rigidbody | Rigidbody가 있으면 속도와 각속도를 송수신하고, 수신한 위치·회전도 해당 Rigidbody에 적용합니다. |
| Compression Mode | 정밀도를 조금 포기하고 payload size를 줄입니다. |

물리가 오브젝트를 움직이는 경우 Rigidbody sync를 켜세요. 중력으로 떨어지는 오브젝트의 jitter를 줄이는 데 도움이 됩니다.

수신 측에서 이 옵션을 끄면 로컬 속도와 각속도를 유지하며 Transform의 위치·회전·스케일은 계속 동기화합니다. Discrete와 Continuous 모두 동일합니다. 끌 때 대기 중인 속도 보간 목표도 지워지므로, 다시 켠 뒤에는 다음 수신 패킷부터 속도가 갱신됩니다. 질량, 중력, Kinematic 설정은 이 컴포넌트가 전송하지 않는 로컬 설정입니다.

## Humanoid pose sync

**Auto Resolve Bones**를 사용하는 고정 무대 리그는 **Cache Fixed Receive Rig**를 켜면 본의 부모 참조를 재사용합니다. 본을 다른 부모 아래로 옮기거나 기존 본 ID 배열의 원소를 바꾼 뒤에는 `InvalidateReceiveRig()`를 호출하세요. 자주 바뀌는 리그는 기본값인 꺼짐이 적합합니다.

**Alternate Hand Updates**는 첫 정상 포즈에서 양손을 적용하고 이후 수신할 때마다 좌우 손가락을 번갈아 갱신합니다. **Alternate Include Wrists**를 켜면 손목도 교대합니다. 몸통은 매번 갱신하고 연속 보간은 양손 모두 유지합니다. 손의 접촉 타이밍이 중요하면 교대를 끄세요. 전송량은 달라지지 않습니다.

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

선택 목록이 고정돼 있다면 **Validate Blend Shape Cache On Receive**를 꺼서 매번 인덱스를 검사하는 비용을 줄일 수 있습니다. 배열 원소를 직접 바꾸면 `_InvalidateBlendShapeCache()`를 호출하세요. 렌더러·메시·인덱스 배열 자체의 교체는 자동으로 감지합니다.

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

`TSMPNetworkTimelineSync`는 `PlayableDirector`의 Timeline 재생 상태와 시간을 동기화합니다.

Receiver가 play/pause와 timeline time을 따라야 할 때 사용합니다. Sender와 receiver의 director setup이 일관되어야 합니다.

양쪽에 Timeline 에셋이 설정된 `PlayableDirector`를 지정하세요. 필드가 비어 있으면 같은 GameObject에서 Director를 찾습니다. Timeline 내용, 길이, 트랙 바인딩, Wrap Mode, Update Mode는 전송하지 않으므로 양쪽을 동일하게 구성해야 합니다.

| Receive Interpolation | 동작 |
| --- | --- |
| `None` | 수신 패킷을 무시하고 진행 중인 보정을 취소합니다. 로컬 재생 자체를 멈추지는 않습니다. |
| `Discrete` | 재생 상태를 즉시 적용하고, 시간 차이가 `Time Apply Threshold`(초)를 넘으면 위치를 맞춥니다. |
| `Continuous` | 패킷 사이에도 `Continuous Interpolation Rate`에 따라 재생 시간 차이를 부드럽게 보정합니다. 루프 경계에서는 더 짧은 방향으로 보정합니다. |

첫 패킷과 재생 상태가 바뀌는 패킷은 임계값과 무관하게 정확한 위치를 적용합니다. 일시정지 위치도 즉시 적용하며, 반복된 Stop 패킷으로 Director 그래프를 다시 만들지 않습니다. Manual Update Mode에서는 수신 위치만 보정하고 재생 시간을 자동으로 진행하지 않습니다. Continuous는 영상 송출 지연을 제거하거나 Timeline Signal을 개별적으로 동기화하는 기능이 아닙니다.

일반 Unity에서는 Director의 실제 재생 상태를 읽습니다. 일시 정지 중 시간을 변경해도 재생으로 판단하지 않으며, 같은 시간을 두 번 캡처해도 일시 정지로 판단하지 않습니다.

VRChat에서는 Director를 직접 호출하는 대신 송신 측 `TSMPNetworkTimelineSync`의 `Play()`, `Pause()`, `Resume()`, `Stop()`으로 제어하세요. Udon에는 Director의 재생 상태와 PlayableGraph를 읽는 API가 없으므로, `playOnAwake`를 초기 상태로 삼고 이 명령들을 추적합니다. 외부 스크립트의 직접 제어와 Timeline의 자동 종료는 정확하게 감지할 수 없습니다. 시퀀스가 끝나면 의도에 맞게 `Stop()` 또는 `Pause()`를 호출해야 합니다. 재생 시간은 계속 Director에서 읽습니다.

[Scripting API의 Timeline 제어](../scripting-api/network-components.md#timeline-controls)도 참고하세요.

## GameObject toggle

`TSMPNetworkGameObjectToggle`은 TSMP RPC로 toggle되는 간단한 interactable object입니다.

`RPCTarget.All` 루프백 테스트에서는 local toggle이 즉시 일어나고, decoded toggle이 stream delay 뒤 한 번 더 일어나야 합니다.

Payload가 매우 작고 delayed RPC 동작을 눈으로 확인하기 쉬우므로 첫 기능 테스트로 권장합니다.
