---
title: 사용자정의 Network RPC
---

# 사용자정의 network RPC

벨 소리나 애니메이션 트리거 같은 순간 이벤트에 TSMP RPC를 사용합니다. 문의 `isOpen`이나 현재 게임 단계처럼 유지되어야 하는 값은 `[TransSync]` 필드에 저장하면 프레임 손실 후 주기적 상태 갱신으로 복구할 수 있습니다.

TSMP RPC는 method call을 자동 주입하지 않습니다. `TSMPNetworkBehaviour`에서 `SendTransRPC(methodName, target)`을 호출하세요.

## Minimal RPC component

```csharp
using K13A.TSMP.Udon;
using UnityEngine;

public class BellSync : TSMPNetworkBehaviour
{
    public AudioSource bell;

    public override void Interact()
    {
        SendTransRPC(nameof(Ring), RPCTarget.All);
    }

    public void Ring()
    {
        if (bell != null)
        {
            bell.Play();
        }
    }
}
```

## `SendTransRPC()`

```csharp
public bool SendTransRPC(string methodName, RPCTarget target)
```

| Parameter | 의미 |
| --- | --- |
| `methodName` | Target behaviour에서 실행할 public method name. 가능하면 `nameof(Method)`를 사용하세요. |
| `target` | `Local`, `Remote`, `All`. |

`Remote`와 `All`에서 `true`는 로컬 인코더가 이벤트를 큐에 수락했다는 뜻입니다. 다른 클라이언트의 수신이나 실행을 보장하지 않습니다. 인코더 미할당, 큐 포화, 현재 payload 예산에 들어가지 않는 이벤트 등은 `false`를 반환할 수 있습니다.

`All`은 원격 큐 등록을 시도하기 전에 로컬 메서드를 실행합니다. `false`가 반환되어도 로컬 동작은 이미 실행됐을 수 있으므로, 전체 `All` 호출을 다시 시도하면 로컬 동작이 중복될 수 있습니다.

## `RPCTarget`

| Target | Sender behaviour | Receiver behaviour |
| --- | --- | --- |
| `Local` | Method를 즉시 실행합니다. | Queue하지 않습니다. |
| `Remote` | Local에서는 실행하지 않습니다. | Decoded receivers에 queue합니다. |
| `All` | 즉시 실행합니다. | Decoded receivers에도 queue합니다. |

Sender와 receiver를 OBS 같은 capture path로 loopback하면 `All`은 즉시 한 번, capture delay 뒤에 한 번 더 실행될 수 있습니다.

## Reliability and repeat frames

인코더는 큐의 이벤트를 `transRpcRepeatFrames`회 성공적으로 출력하며 기본값은 4회입니다. 수신기는 제한된 중복 억제 캐시 안에서 양수 이벤트 ID의 반복 실행을 막습니다. 수신 확인 응답은 없으므로 영상 손실 구간이 모든 복사본을 덮으면 이벤트는 영구적으로 누락될 수 있습니다. 주기적 상태 갱신은 RPC를 다시 생성하지 않습니다.

반복 횟수를 늘리면 개별 프레임 손실에 도움이 될 수 있지만, FIFO 큐 선두 이벤트가 더 오래 남아 뒤의 이벤트도 지연됩니다. 먼저 큐 증가와 최초 적용 지연을 확인하세요. 송신자의 현재 값으로 결국 복구되어야 하는 정보는 상태 필드로 표현합니다. 상태 갱신도 중간의 모든 전환을 보존하지는 않습니다.

## Method restrictions

- Receiving `TSMPNetworkBehaviour`에 method가 있어야 합니다.
- Udon-safe path에서는 public, argument-free method를 권장합니다.
- Event에 data가 필요하면 `[TransSync]` field로 보내세요.
- Repeat frames 가능성을 고려해 side effect는 가능하면 idempotent하게 만드세요.

## Debug checklist

| Symptom | Check |
| --- | --- |
| Local action works but delayed action does not | Encoder `queuedRpcCount`, `rpcMessageCount`, decoder `lastRpcCallCount`. |
| Event is missed | 큐 수락 여부, payload 보류, 영상 손실을 확인합니다. 모든 반복본이 유실될 수 있으므로 유지되어야 하는 값에는 상태 필드를 사용합니다. |
| Events arrive late under load | 반복 횟수를 늘리기 전에 큐 증가와 현재 반복 횟수를 확인합니다. |
| Wrong object receives event | `Apply Setup` 실행 후 `networkId` uniqueness 확인. |
