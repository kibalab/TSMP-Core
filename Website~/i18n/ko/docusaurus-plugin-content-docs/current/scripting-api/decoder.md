---
title: TSMPDecoder API
---

# `TSMPDecoder`

`TSMPDecoder`는 TSMP texture를 읽고 header를 검증한 뒤, 선택된 codec으로 payload bytes를 복구하고 variable/RPC message를 bound behaviour에 적용합니다.

Custom receiver wiring, diagnostics 확인, frame이 무시된 이유를 debug할 때 사용하세요.

## Component fields

| Field | 용도 |
| --- | --- |
| `sourceTexture` | Encoded TSMP frame이 들어있는 texture. |
| `payloadByteTexture` | Codec shader가 payload bytes를 복구할 때 사용하는 render texture. |
| `codecHandlers` | Decoder가 사용할 수 있는 codec components. Header의 `codecId`가 handler를 선택합니다. |
| `applyEveryFrame` | Component update loop에서 decode합니다. 다른 script가 `DecodeNow()`를 호출한다면 끄세요. |
| `skipDuplicateFrames` | Header frame index가 이미 처리된 frame이면 무시합니다. |
| `flipY` | Capture path가 image를 뒤집는 경우 texture sampling을 반전합니다. |
| `useHeaderPayloadLayout` | Header payload metadata로 readback size를 결정합니다. 일반적으로 켜둡니다. |
| `payloadBytesOverride` | Test path용 manual payload byte count. |
| `decodeSafetyMode` | Malformed 또는 partial frame에 대한 추가 guard. |

`TSMPSetup`이 보통 source texture, byte texture, codec handlers, binding arrays를 할당합니다.

## Header validation

Decoder는 다음 경우 payload decode 전에 frame을 버립니다.

- Magic bytes가 TSMP가 아님.
- Header size가 지원되지 않음.
- Major protocol version이 지원되지 않음.
- Header CRC 불일치.
- Payload size가 texture layout capacity를 초과.

CRC failure는 warning log로 표시됩니다. 손상된 capture frame이 잘못 적용되는 것을 막기 위한 동작입니다.

## `DecodeNow()`

```csharp
public void DecodeNow()
```

즉시 한 frame을 decode합니다. `applyEveryFrame`이 꺼져 있어도 호출할 수 있습니다.

실행 순서:

1. Header pixels를 읽습니다.
2. Magic, version, payload layout, CRC를 검증합니다.
3. `codecId`로 codec handler를 선택합니다.
4. Payload bytes를 decode합니다.
5. Network messages를 parse합니다.
6. Variables를 적용하고 RPC를 dispatch합니다.

## `ResetDecodeDiagnostics()`

```csharp
public void ResetDecodeDiagnostics()
```

Counters와 last-error fields를 초기화합니다. 반복 가능한 test나 scene wiring 변경 후 사용하세요.

## Diagnostics

| Member | 의미 |
| --- | --- |
| `lastFrameValid` | Last frame이 모든 decode stage를 통과했는지. |
| `lastHeaderValid` | Payload decode 전에 header가 valid였는지. |
| `lastError` | 마지막 decoder error 또는 warning text. |
| `lastNetworkMessageCount` | Decoded network frame의 message 수. |
| `lastAppliedVariableCount` | Behaviour에 적용된 variable 수. |
| `lastRpcCallCount` | Dispatch된 RPC message 수. |
| `lastCodecId` | Last valid header에서 읽은 codec ID. |
| `lastFrameIndex` | Header frame index. |
| `lastPayloadBytes` | Decoder가 사용한 payload byte count. |

## Binding application

Decoder는 `TSMPSetup`이 만든 binding arrays를 사용합니다. 각 binding은 다음을 연결합니다.

- `networkId`
- `variableHash`
- value type
- target behaviour
- target field name
- sync direction

Variable message가 도착하면 matching binding에만 적용합니다. RPC message는 matching `TSMPNetworkBehaviour`로 dispatch합니다.

## Runtime rules

- Header가 valid라도 알 수 없는 codec ID면 frame을 무시합니다.
- Target behaviour의 `ReceiveInterpolation.None`은 received values를 무시합니다.
- RPC는 event 성격입니다. Transport가 frame을 drop한다면 encoder repeat frames를 늘리세요.
- Uploaded world에서는 inspector field를 볼 수 없기 때문에 중요한 runtime failure는 log로 표시됩니다.
