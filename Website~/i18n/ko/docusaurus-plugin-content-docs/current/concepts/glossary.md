---
title: 용어집
---

# 용어집

Inspector, debug canvas, 로그에서 TSMP 용어가 보일 때 참고하세요.

| 용어 | 의미 |
| --- | --- |
| Frame | 하나의 완전한 TSMP 텍스처 업데이트입니다. Header와 선택적 payload를 포함합니다. |
| Header | 프레임 시작 부분의 고정 56바이트 메타데이터 블록입니다. |
| Payload | Header 뒤의 데이터 본문입니다. 네트워크 메시지가 들어 있습니다. |
| Codec | 바이트를 픽셀로, 픽셀을 바이트로 되돌리는 컴포넌트입니다. |
| Luma4 | 샘플 프리팹에서 사용하는 기본 TSMP 코덱입니다. |
| Network ID | 메시지를 일치하는 `TSMPNetworkBehaviour`로 보내기 위한 ID입니다. |
| Binding | Network ID와 field hash를 컴포넌트 필드에 매핑하는 생성된 설정 데이터입니다. |
| Variable state | 하나 이상의 동기화 필드 값을 담는 페이로드 메시지입니다. |
| TSMP RPC | 텍스처 스트림을 통해 method-like 이벤트를 전달하는 페이로드 메시지입니다. |
| Frame index | 송신자가 증가시키는 프레임 번호입니다. 중복과 손실을 감지하는 데 사용됩니다. |
| CRC | 손상된 헤더를 거부하기 위한 체크섬입니다. |
| Payload capacity | 현재 텍스처와 코덱이 운반할 수 있는 payload byte 수입니다. |
| Receive interpolation | 수신 값을 어떻게 적용할지 결정하는 컴포넌트별 설정입니다. |
| Transport | VRChat 카메라 출력, Spout, OBS 같은 인코딩 텍스처 전달 경로입니다. |

## 자주 보이는 디버그 문구

| 문구 | 보통 의미 |
| --- | --- |
| `Payload buffer is full` | 선택한 동기화 데이터가 현재 프레임에 들어가지 않습니다. |
| `Header CRC mismatch` | 전송 경로가 헤더 픽셀을 바꿨거나 수신자가 잘못된 영역을 읽고 있습니다. |
| `payload=0` | 인코더는 실행 중이지만 선택된 데이터가 쓰이지 않았습니다. |
| RX의 `msg=0` | 디코더가 프레임은 봤지만 사용할 네트워크 메시지를 찾지 못했습니다. |
| `ReceiveInterpolation=None` | 해당 컴포넌트가 의도적으로 수신 값을 무시합니다. |
