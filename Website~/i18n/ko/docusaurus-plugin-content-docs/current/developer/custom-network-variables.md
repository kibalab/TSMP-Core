---
title: 사용자정의 Network Variables
---

# 사용자정의 network variables

`TSMPNetworkBehaviour`가 매 frame 또는 값 변경 시 data를 보내야 한다면 `[TransSync]`를 사용합니다. Encoder가 field를 읽어 variable message를 만들고, decoder가 matching receiver field에 적용합니다.

## Minimal component

```csharp
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public class DoorStateSync : TSMPNetworkBehaviour
{
    [TransSync("door.open")]
    public bool isOpen;

    public override void TSMPBeforeEncode()
    {
        isOpen = transform.localEulerAngles.y > 45f;
    }

    public override void OnTSMPVariableReceived()
    {
        transform.localRotation = Quaternion.Euler(0f, isOpen ? 90f : 0f, 0f);
    }
}
```

## `[TransSync]` keys

```csharp
[TransSync("health.current")]
public int health;
```

Key는 stable `variableHash`로 변환됩니다. Field name이 나중에 바뀔 수 있다면 field name에 의존하지 말고 explicit key를 사용하세요.

## Supported value types

| Type group | Examples |
| --- | --- |
| Boolean and numeric | `bool`, `int`, `float` |
| Unity structs | `Vector2`, `Vector3`, `Quaternion` |
| Text | `string` |
| Packed data | `byte[]` |
| Arrays | Supported primitive and struct arrays |

고빈도 값은 많은 작은 field message보다 `byte[]`로 직접 layout을 제어하는 편이 좋습니다.

## Direction

```csharp
[TransSync("state", Direction = NetworkSyncDirection.SendReceive)]
public int state;
```

| Direction | 의미 |
| --- | --- |
| `SendReceive` | Sender가 쓰고 receiver가 적용합니다. |
| `SendOnly` | Sender가 쓰지만 receiver target으로 사용하지 않습니다. |
| `ReceiveOnly` | Receiver가 적용하지만 sender field로 쓰지 않습니다. |

## Conditional enable

```csharp
public bool sendVelocity = true;

[TransSync("velocity", EnabledBy = nameof(sendVelocity))]
public Vector3 velocity;
```

`EnabledBy`는 같은 component의 boolean field를 가리킵니다. false면 variable을 skip합니다. Variable message가 field를 하나도 갖지 않게 되면 payload에서 제거됩니다.

## Receive interpolation

각 `TSMPNetworkBehaviour`는 `receiveInterpolation`을 가집니다.

| Mode | Behaviour |
| --- | --- |
| `None` | Received values를 무시합니다. |
| `Discrete` | Frame decode 시점에 값을 적용합니다. |
| `Continuous` | Component가 continuous application을 구현한 경우 목표값으로 smooth합니다. |

Custom component는 `GetReceiveInterpolationStep()`으로 자체 smoothing을 구동할 수 있습니다.

## Setup requirement

`[TransSync]` field를 추가, 제거, rename한 뒤에는 `Apply Setup`을 실행하세요. TSMP는 uploaded world에서 runtime reflection 대신 generated binding table을 사용합니다.
