---
title: TSMPEncoder API
---

# `TSMPEncoder`

## Luma4 GPU 인코딩

Luma4는 헤더와 payload를 RGBA 바이트로 올린 뒤 GPU에서 작은 심볼 텍스처를 만들고 블록을 확장해 `output`에 기록합니다. 팔레트·니블 순서·CRC·패킷 구조는 바뀌지 않습니다. 심볼 이미지는 매번 다시 그려 payload가 줄어도 잔상이 남지 않습니다.

머티리얼은 Editor/빌드 준비에서 자동 지정하며 일반 Unity에서는 Resources에서 불러옵니다. 별도 설정 작업은 없습니다. 숨겨진 `useGpuLuma4`는 고급 비활성화 옵션이고 `lastFrameUsedGpuLuma4`는 마지막 출력의 경로를 나타냅니다. 리소스 누락, native 셰이더 미지원, 지원하지 않는 레이아웃에서는 CPU 경로를 유지합니다. 출력 크기는 블록 크기의 배수이고 가로 38블록 이상이며 헤더·payload·종료 마커 공간이 충분해야 합니다.

Native 코덱은 기본값이 false인 `SupportsGpuLuma4Encoding`으로 명시적으로 참여합니다. Udon에서는 기본 Luma4 심볼 모드에만 적용하며 다른 코덱의 writer는 그대로입니다. GPU 리소스는 인코더가 소유하고 비활성화·제거 시 해제합니다. 최종 영상은 `output`을 사용하세요. 숨겨진 `outputTexture`는 CPU 작업용이며 GPU 인코딩 시 갱신되지 않습니다.

측정한 0·32·256바이트에서도 CPU 비용이 줄어 작은 payload를 제외하지 않습니다. GPU 패스가 늘어나는 CPU/GPU 절충이며 모든 장치에서 GPU 비용도 감소한다는 뜻은 아닙니다. 측정 환경은 데스크톱 D3D11이며 다른 배포 장치는 별도 측정이 필요합니다.

`TSMPEncoder`는 synchronized behaviour, queued RPC, 선택된 codec으로 TSMP frame을 만들고 하나의 output `RenderTexture`에 기록합니다.

코드에서 encode를 직접 실행하거나, frame counter를 확인하거나, TSMP RPC를 보내야 할 때 이 페이지를 보세요.

## Component fields

| Field | 용도 |
| --- | --- |
| `output` | Encoded frame이 기록되는 RenderTexture. Texture size가 최종 frame size가 됩니다. |
| `frameRate` | `autoEncode`가 켜져 있을 때 자동 encode 최대 rate. |
| `autoEncode` | Component update loop에서 encode합니다. 다른 script가 `EncodeNow()`를 호출한다면 끄세요. |
| `clearAfterEncode` | Udon Writer: 출력 성공 후 작업 중인 프레임을 초기화합니다(기본 `true`). 출력 통계는 남습니다. 일반 Unity는 시도마다 payload를 다시 구성합니다. |
| `selectedCodec` | Payload bytes를 pixel로 변환하는 codec component. 기본 codec은 Luma4입니다. |
| `useBlockSymbolTexture` | 선택 codec이 지원하면 block texture path를 사용합니다. |
| `networkBehaviours` | `[TransSync]` data와 TSMP RPC message를 만들 수 있는 bound behaviours. |
| `transRpcRepeatFrames` | 대기 중인 TransRPC 하나를 담아 출력에 성공할 총 프레임 수입니다. 최초 전송을 포함한 1-16회이며, 인코딩 실패는 횟수를 소모하지 않습니다. |
| `transSyncRefreshInterval` | 변경 없는 자동 TransSync 필드를 재전송할 간격(초, 기본 1)입니다. 0이면 재전송을 끄며 필드별 최소 전송 간격은 계속 적용됩니다. |
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
| `deferredVariableCount` | 최근 인코딩 시도에서 용량 부족으로 미룬 전송 대상 필드 수입니다. 해당 필드는 이후 다시 시도합니다. |
| `messageCount` | Last network frame의 variable message와 RPC message 합계. |
| `variableMessageCount` | Variable state message 수. |
| `rpcMessageCount` | RPC message 수. |
| `FrameIndex` / `frameIndex` | Header에 기록되는 frame counter. |
| `lastEncodeStage` | Udon 정수 단계: 0 초기, 1 준비, 2 자동 필드, 3 패킷/헤더, 4 텍스처 쓰기, 5 출력/반영, 6 완료. 오류가 나면 앞 단계에서 멈출 수 있습니다. |
| `LastError` / `lastError` | 마지막 encoder error. 중요한 runtime failure는 log로도 표시됩니다. |

Udon 출력에 성공하면 자동 정리 후에도 `payloadBytes`, `messageCount`, `variableMessageCount`, `rpcMessageCount`가 남아 Inspector와 디버그 화면에서 읽을 수 있습니다. 최근 시도의 통계이며 마지막 성공을 영구 보관하는 값은 아닙니다. `BeginFrame()`, 명시적 `ClearFrame()`, 빈 시도 또는 실패 시에는 초기화됩니다. 출력 성공은 `frameIndex`, 실패 이유는 `lastError`로 확인하세요. Writer 정리는 출력 픽셀이나 패킷 버퍼 내용을 지우지 않습니다.

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

필드가 변경되지 않았거나 최소 전송 간격을 기다리는 동안에는 프레임을 생성하지 않을 수 있습니다. 오류가 아니며 프레임 번호도 증가하지 않습니다. `ResetFrameIndex()`는 송신 기준값도 초기화하여 다음 시도에서 최초 상태를 다시 보냅니다.

## `ResetFrameIndex()`

```csharp
public void ResetFrameIndex()
```

Header와 diagnostics에 쓰는 frame counter를 reset합니다. 깨끗한 capture나 반복 가능한 test에 유용합니다.

## `QueueRpc()` and `QueueRpcHash()`

```csharp
public bool QueueRpc(ushort networkId, string rpcName, params object[] arguments)
public bool QueueRpcHash(ushort networkId, uint rpcHash, params object[] arguments)
```

Lower-level RPC message를 queue합니다. 일반 component에서는 behaviour network ID를 알고 있는 `TSMPNetworkBehaviour.SendTransRPC()`를 우선 사용하세요.

고빈도 또는 복잡한 data는 RPC argument를 많이 보내기보다 `[TransSync] byte[]`로 pack하는 편이 좋습니다.

이 저수준 API는 UdonSharp가 없는 일반 Unity 환경에서 사용할 수 있습니다. 반환값 `true`는 큐 등록 성공이며 전달 성공이 아닙니다. 미지원 타입이나 null 인자, 255개를 초과하는 인자, 프로토콜 한도 또는 설정된 프레임 용량을 초과하는 메시지는 `false`를 반환합니다. 이유는 `lastError`에서 확인할 수 있습니다.

등록 후 인자가 변경되거나 코덱·출력 용량이 줄어 RPC를 보낼 수 없게 되면, 해당 이벤트만 제거하고 진단을 남긴 뒤 후속 RPC와 변수를 계속 송신합니다. 경고 로그는 Encoder의 디버그 설정을 따릅니다. 출력이나 코덱 미지정, 프레임 자체를 구성할 수 없는 용량, 코덱 쓰기 실패에서는 유효한 이벤트의 송신 횟수를 소모하지 않습니다.

## `QueueTransRpc()`

```csharp
public bool QueueTransRpc(int networkId, uint rpcHash, string methodName)
```

Network ID와 method hash로 TSMP RPC를 queue합니다. `TSMPNetworkBehaviour.SendTransRPC()`가 내부적으로 사용하는 encoder-side entry point입니다.

Queue된 RPC는 `transRpcRepeatFrames`에 따라 다음 frame들에도 기록되어 짧은 frame drop을 견딜 수 있습니다.

남은 횟수는 해당 이벤트를 담은 프레임의 로컬 출력에 성공했을 때만 차감합니다. `TSMPBeforeEncode()` 중 등록된 RPC는 횟수를 그대로 유지한 채 다음 프레임까지 대기합니다. 설정된 횟수를 소진하면 수신 측에 한 번도 도착하지 않았더라도 송신을 종료합니다. 일반 Unity의 `QueueTransRpc()`에도 위의 등록 시 검증과 송신 불가능 이벤트 처리 규칙이 적용됩니다.

이는 반복 전송이며 수신 확인을 통한 전달 보장은 아닙니다. 해당 이벤트를 담은 프레임이 모두 유실되면 RPC도 유실됩니다. Decoder는 Stream ID, Network ID, 메서드 해시, 이벤트 ID를 기준으로 최근 32개의 서로 다른 이벤트를 기억해 중복 실행을 막습니다. 하나의 Decoder에서 여러 송신자를 받는다면 각각 다른 `streamId`를 사용하세요. 송신자를 재시작한 뒤 같은 Stream ID와 이벤트 ID를 재사용하면 이전 캐시와 충돌할 수 있습니다.

## Raw variable writer API

Udon의 advanced tool이나 generated code용 Writer API입니다.

```csharp
public void BeginFrame()
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
encoder.BeginFrame();
if (encoder.BeginVariableState(networkId) &&
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
