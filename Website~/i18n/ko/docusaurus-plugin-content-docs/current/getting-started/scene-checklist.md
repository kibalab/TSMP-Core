---
title: 씬 체크리스트
---

# 씬 체크리스트

VRChat 테스트, OBS 루프백 녹화, 샘플 씬 공유 전에 이 체크리스트를 확인하세요.

## 컨트롤러

- 씬에 `TSMPController.prefab`이 있거나, 동등한 `TSMPSetup`, `TSMPEncoder`, `TSMPDecoder`, Luma4 코덱 오브젝트가 있습니다.
- `TSMPSetup`에 의도한 인코더와 디코더가 지정되어 있습니다.
- Codec 탭에서 Luma4가 선택되어 있습니다.
- 마지막 변경 이후 `Apply Setup`을 클릭했습니다.
- 코덱 런타임 인스턴스가 예상한 컨트롤러 오브젝트 아래에 있고 중복되지 않았습니다.

## 텍스처

- Encoder output이 유효한 `RenderTexture`입니다.
- Decoder input이 관계없는 프리뷰 텍스처가 아니라 캡처된 TSMP 텍스처를 가리킵니다.
- Payload byte texture가 존재하고 디코딩 페이로드를 담을 만큼 큽니다.
- 송신자와 수신자가 같은 TSMP 영역을 봅니다.
- 인코딩된 텍스처가 디코드 전에 스케일, 블러, 색 보정, 포스트 프로세싱, 압축을 거치지 않습니다.
- 가능한 곳에서 point filter를 사용하고, mipmap과 anti-aliasing이 꺼져 있습니다.

## 네트워크 컴포넌트

- 데이터를 보낼 오브젝트에 TSMP sync 컴포넌트가 있습니다.
- GameObject와 컴포넌트가 활성화되어 있습니다.
- Network ID가 고유합니다.
- 수신 오브젝트에 일치하는 TSMP 컴포넌트와 바인딩이 있습니다.
- 의도적으로 수신 값을 무시하는 경우가 아니라면 Receive interpolation이 `None`이 아닙니다.
- `TSMPNetworkBlendShapesSync`는 실제로 필요한 블렌드셰이프만 선택했습니다.
- Humanoid sync는 보고 싶은 애니메이션 부위의 본을 포함합니다.

## 용량

- Encoder payload bytes가 usable payload bytes보다 작습니다.
- Full avatar pose sync는 필요할 때만 사용합니다.
- Blend shape sync는 선택한 키만 포함합니다.
- 물리로 움직이는 Transform 오브젝트에는 Rigidbody sync가 켜져 있습니다.
- 두 번째 플레이어나 다른 동기화 오브젝트를 추가해도 payload가 용량을 넘지 않습니다.

## 런타임 확인

- Encoder frame index가 증가합니다.
- Decoder frame index가 전송 지연 뒤 따라옵니다.
- Decoder header와 frame이 valid입니다.
- `TSMPDebugCanvas`에서 loss가 낮게 유지됩니다.
- TX message count가 활성화한 기능과 맞습니다.
- RX message count가 0에 고정되어 있지 않습니다.
- Last error가 비어 있거나 원인을 이해할 수 있습니다.

## 디코더를 의심하기 전에

대부분의 TSMP 문제는 최종 적용 단계가 아니라 설정이나 전송 경로에서 발생합니다. 단계별로 확인하세요.

1. 송신자가 0보다 큰 payload를 쓰는가.
2. 수신자가 유효한 header를 보는가.
3. 수신자가 message를 보는가.
4. network ID와 receive mode가 메시지 적용을 허용하는가.
