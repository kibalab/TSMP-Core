---
title: TSMPDecoder
---

# TSMPDecoder

## 헤더와 본문 직접 출력

`useCombinedByteOutput`의 기본값은 `true`입니다. 예측 경로에서 지원 코덱은 복원한 56바이트 헤더와 본문을 전용 RGBA8 readback 텍스처에 직접 출력합니다. 별도 합치기 패스와 본문 중간 출력이 줄어듭니다. 원본 스냅샷, 헤더 CRC 검사, 정확한 길이 비교와 같은 이미지에서의 폴백은 유지됩니다. 전송 데이터그램은 바뀌지 않으며 일정 수신율을 보장하는 기능은 아닙니다.

셰이더가 `_TSMPHeaderTex`와 `_TSMPHeaderPixels`를 모두 구현해야 사용됩니다. 지원하지 않는 기존 코덱은 별도 합치기 경로를 유지합니다. 비교용 스위치는 Diagnostics > Advanced Decode에 있습니다. `combinedByteOutputCount`는 제출 횟수이며 이후 거절되거나 재처리된 예측도 포함합니다. 실제 수신 프레임 수가 아닙니다. `ResetDecodeDiagnostics()`로 초기화합니다.

`TSMPDecoder`는 수신자 쪽에서 사용합니다. TSMP 입력 텍스처를 읽고 디코딩된 메시지를 일치하는 씬 오브젝트에 적용합니다.

대부분의 decode 문제는 입력 텍스처가 변형 없는 TSMP 이미지를 담고 있지 않아서 발생합니다. Receiver component를 바꾸기 전에 texture path를 먼저 확인하세요.

## 필요한 것

- 인코딩된 TSMP frame을 담은 input texture.
- 호환되는 Luma4 handler.
- Payload byte texture.
- 일치하는 TSMP network behaviours와 bindings.
- Reference 지정 후 `TSMPSetup`의 자동 준비 완료.

## 사용 방법

1. Input texture를 지정합니다.
2. Sender와 같은 Luma4 경로를 사용합니다.
3. `TSMPSetup`을 통해 payload byte texture를 지정하거나 auto-size합니다.
4. Receiver object에 일치하는 TSMP network component가 있는지 확인합니다.
5. Setup 자동 준비가 끝났는지 확인합니다. `Apply Setup`은 선택적인 수동 갱신에 사용합니다.
6. 로그 또는 `TSMPDebugCanvas`로 frame status를 확인합니다.

## 프레임 윈도우

실시간 수신에서는 디코더의 **Decode** 섹션에서 **Filter Frames**를 켜 두세요. **Window Size**는 기본 **256**프레임이며 수신기에서 조절합니다. 송신기에 같은 값을 설정할 필요는 없습니다.

중복되거나 오래된 번호는 무시합니다. 다만 마지막 적용 번호가 한 윈도우 이상일 때 0번이 도착하면 재시작으로 허용합니다. 기본값에서는 `256 -> 0`은 허용하고 `255 -> 0`은 거부합니다. UInt32 번호의 정상 순환은 별도로 처리합니다. 윈도우만큼 텍스처를 저장하지 않으며 데이터그램도 바뀌지 않습니다.

세션을 식별하는 기능은 아닙니다. 오래된 0번을 재시작으로 오인하거나 0번 유실로 재시작을 놓칠 수 있습니다. 녹화 영상의 원하는 시점으로 이동할 때는 필터를 끌 수 있지만 과거 변수값도 적용됩니다. [정확한 판정 규칙과 한계](../scripting-api/decoder.md#frame-window)를 참고하세요.

## 정상 decode 상태

`TSMPDebugCanvas`에서 정상 decoder는 다음을 보여야 합니다.

- `valid=yes`
- `header=yes`
- 시간이 지나며 바뀌는 frame index.
- 데이터가 전송 중일 때 0보다 큰 payload size.
- 전송 중인 데이터 종류와 맞는 message count.
- 안정적인 재생 중 낮은 loss.

Frame은 valid인데 object가 움직이지 않으면 network ID, receive interpolation, receiver object active state를 확인하세요. `valid=yes`는 선택한 헤더·구조 검사를 통과했다는 뜻이며 송신 바이트와의 일치를 인증하지 않습니다. CRC32는 헤더 0..51바이트만 보호하고 payload는 포함하지 않습니다. 형식을 유지한 값이나 RPC 인자 변화는 검출하지 못할 수 있습니다. 실제 전송 경로에서 알려진 송신 바이트 또는 애플리케이션 표식과 대조하세요.

## Decode 순서

Decoder는 단계적으로 동작합니다.

1. Input texture에서 header 영역을 읽습니다.
2. Magic, version, header size, CRC를 검증합니다.
3. Codec ID로 codec handler를 선택합니다.
4. Payload byte texture로 payload bytes를 decode합니다.
5. Payload bytes를 읽습니다.
6. Variable state messages와 RPC messages를 dispatch합니다.

위 순서는 첫 프레임과 폴백 경로에 해당합니다. 기본으로 켜진 **Use Predicted Readback**은 이전에 검증한 설정으로 현재 헤더와 payload를 함께 복원해, 순차 GPU readback 대기 두 번을 한 번으로 줄입니다. 현재 헤더를 검증하기 전에는 변수나 RPC를 적용하지 않으며, 설정이나 payload 길이가 달라지면 같은 스냅샷을 기존 경로로 다시 처리합니다.

추가 머티리얼 지정은 필요하지 않습니다. 비교 테스트 시 **Diagnostics > Advanced Decode**에서 끌 수 있습니다. 수동 레이아웃과 안전 모드는 기존 순차 경로를 사용합니다. 추측한 GPU 디코딩이 이미 실행됐어도 CRC가 틀리면 프레임을 폐기합니다. 처리 기회를 늘리는 최적화이지, 전달 보장이나 일정한 수신 프레임레이트를 보장하는 기능은 아닙니다.

## Readback 중첩 {#overlapping-readbacks}

**Diagnostics > Advanced Decode**의 **Overlap Readbacks**는 기본으로 켜져 있습니다. 디코더가 최대 두 이미지를 보관하며, 첫 이미지의 GPU 응답을 기다리는 동안 다음 이미지를 캡처합니다. 결과는 캡처 순서대로 적용합니다. 두 슬롯이 모두 차면 새 캡처를 건너뛰고 대기열을 늘리지 않습니다. 이 옵션을 끄면 1슬롯 동작과 비교할 수 있으며 송신 측 설정은 바꿀 필요가 없습니다.

Readback이 한 번의 업데이트보다 오래 걸릴 때 누락을 줄일 수 있지만, 메모리 사용량과 적용 지연, GPU/CPU 부하가 늘어날 수 있습니다. 스냅샷의 texel 저장량은 입력 형식에 따라 ARGB32 4바이트/픽셀, ARGBHalf 8바이트, ARGBFloat 16바이트입니다. 다른 입력이나 지원하지 않는 형식은 Float32 폴백을 사용할 수 있습니다. Udon 경로는 지원하는 RenderTexture 형식만 일치시키며 다른 텍스처 타입은 Float32를 사용합니다. 1280x720 한 장은 각각 약 3.52 / 7.03 / 14.06 MiB이며, 두 슬롯은 두 배입니다. 바이트 텍스처, CPU 버퍼와 드라이버 오버헤드는 별도입니다. 입력 텍스처에 도착하지 않은 프레임은 복구하지 못합니다. **Pending Frames**는 사용 중인 슬롯 수이고, **Skipped Busy Captures**는 용량 부족으로 거부된 시도 횟수이지 고유 손실 프레임 수가 아닙니다.

## Receive interpolation

각 `TSMPNetworkBehaviour`에는 receive mode가 있습니다.

| Mode | 결과 |
| --- | --- |
| None | 수신 값을 무시합니다. |
| Discrete | 받은 값을 바로 적용합니다. |
| Continuous | 지원하는 컴포넌트에서 target을 향해 부드럽게 보간합니다. |

컴포넌트가 데이터를 보내되 자신에게 들어오는 값은 적용하지 않아야 한다면 `None`을 사용합니다.

## CRC failures

디코더가 CRC mismatch를 기록하면 decode 전에 frame이 손상된 것입니다. 먼저 scaling, filtering, compression, Luma4 setting mismatch를 확인하세요.

일반적인 원인:

- OBS가 TSMP 이미지를 resize했습니다.
- Material 또는 camera effect가 pixel을 바꿨습니다.
- Receiver input이 잘못된 texture입니다.
- Header 영역이 crop되었습니다.
- Sender와 receiver의 texture layout settings가 맞지 않습니다.

## 흔한 해결 방법

| 증상 | 시도할 것 |
| --- | --- |
| `header=no` | 전체 TSMP 이미지가 decoder input에 도달하는지 확인합니다. |
| CRC warning과 `valid=no` | 전송 경로의 scaling/filtering/color processing을 제거합니다. |
| `msg=0` | Encoder payload가 0보다 크고 setup binding이 있는지 확인합니다. |
| RPC count가 0 | `TSMPNetworkGameObjectToggle`로 테스트하고 frame loss를 확인합니다. |
| 값은 수신되지만 적용되지 않음 | Receive interpolation과 active state를 확인합니다. |
