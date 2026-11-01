---
title: 성능 튜닝
---

# 성능 튜닝

TSMP는 먼저 payload를 줄이고, 그 다음 frame rate와 texture size를 조정하는 순서로 튜닝합니다.

시각적 부드러움만으로 성능을 판단하면 안 됩니다. Payload 문제, frame loss 문제, interpolation 문제는 비슷하게 보일 수 있습니다.

## Debug canvas부터 보기

확인할 것:

- `payload`: 각 frame이 얼마나 찼는지.
- `msg`, `var`, `rpc`: 얼마나 많은 data가 쓰이는지.
- `loss`: receiver가 놓친 것으로 보이는 frame 수.
- `valid`, `header`: decode가 안정적인지.
- Last error text.

시각적 smoothness만 보고 튜닝하지 마세요. Transport 문제와 payload 문제는 비슷하게 보일 수 있습니다.

## 먼저 payload 줄이기

| Source | Action |
| --- | --- |
| Transform sync | 꼭 필요한 object만 sync합니다. |
| Humanoid pose | 가능하면 bone 수를 줄입니다. |
| VRChat avatar pose | receiver에 full pose data가 필요할 때만 full mode를 사용합니다. |
| Blend shapes | 보이거나 gameplay에 중요한 shape만 선택합니다. |
| Animator | visible state에 영향을 주는 parameter만 sync합니다. |
| Timeline | sender와 맞아야 하는 director만 sync합니다. |

Payload 감소는 encode work, transport bandwidth, decode work, apply work를 동시에 줄이기 때문에 보통 가장 좋은 첫 최적화입니다.

## 실제 전송 바이트 제한

`TSMPEncoder.targetPayloadBytes`는 backing buffer가 커져도 각 network payload의 전송량을 제한합니다. 기본값 `0`은 기존 raster capacity 한도를 사용합니다. 양수 제한에는 network/message header도 포함되며, 완전한 필드나 RPC가 들어가야 합니다. 필드를 분할해서 보내지는 않습니다.

설정을 바꿀 때 `effectivePayloadBudgetBytes`, `deferredVariableCount`, `deferredRpcCount`, `queuedRpcCount`를 확인하세요. 들어가지 않는 필드는 전송을 미룹니다. RPC를 큐에 넣은 뒤 제한을 줄이면 큰 RPC가 맨 앞에 남아 뒤 RPC도 지연시킬 수 있습니다. 제한을 늘리면 남은 반복 횟수를 보존한 채 다시 전송합니다.

Encoder Inspector에는 마지막으로 미룬 필드와 막힌 맨 앞 RPC, 각 항목 하나만 담은 완전한 패킷에 필요한 바이트 수가 표시됩니다. 이 크기를 실제 예산과 비교하세요. 단독으로 들어가는 필드도 다른 데이터가 이번 프레임의 공간을 사용하면 기다릴 수 있습니다. `oversizedVariableCount`는 전송 대상 중 단독으로도 들어가지 않는 필드 수입니다. 이 진단은 마지막 인코딩 시도의 상태이며 수신 데이터의 경과 시간이나 수신 확인을 뜻하지 않습니다. 진단을 위한 추가 직렬화는 수행하지 않습니다.

패킷 성공률과 함께 실제 적용된 갱신, 필드별 데이터 경과 시간, 이벤트 도달 여부를 비교하세요. 작은 패킷이 더 잘 통과해도 일부 데이터는 계속 도착하지 않을 수 있습니다. 자동 조절을 추가하기 전에 실제 부하로 고정 제한값을 선택하세요.

## 그 다음 frame rate 조정

Frame rate가 높으면 latency를 줄일 수 있지만 Udon과 texture 작업도 증가합니다.

Receiver 또는 OBS 경로가 따라가지 못하면 frame rate를 높이는 것이 skipped frames를 늘려 움직임을 더 나쁘게 만들 수 있습니다.

권장 순서:

1. 30 FPS에서 시작합니다.
2. Loss가 낮은지 확인합니다.
3. 움직임에 필요할 때만 frame rate를 올립니다.
4. Loss 또는 CPU cost가 증가하면 더 이상 올리지 않습니다.

## CRC 복원 비용 측정

`TSMPDecoder.maxCrcRecoveryAttempts`는 보호된 CRC가 실패한 뒤 대체 payload 디코딩을 0·1·2회 허용합니다. 기본값은 2회입니다. 0은 재시도를 끄며 일반 CRC 검증은 유지합니다. `recoverHeaderCrc`는 기본적으로 꺼져 있습니다.

재시도는 같은 캡처 이미지를 사용하며 GPU 작업, 비동기 readback 대기, 콜백 처리를 추가할 수 있습니다. 콜백 CPU 시간과 요청부터 적용까지의 지연을 나누어 측정하고, p95와 정상적으로 적용된 고유 업데이트도 기록하세요. GPU 패스 시간만으로 전체 decoder 비용이나 VRChat FPS를 판단할 수 없습니다.

전달 품질과 지연 요구를 모두 만족하는 횟수를 선택하세요. 첫 번째가 실패해도 두 번째에서 복원되는 프레임이 있습니다. 수신 polling도 같은 영상 프레임을 반복 처리할 수 있으므로, 고정 주기로 제한하려면 원본 ID와 RPC 누락을 별도로 검사해야 합니다.

## 그 다음 capacity 조정

`Payload buffer is full`이 나타나면:

1. 동기화 데이터를 줄입니다.
2. Output texture capacity를 늘립니다.
3. `Apply Setup`을 다시 실행합니다.
4. Payload가 usable capacity보다 낮은지 확인합니다.

모든 capacity 문제를 해상도 증가로 해결하지 마세요. 큰 texture는 render, capture, decode 비용이 큽니다.

## Interpolation 선택

컴포넌트별 receive interpolation을 의도적으로 선택하세요.

| Mode | 비용 | 시각 결과 |
| --- | --- | --- |
| None | 가장 낮음 | 수신 값을 무시합니다. |
| Discrete | 낮음 | 받은 상태로 즉시 snap합니다. |
| Continuous | 더 높음 | 지원 컴포넌트가 target으로 smooth하게 이동합니다. |

Continuous mode는 transport delay를 덜 어색하게 만들 수 있지만 누락된 데이터를 복구하지는 못합니다. Loss가 높다면 loss를 먼저 해결하세요.

## 여러 플레이어

Player count가 늘어나면 특히 avatar pose sync에서 payload가 빠르게 증가할 수 있습니다.

다른 player가 들어오면 데이터가 사라지는 경우:

- Payload capacity를 확인합니다.
- 각 player가 추가 avatar pose data를 만드는지 확인합니다.
- Remote player에게 필요 없는 sync component를 끕니다.
- Texture resolution을 올리기 전에 avatar pose details를 줄입니다.
- Visual 최적화 전에 debug canvas를 보며 테스트합니다.

## 실용적인 튜닝 순서

1. Low-bandwidth toggle로 valid decode를 확인합니다.
2. Transform sync를 추가합니다.
3. High-bandwidth feature를 하나씩 추가합니다.
4. 추가할 때마다 payload usage를 확인합니다.
5. Transport와 capacity가 안정된 뒤 receive interpolation을 조정합니다.

## 필드 수와 대상 객체 수 측정

같은 payload 크기라도 Udon 처리 비용은 다를 수 있습니다. byte 배열 필드 하나와 여러 scalar 필드, behaviour 하나와 여러 behaviour를 각각 비교하세요. 변경 감지도 전송 대상 필드를 읽고 직렬화한 뒤 바이트를 비교하므로, 값이 그대로인 경우와 일부만 바뀌는 경우도 측정합니다.

첫 인코딩과 실행 중 바인딩 변경은 안정된 반복 처리와 구분해서 측정하세요. Udon 인코더는 기존 송신 상태를 다시 만들 때 대상별 BeforeEncode 그룹을 캐시하고, 평상시에는 이 매핑을 재사용합니다. 바인딩이 많으면 필드 접근·직렬화·수신 적용 비용도 남습니다. 전체 처리시간과 함께 필드별 갱신 지연과 RPC 큐 대기시간을 기록하세요.
