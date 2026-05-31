---
title: TSMPEncoder API
---

# `TSMPEncoder`

`TSMPEncoder`는 synchronized behaviour, queued RPC, 선택된 codec으로 TSMP frame을 만들고 하나의 output `RenderTexture`에 기록합니다.

코드에서 encode를 직접 실행하거나, frame counter를 확인하거나, TSMP RPC를 보내야 할 때 이 페이지를 보세요.

## Component fields

| Field | 용도 |
| --- | --- |
| `output` | Encoded frame이 기록되는 RenderTexture. Texture size가 최종 frame size가 됩니다. |
| `frameRate` | `autoEncode`가 켜져 있을 때 자동 encode 최대 rate. |
| `autoEncode` | Component update loop에서 encode합니다. 다른 script가 `EncodeNow()`를 호출한다면 끄세요. |
| `clearAfterEncode` | 현재 frame 작성 후 사용하지 않는 pixel을 clear합니다. Codec 또는 payload size를 자주 바꿀 때 켜는 것이 좋습니다. |
| `selectedCodec` | Payload bytes를 pixel로 변환하는 codec component. 기본 codec은 Luma4입니다. |
| `useBlockSymbolTexture` | 선택 codec이 지원하면 block texture path를 사용합니다. |
| `networkBehaviours` | `[TransSync]` data와 TSMP RPC message를 만들 수 있는 bound behaviours. |
| `transRpcRepeatFrames` | Queued RPC를 추가 frame에 반복 노출하는 횟수. 손실이 있는 capture path에서는 늘립니다. |
| `streamId` | Frame header에 기록되는 logical stream ID. |
| `layoutId` | Frame header에 기록되는 logical layout ID. |

대부분의 경우 `TSMPSetup`이 binding과 codec field를 채웁니다.

## Diagnostics

| Member | 의미 |
| --- | --- |
| `EncodedObjectCount` / `encodedObjectCount` | Last frame에 encoded된 variable source behaviour 수. |
| `QueuedRpcCount` / `queuedRpcCount` | Encode 대기 중인 RPC message 수. |
| `PayloadBytes` / `payloadBytes` | Last payload에 기록된 byte 수. |
| `usablePayloadBytes` | Header와 codec layout을 고려한 payload capacity. |
| `messageCount` | Last network frame의 variable message와 RPC message 합계. |
| `variableMessageCount` | Variable state message 수. |
| `rpcMessageCount` | RPC message 수. |
| `FrameIndex` / `frameIndex` | Header에 기록되는 frame counter. |
| `lastEncodeStage` | 마지막 encode stage text. |
| `LastError` / `lastError` | 마지막 encoder error. 중요한 runtime failure는 log로도 표시됩니다. |

## `EncodeNow()`

```csharp
public void EncodeNow()
```

즉시 한 frame을 encode합니다. `autoEncode`가 꺼져 있어도 호출할 수 있습니다.

사용 예:

- Debug button에서 frame 강제 생성.
- 별도 timing을 가진 capture pipeline.
- Editor-time preview tool.

Variable data와 queued RPC가 모두 없으면 frame을 쓰지 않고 반환합니다.

## `ResetFrameIndex()`

```csharp
public void ResetFrameIndex()
```

Header와 diagnostics에 쓰는 frame counter를 reset합니다. 깨끗한 capture나 반복 가능한 test에 유용합니다.

## `QueueRpc()` and `QueueRpcHash()`

```csharp
public void QueueRpc(ushort networkId, string rpcName, params object[] arguments)
public void QueueRpcHash(ushort networkId, uint rpcHash, params object[] arguments)
```

Lower-level RPC message를 queue합니다. 일반 component에서는 behaviour network ID를 알고 있는 `TSMPNetworkBehaviour.SendTransRPC()`를 우선 사용하세요.

고빈도 또는 복잡한 data는 RPC argument를 많이 보내기보다 `[TransSync] byte[]`로 pack하는 편이 좋습니다.

## `QueueTransRpc()`

```csharp
public void QueueTransRpc(int networkId, uint rpcHash, string methodName)
```

Network ID와 method hash로 TSMP RPC를 queue합니다. `TSMPNetworkBehaviour.SendTransRPC()`가 내부적으로 사용하는 encoder-side entry point입니다.

Queue된 RPC는 `transRpcRepeatFrames`에 따라 다음 frame들에도 기록되어 짧은 frame drop을 견딜 수 있습니다.

## Raw variable writer API

Advanced tool이나 generated code용 API입니다.

```csharp
public bool BeginFrame()
public void ClearFrame()
public bool BeginVariableState(int networkId)
public bool WriteRawBytesVariable(uint variableHash, byte[] value)
public bool EndVariableState()
public bool BeginRpcCall(int networkId, uint rpcHash)
public bool WriteStringRpcArgument(string value)
public bool WriteInt32RpcArgument(int value)
public bool EndRpcCall()
public void CancelCurrentMessage()
```

Custom variable message flow:

```csharp
if (encoder.BeginFrame() &&
    encoder.BeginVariableState(networkId) &&
    encoder.WriteRawBytesVariable(variableHash, payload))
{
    encoder.EndVariableState();
}
else
{
    encoder.CancelCurrentMessage();
}
```

일반 component에서는 `[TransSync]`가 더 단순하고 안전합니다.

## Runtime rules

- Encoder는 optional codec package의 concrete type을 몰라야 합니다. 선택된 `TSMPCodec`과만 통신합니다.
- Output texture는 선택 codec과 payload size를 담을 만큼 충분히 커야 합니다.
- Payload overflow는 warning을 남기고 초과 message를 drop합니다.
- RPC-only frame은 유효합니다. Variable message 없이 queued RPC만 담을 수 있습니다.
