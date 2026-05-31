---
title: 퀵스타트
---

# 퀵스타트

여기서는 기본 TSMP 송신자와 수신자 설정을 만드는 절차를 다룹니다.

처음 목표는 성능 튜닝이 아닙니다. 먼저 TSMP 프레임이 인코딩되고, 전송되고, 디코딩되고, 보이는 오브젝트에 적용되는지 확인하는 것이 목표입니다.

## 1. 컨트롤러 프리팹 추가

다음 프리팹을 씬에 드래그하세요.

```text
Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab
```

이 프리팹은 권장 시작점입니다. 일반적으로 필요한 컨트롤러 오브젝트, 코덱 참조, 인코더, 디코더, 설정 컴포넌트를 포함합니다.

처음에는 프리팹을 그대로 유지하세요. 송신자와 수신자 오브젝트를 분리하는 작업은 루프백 테스트가 동작한 뒤에 하는 것이 쉽습니다.

## 2. 텍스처 지정

전송 경로에서 사용할 텍스처를 지정합니다.

| 텍스처 | 용도 |
| --- | --- |
| Encoder output render texture | `TSMPEncoder`가 쓰는 텍스처입니다. 표시, 캡처, 스트리밍 대상입니다. |
| Decoder input texture | `TSMPDecoder`가 읽는 텍스처입니다. 실제 스트림에서는 원본 인코더 텍스처가 아니라 캡처된 TSMP 이미지여야 합니다. |
| Payload byte texture | 인코딩된 픽셀을 바이트로 되돌릴 때 사용하는 디코더 작업 텍스처입니다. `TSMPSetup`이 크기를 계산할 수 있습니다. |

로컬 테스트에서는 출력이 Spout, OBS, 카메라 캡처, 기타 루프백 경로를 거쳐 디코더 입력으로 들어갈 수 있습니다.

송신자 출력과 수신자 입력은 같은 텍스처일 필요가 없습니다. 실제 전송에서는 보통 캡처 경로로 연결된 서로 다른 텍스처입니다.

## 3. 동기화할 데이터 선택

보낼 오브젝트에 TSMP 네트워크 컴포넌트를 추가합니다.

| 컴포넌트 | 용도 |
| --- | --- |
| `TSMPNetworkTransformSync` | 오브젝트 위치, 회전, 스케일, 선택적 Rigidbody 움직임. |
| `TSMPNetworkHumanoidPoseSync` | 휴머노이드 Animator 리그와 선택한 휴머노이드 본. |
| `TSMPNetworkBlendShapesSync` | 선택한 얼굴 또는 메시 블렌드셰이프. |
| `TSMPNetworkAnimatorSync` | 선택한 Animator 파라미터와 레이어 상태. |
| `TSMPNetworkTimelineSync` | Timeline 또는 `PlayableDirector` 재생 상태. |
| `TSMPNetworkVrchatAvatarPoseSync` | 실시간 VRChat 플레이어 포즈를 아바타 인스턴스에 미러링. |
| `TSMPNetworkGameObjectToggle` | 간단한 Interact와 TSMP RPC 테스트. |

활성 GameObject의 활성 컴포넌트만 참여합니다. 컴포넌트나 GameObject를 비활성화하면 TSMP 데이터 송수신이 중지됩니다.

## 4. 설정 적용

`TSMPSetup`이 붙은 오브젝트를 선택하고 `Apply Setup`을 클릭합니다.

다음 작업 후에는 다시 실행하세요.

- TSMP 네트워크 컴포넌트를 추가하거나 제거한 경우.
- 동기화 오브젝트를 프리팹이나 씬 오브젝트 사이에서 옮긴 경우.
- Luma4 또는 텍스처 참조를 바꾼 경우.
- 수동 네트워크 ID를 바꾼 경우.
- 인코더, 디코더, 코덱 오브젝트를 교체한 경우.

`Apply Setup`은 인코더와 디코더가 사용하는 바인딩 테이블을 다시 만들기 때문에 중요합니다.

## 5. 스트림 테스트

Play 모드나 VRChat에서 테스트합니다.

정상 동작의 신호는 다음과 같습니다.

- 동기화 오브젝트가 움직이면 인코더 출력 텍스처가 변합니다.
- 인코더 프레임 인덱스가 증가합니다.
- 데이터가 선택된 경우 Encoder payload bytes가 0보다 큽니다.
- 디코더가 유효한 헤더와 유효한 프레임을 보고합니다.
- 수신 오브젝트가 캡처 경로 지연 뒤에 업데이트됩니다.
- `TSMPDebugCanvas`가 프레임 인덱스, 페이로드, 메시지, 손실 정보를 표시합니다.

캡처 루프백은 [OBS와 Spout 루프백](../guides/obs-spout-loopback.md)을 참고하세요.

## 권장 첫 테스트

처음에는 `TSMPNetworkGameObjectToggle`을 사용하세요.

`RPCTarget.All`에서는 Interact 시 송신자에서 즉시 토글되고, 스트림 지연 뒤 디코딩된 RPC가 다시 토글됩니다. 이 테스트는 짧은 이벤트 메시지가 전송 경로를 통과하는지 확인해 줍니다.

그 다음 `TSMPNetworkTransformSync`를 추가하고, 마지막에 휴머노이드 포즈나 VRChat 아바타 포즈처럼 대역폭이 큰 컴포넌트를 추가하세요.

## 아무것도 움직이지 않을 때

다음 순서로 확인하세요.

1. `TSMPEncoder` 프레임 인덱스가 증가하는가.
2. Encoder payload bytes가 0보다 큰가.
3. 인코딩된 텍스처가 전송 경로에서 보이는가.
4. `TSMPDecoder`가 `header=yes`, `valid=yes`를 보고하는가.
5. Decoder message count가 0보다 큰가.
6. 수신 오브젝트에 일치하는 TSMP 네트워크 컴포넌트가 있는가.
7. Receive interpolation이 `None`이 아닌가.
8. 마지막 씬 변경 후 `TSMPSetup`을 적용했는가.
