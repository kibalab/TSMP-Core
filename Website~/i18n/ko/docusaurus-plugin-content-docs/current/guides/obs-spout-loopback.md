---
title: OBS와 Spout 루프백
---

# OBS와 Spout 루프백

Loopback은 한 대의 PC에서 실제 capture path를 테스트할 때 유용합니다.

기대 결과는 즉시 반응이 아니라 지연입니다. 예를 들어 sender-side avatar가 먼저 움직이고, decoded avatar가 OBS 또는 capture delay 뒤에 따라와야 합니다.

## Sender path

1. Encoder output render texture를 지정합니다.
2. TSMP output을 screen-space 또는 camera output path로 표시합니다.
3. 보이는 출력을 Spout으로 보냅니다.
4. OBS에 Spout source type이 보이지 않는다면 OBS Spout2 plugin을 설치합니다.
5. OBS에 Spout source를 추가합니다.
6. Spout source settings에서 TSMP sender를 선택합니다.
7. OBS가 TSMP 영역을 scale/filter하지 않도록 합니다.

OBS는 Spout source를 기본 built-in source로 포함하지 않습니다. 일반적으로 OBS Spout2 plugin 같은 별도 plugin을 설치해야 Spout input/output을 사용할 수 있습니다.

OBS preview에는 전체 TSMP frame이 보여야 합니다. Preview가 위쪽 header 영역을 crop하면 decoding이 실패합니다.

## Receiver path

1. OBS output을 texture source로 다시 입력합니다.
2. 그 texture를 `TSMPDecoder`에 지정합니다.
3. Sender와 호환되는 Luma4 설정을 유지합니다.
4. `Apply Setup`을 실행합니다.
5. `TSMPDebugCanvas`를 확인합니다.

OBS를 테스트할 때 decoder에 원본 encoder render texture를 지정하지 마세요. 그러면 검증하려는 경로를 우회하게 됩니다.

## 권장 첫 테스트

`TSMPNetworkGameObjectToggle`을 사용하세요.

`RPCTarget.All`에서는 sender가 즉시 toggle되고, OBS delay 뒤 decoded RPC가 다시 toggle되어야 합니다. 이 테스트는 RPC message가 texture path를 통과하는지 확인합니다.

Local toggle은 되지만 delayed toggle이 되지 않는다면:

- TX `rpc`가 증가해야 합니다.
- RX `rpc`가 delay 뒤 증가해야 합니다.
- RX frame loss가 낮아야 합니다.
- Receiver object가 같은 network ID를 가져야 합니다.

## 예상 동작

Loopback이 동작하면:

- Encoder frame index가 증가합니다.
- Decoder frame index가 delay 뒤 따라옵니다.
- Receiver-side transform 또는 avatar가 OBS delay를 두고 따라옵니다.
- CRC warning이 나타나지 않습니다.
- Loss가 낮게 유지됩니다.
- 데이터가 바뀔 때 payload와 message count가 0보다 큽니다.

## Receiver가 움직이지 않을 때

확인할 것:

1. OBS가 TSMP 이미지를 보여주는가.
2. OBS에 plugin을 통해 Spout source가 사용 가능한가.
3. Receiver input texture가 encoder texture가 아니라 OBS output인가.
4. 전체 TSMP frame이 보이고 crop되지 않았는가.
5. Decoder `header=yes`, `valid=yes`인가.
6. Decoder `msg`가 0보다 큰가.
7. Receiver component가 enabled이고 receive interpolation이 `None`이 아닌가.
8. Receiver component 추가 후 `Apply Setup`을 실행했는가.

## 움직임이 지연되거나 끊길 때

일부 지연은 정상입니다. 끊김은 보통 다음을 의미합니다.

- OBS 또는 receiver가 frame을 drop합니다.
- Encoder frame rate가 transport가 운반할 수 있는 수준보다 높습니다.
- Payload가 capacity에 가깝습니다.
- Receiver component가 `Continuous`가 더 적합한데 `Discrete`를 사용 중입니다.

Visual setting을 바꾸기 전에 debug canvas를 확인하세요.
