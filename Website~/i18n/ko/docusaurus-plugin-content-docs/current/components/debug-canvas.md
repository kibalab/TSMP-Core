---
title: TSMPDebugCanvas
---

# TSMPDebugCanvas

`TSMPDebugCanvas`는 Play mode 또는 업로드된 VRChat 월드 안에서 TSMP 상태를 보기 위한 컴포넌트입니다.

인게임에서는 Inspector field를 볼 수 없기 때문에 중요합니다. VRChat에서 스트림이 실패하면 debug canvas가 실패 단계를 가장 빠르게 알려줍니다.

## 지정할 것

- `Encoder`: TX 데이터를 표시하려면 sender `TSMPEncoder`.
- `Decoder`: RX 데이터를 표시하려면 receiver `TSMPDecoder`.
- `Target Text`: Unity UI `Text` component.

Encoder, decoder, 또는 둘 다 표시할 수 있습니다.

## TX fields

| Field | 의미 |
| --- | --- |
| `TX` bitrate | 추정 outgoing TSMP data rate. |
| `frame` | 현재 encoder frame index. Encoding 중 증가해야 합니다. |
| `payload` | 사용한 payload bytes / usable payload bytes. |
| `msg` | 이번 frame에 쓴 전체 payload message 수. |
| `var` | Variable state messages. |
| `rpc` | RPC messages. |
| `codec` | 선택된 codec ID. |
| `size` | Output frame size. |

## RX fields

| Field | 의미 |
| --- | --- |
| `RX` bitrate | 추정 received TSMP data rate. |
| `loss` | 추정 skipped frame percentage. |
| `valid` | 마지막 decoded frame이 usable인지. |
| `header` | Header가 valid인지. |
| `payload` | Header가 요청한 payload bytes. |
| `available` | Decoded texture에서 사용 가능한 payload bytes. |
| `msg` | Payload에서 찾은 network messages. |
| `var` | 적용된 variable values. |
| `rpc` | 수신된 RPC calls. |
| `dupFrame` | Decoder가 skip한 duplicate frames. |
| `dupRpc` | Decoder가 skip한 duplicate RPC calls. |

## 흔한 상태 읽기

| 표시 | 가능성 높은 의미 |
| --- | --- |
| `TX frame`은 증가하지만 `payload=0` | Encoder는 실행 중이지만 sync 대상으로 선택된 데이터가 없습니다. |
| `TX payload`가 capacity에 가까움 | 동기화 데이터를 줄이거나 output capacity를 늘리세요. |
| `RX valid=no` | Receiver가 현재 texture를 decode할 수 없습니다. |
| `header=no` | TSMP header가 없거나 손상되었습니다. |
| RX의 `msg=0` | Valid frame은 도착했지만 network message가 없습니다. |
| `loss`가 높음 | 전송 경로가 frame을 drop하거나 repeat하고 있습니다. |
| TX의 `rpc`는 증가하지만 RX는 증가하지 않음 | RPC frame이 손실되거나 decoder binding이 맞지 않습니다. |

## 디버깅할 때 사용 순서

1. TX frame이 증가하는지 확인합니다.
2. TX payload가 0보다 큰지 확인합니다.
3. RX header와 valid가 모두 yes인지 확인합니다.
4. RX message count가 0보다 큰지 확인합니다.
5. Receiver object가 active이고 receive interpolation이 `None`이 아닌지 확인합니다.

어떤 단계가 실패하면 다음 단계로 넘어가기 전에 그 단계를 먼저 해결하세요.
