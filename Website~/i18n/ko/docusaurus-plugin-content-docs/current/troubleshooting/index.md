---
title: 문제 해결
---

# 문제 해결

항상 sender에서 시작해 바깥쪽으로 확인하세요: encoder, texture transport, decoder, receiver objects 순서입니다.

모든 컴포넌트를 한꺼번에 바꾸지 마세요. TSMP diagnostics를 보면 실패 단계를 좁힐 수 있습니다.

## 빠른 진단 순서

1. TX frame index가 증가합니다.
2. TX payload bytes가 0보다 큽니다.
3. 인코딩 이미지가 receiver input texture에 도달합니다.
4. RX header가 valid입니다.
5. RX frame이 valid입니다.
6. RX message count가 0보다 큽니다.
7. Receiver object가 enabled이고 receive interpolation이 `None`이 아닙니다.

처음 실패하는 단계에서 멈추고 그 부분을 디버깅하세요.

## Output이 flat gray texture입니다

가능성 높은 원인:

- Encoder output이 지정되지 않았습니다.
- Luma4 handler가 지정되지 않았습니다.
- Enabled TSMP network component가 데이터를 보내지 않습니다.
- Component 추가 후 setup을 적용하지 않았습니다.
- Editor-time encode가 꺼져 있습니다.

Encoder frame index가 증가하고 payload bytes가 0보다 큰지 확인하세요.

## Output은 변하지만 receiver가 움직이지 않습니다

확인할 것:

1. Decoder input이 captured texture입니다.
2. Decoder `header=yes`, `valid=yes`입니다.
3. Decoder message count가 0보다 큽니다.
4. Receiver object에 일치하는 TSMP component가 있습니다.
5. Receive interpolation이 `None`이 아닙니다.
6. 마지막 씬 변경 후 `Apply Setup`을 실행했습니다.

RX message count가 0이면 encoder와 transport를 디버깅하세요. RX message count가 0보다 크면 receiver binding과 active state를 확인하세요.

## CRC mismatch

수신 픽셀이 기록된 header와 다릅니다. 확인할 것:

- Scaling 또는 filtering.
- OBS color conversion.
- TSMP 영역 위의 post processing.
- 잘못된 input texture.
- Crop된 header area.
- Sender와 receiver의 호환되지 않는 Luma4 settings.

CRC mismatch가 있으면 frame은 payload processing 전에 버려집니다. Receiver object는 그 frame에서 업데이트될 수 없습니다.

## Payload buffer is full

선택한 데이터를 현재 frame에 모두 담을 수 없습니다.

다음 순서로 해결하세요.

1. 동기화 object를 줄입니다.
2. 선택한 humanoid bones 또는 blend shapes를 줄입니다.
3. 꼭 필요한 경우가 아니면 full avatar pose sync를 피합니다.
4. Output capacity를 늘립니다.
5. `Apply Setup`을 실행합니다.

다른 player가 들어올 때만 나타난다면 avatar pose sync와 player별 payload 증가를 확인하세요.

## RPC calls are missed

짧은 이벤트는 그 이벤트를 담은 video frame이 drop되면 잃을 수 있습니다.

확인할 것:

- Receiver loss percentage.
- TX와 RX의 `rpc` count.
- Duplicate RPC counters.
- Receiving object가 matching network ID를 가지는지.
- `SendTransRPC`에 넘긴 method name이 public method와 정확히 일치하는지.

Loopback 테스트에는 먼저 `TSMPNetworkGameObjectToggle`을 사용하세요.

## Avatar movement is choppy

확인할 것:

- Receiver loss percentage.
- Encoder frame rate.
- Payload capacity.
- Full pose data가 truncate되는지.
- Receive interpolation mode.
- Receiver avatar rig에 예상한 humanoid targets가 있는지.

Root motion은 움직이지만 limbs가 lag 또는 freeze된다면 pose payload가 너무 크거나, selected bones가 불완전하거나, receiver가 frames를 skip하는 경우가 많습니다.

## Luma4가 보이지 않습니다

확인할 것:

1. Luma4 package가 import되어 있습니다.
2. Luma4 codec prefab이 있습니다.
3. `TSMPSetup`이 codec catalog를 refresh했습니다.
4. Selected codec list가 비어 있지 않습니다.

Package import를 고친 뒤 `Refresh Codecs`와 `Apply Setup`을 클릭하세요.

## UdonSharp compile fails

큰 script 또는 assembly 변경 후에는 모든 UdonSharp program을 compile하세요. Unity가 stale Udon fields를 보고하면 Unity C# compilation이 끝날 때까지 기다린 뒤 UdonSharp를 다시 compile합니다.

Unsupported Udon API 오류가 나오면 해당 script가 C#에서는 가능하지만 Udon에는 노출되지 않은 Unity API를 사용하는지 확인하세요.

## Editor에서는 되지만 VRChat에서는 값이 업데이트되지 않습니다

확인할 것:

- 필요한 경우 script가 UdonSharp assembly에 속해 있습니다.
- 최신 code 변경 후 UdonSharp compile이 완료되었습니다.
- Field type이 TSMP와 UdonSharp에서 지원됩니다.
- Receiver component가 uploaded world에서 active입니다.
- Runtime path가 editor-only API에 의존하지 않습니다.
