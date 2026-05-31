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

## 그 다음 frame rate 조정

Frame rate가 높으면 latency를 줄일 수 있지만 Udon과 texture 작업도 증가합니다.

Receiver 또는 OBS 경로가 따라가지 못하면 frame rate를 높이는 것이 skipped frames를 늘려 움직임을 더 나쁘게 만들 수 있습니다.

권장 순서:

1. 30 FPS에서 시작합니다.
2. Loss가 낮은지 확인합니다.
3. 움직임에 필요할 때만 frame rate를 올립니다.
4. Loss 또는 CPU cost가 증가하면 더 이상 올리지 않습니다.

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
