---
title: 텍스처 전송
---

# 텍스처 전송

TSMP는 인코딩된 픽셀이 encoder에서 decoder까지 살아남아야 동작합니다.

TSMP 영역은 데이터 버스처럼 다루세요. 시각적으로는 사소해 보이는 변화도 바이트를 손상시킬 수 있습니다.

## 기본 규칙

TSMP 영역은 영상이 아니라 데이터처럼 복사되어야 합니다. 가능한 한 정확히 유지하세요.

피해야 할 것:

- TSMP 이미지 scaling.
- Bilinear 또는 trilinear filtering.
- Anti-aliasing.
- Color grading.
- Tone mapping.
- TSMP 영역 위의 bloom 또는 post processing.
- 픽셀 값을 바꾸는 video compression.
- Header 또는 payload region cropping.

## VRChat screen-space output

VRChat camera capture에서는 TSMP output이 TSMP camera 또는 screen-space material로 표시되어야 합니다.

일반적인 설정:

1. TSMP output material을 cube 같은 mesh에 적용합니다.
2. VRChat camera를 그 mesh 안에 넣습니다.
3. TSMP 이미지가 capture view를 가득 채우게 합니다.
4. 전체 header와 payload region이 보이게 합니다.

Camera가 mesh 일부만 보면 decoder가 missing header, CRC mismatch, zero messages를 보고할 수 있습니다.

## 해상도

해상도가 높을수록 payload capacity는 커지지만 rendering과 readback 비용도 증가합니다.

샘플 프리팹 기본값으로 시작하세요. 다음 경우에만 해상도를 올리세요.

- `Payload buffer is full`이 나타납니다.
- Debug canvas에서 payload usage가 capacity에 가까워집니다.
- 이미 불필요한 sync data를 줄였습니다.

Transport 문제를 숨기려고 해상도를 올리지 마세요. CRC error는 픽셀을 보존해서 해결해야 합니다.

## Filtering settings

TSMP 데이터를 운반하는 텍스처:

- 가능한 곳에서 point filtering을 사용합니다.
- Mipmap을 피합니다.
- TSMP render texture의 anti-aliasing을 끕니다.
- Sender와 receiver의 color space와 sampling을 일관되게 유지합니다.
- TSMP 영역이 pixel-exact로 유지되는 것을 확신하지 않는다면 OBS에서 resize하지 않습니다.

## OBS와 capture settings

OBS를 사용할 때:

- OBS에서 Spout input/output이 필요하다면 Spout2 plugin을 설치합니다.
- 가능하면 TSMP output resolution 그대로 capture합니다.
- Scaling filter를 피합니다.
- Color correction filter를 피합니다.
- TSMP 영역을 완전히 보이게 합니다.
- Receiver input texture가 원본 encoder render texture가 아니라 OBS output인지 확인합니다.

## 첫 전송 테스트

복잡한 avatar를 동기화하기 전에:

1. `TSMPNetworkGameObjectToggle`을 추가합니다.
2. Local toggle을 확인합니다.
3. Texture path를 통한 delayed decoded toggle을 확인합니다.
4. Transform sync를 추가합니다.
5. 대역폭이 큰 sync component는 마지막에 추가합니다.

Toggle이 도착하지 않으면 더 많은 데이터를 추가하기 전에 transport와 decoder status를 먼저 해결하세요.
