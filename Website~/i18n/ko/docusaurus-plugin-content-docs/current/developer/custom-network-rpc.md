---
title: 사용자정의 Network RPC
---

# 사용자정의 network RPC

Texture stream을 통해 event를 보내야 한다면 TSMP RPC를 사용합니다. Delayed interaction, toggle, trigger, one-shot state transition에 적합합니다.

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
public void SendTransRPC(string methodName, RPCTarget target)
```

| Parameter | 의미 |
| --- | --- |
| `methodName` | Target behaviour에서 실행할 public method name. 가능하면 `nameof(Method)`를 사용하세요. |
| `target` | `Local`, `Remote`, `All`. |

## `RPCTarget`

| Target | Sender behaviour | Receiver behaviour |
| --- | --- | --- |
| `Local` | Method를 즉시 실행합니다. | Queue하지 않습니다. |
| `Remote` | Local에서는 실행하지 않습니다. | Decoded receivers에 queue합니다. |
| `All` | 즉시 실행합니다. | Decoded receivers에도 queue합니다. |

Sender와 receiver를 OBS 같은 capture path로 loopback하면 `All`은 즉시 한 번, capture delay 뒤에 한 번 더 실행될 수 있습니다.

## Reliability and repeat frames

RPC는 encoder의 `transRpcRepeatFrames` 동안 frame에 노출됩니다. 다음 경우 값을 늘리세요.

- Capture path가 frame을 drop함.
- OBS나 Spout frame pacing이 불규칙함.
- Interaction이 씹힘.

빠르게 반복되는 event에는 너무 높은 repeat count를 쓰지 마세요. Receiver가 같은 event를 예상보다 오래 볼 수 있습니다.

## Method restrictions

- Receiving `TSMPNetworkBehaviour`에 method가 있어야 합니다.
- Udon-safe path에서는 public, argument-free method를 권장합니다.
- Event에 data가 필요하면 `[TransSync]` field로 보내세요.
- Repeat frames 가능성을 고려해 side effect는 가능하면 idempotent하게 만드세요.

## Debug checklist

| Symptom | Check |
| --- | --- |
| Local action works but delayed action does not | Encoder `queuedRpcCount`, `rpcMessageCount`, decoder `lastRpcCallCount`. |
| Event is missed | `transRpcRepeatFrames`를 늘리고 debug canvas에서 frame loss 확인. |
| Wrong object receives event | `Apply Setup` 실행 후 `networkId` uniqueness 확인. |
