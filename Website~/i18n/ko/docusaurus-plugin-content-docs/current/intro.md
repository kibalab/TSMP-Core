---
sidebar_position: 1
title: 소개
---

# TSMP

TSMP, Trans Sync Media Protocol은 런타임 데이터를 텍스처 프레임으로 보내는 프로토콜입니다.

VRChat 월드에서는 송신자가 씬 상태를 보이는 텍스처로 인코딩하고, 캡처나 스트리밍 경로가 그 텍스처를 전달하며, 수신자가 다시 오브젝트 움직임, 아바타 포즈, 블렌드셰이프, Animator 상태, Timeline 상태, TSMP RPC 이벤트로 디코딩할 수 있습니다.

## 어디서 시작할까

프리팹을 배치하고, 기본 동기화 컴포넌트를 사용하고, 텍스처 전송 경로를 디버깅하려면 시작하기 섹션부터 보면 됩니다.

직접 TSMP 컴포넌트, 도구, 테스트, 코덱 패키지를 작성하려면 개발자 섹션과 스크립팅 API를 참고하세요.

## 기본 구조

TSMP는 네 단계 파이프라인으로 이해하면 쉽습니다.

```text
씬 데이터 -> TSMPEncoder -> 텍스처/영상 경로 -> TSMPDecoder -> 씬 데이터
```

- 씬 데이터는 활성화된 `TSMPNetwork*` 컴포넌트에서 나옵니다.
- `TSMPEncoder`는 출력 렌더 텍스처에 TSMP 프레임을 씁니다.
- 텍스처 경로는 VRChat 카메라 뷰, Spout, OBS, 기타 캡처 경로가 될 수 있습니다.
- `TSMPDecoder`는 수신 텍스처를 읽고 일치하는 TSMP 네트워크 컴포넌트에 메시지를 적용합니다.

이 텍스쳐는 데이터를 인코딩한 결과물입니다. 스케일링, 필터링, 색 보정, 압축, 크롭은 데이터를 손상시킬 수 있습니다.

## 프리팹부터 시작하기

권장 시작점은 다음 프리팹입니다.

```text
Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab
```

이 프리팹을 씬에 넣고 필요한 텍스처와 참조를 지정한 뒤 `TSMPSetup`에서 `Apply Setup`을 실행하세요.

복잡한 씬을 만들기 전에 먼저 루프백 테스트를 하는 것이 좋습니다. 루프백은 인코더, 전송 경로, 디코더, 바인딩, 수신 오브젝트가 서로 맞는지 확인해 줍니다.

## TSMP가 씬에 추가하는 것

| 컴포넌트 | 용도 |
| --- | --- |
| `TSMPSetup` | 참조, 코덱 선택, 렌더 텍스처, 바인딩, 네트워크 ID, 런타임 테이블을 설정합니다. |
| `TSMPEncoder` | 출력 렌더 텍스처에 송신 TSMP 프레임을 만듭니다. |
| `TSMPDecoder` | 입력 TSMP 텍스처를 읽고 디코딩된 메시지를 전달합니다. |
| `TSMPNetwork*` 컴포넌트 | 어떤 씬 데이터를 보내거나 받을지 선택합니다. |
| `TSMPDebugCanvas` | 인게임에서 프레임, 대역폭, 손실률, 페이로드, 헤더, 오류 상태를 보여줍니다. |

TSMP에는 기본 코덱인 Luma4가 포함되어 있습니다. 기본 설정 경로는 Luma4를 기준으로 합니다.

## TSMP에 적합한 용도

- 텍스처 스트림을 통한 오브젝트 Transform 미러링.
- 인스턴스간 동기화.
- 전송 지연이 있는 아바타 포즈 재생.
- 선택한 휴머노이드 포즈, 블렌드셰이프, Animator, Timeline 상태 송신.
- GameObject 토글 같은 RPC 방식 이벤트 실행.
- VRChat 월드 안에서 전송 품질 확인.

## TSMP가 숨겨주지 않는 것

TSMP는 텍스처 경로의 품질에 의존합니다. 수신 픽셀이 바뀌면 디코더가 프레임을 거부하거나 메시지를 덜 적용할 수 있습니다. 항상 다음 순서로 디버깅하세요.

1. 송신자가 프레임을 만들고 있는가.
2. 텍스처 경로가 전체 이미지를 변형 없이 전달하는가.
3. 디코더가 헤더와 페이로드를 받아들이는가.
4. 수신 오브젝트에 일치하는 TSMP 컴포넌트와 네트워크 ID가 있는가.

## 다음 단계

1. VPM 저장소에서 [TSMP 설치](./getting-started/installation.md)를 진행합니다.
2. `TSMPController.prefab`으로 [퀵스타트](./getting-started/quickstart.md)를 따라 합니다.
3. VRChat 테스트 전에 [씬 체크리스트](./getting-started/scene-checklist.md)를 확인합니다.
4. 첫 전송 경로를 설정하는 동안 [문제 해결](./troubleshooting/index.md)을 함께 열어 두세요.
