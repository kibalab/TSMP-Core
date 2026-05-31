---
title: Custom Network Behaviour
---

# Custom network behaviour

기본 sync component가 필요한 데이터를 다루지 못할 때 custom network component를 만듭니다.

Custom component는 `K13A.TSMP.Udon.TSMPNetworkBehaviour`를 상속해야 합니다. 이렇게 하면 network ID, receive interpolation 설정, TSMP RPC helper, encode/receive lifecycle hook을 사용할 수 있습니다.

## 기본 형태

```csharp
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public class ExampleCounterSync : TSMPNetworkBehaviour
{
    public int localCounter;

    [TransSync("counter.value")]
    public int syncedCounter;

    public override void TSMPBeforeEncode()
    {
        syncedCounter = localCounter;
    }

    public override void OnTSMPVariableReceived()
    {
        base.OnTSMPVariableReceived();

        if (receiveInterpolation == ReceiveInterpolationMode.None)
            return;

        localCounter = syncedCounter;
    }
}
```

`[TransSync]` key는 안정적으로 유지해야 합니다. Key를 바꾸면 variable hash가 바뀌어 기존 sender/receiver binding이 맞지 않습니다.

## Encoding lifecycle

Encoder는 `[TransSync]` field를 읽기 전에 `TSMPBeforeEncode()`를 호출합니다.

여기에서 할 일:

- Scene state를 sync field로 복사.
- 여러 값을 하나의 `byte[]`로 pack.
- `IsTSMPActive()`가 false일 때 작업 생략.
- Expensive calculation을 decoder path가 아닌 encode path에 배치.

`TSMPBeforeEncode()`는 encoded frame마다 실행될 수 있습니다. 여기서 `Apply Setup`을 호출하거나 큰 temporary array를 매번 할당하지 마세요.

## Receiving lifecycle

Decoder는 received field value를 설정하고 `lastVariableHash`를 지정한 뒤 `OnTSMPVariableReceived()`를 호출합니다.

Override할 때:

- Packed byte array를 apply합니다.
- Target interpolation value를 갱신합니다.
- `receiveInterpolation == None`이면 값을 무시합니다.
- 변경된 variable만 적용합니다.

Base의 `lastVariableHash` 처리를 사용하려면 `base.OnTSMPVariableReceived()`를 호출하세요.

## Packed byte arrays

고빈도 데이터는 `byte[]`로 pack하는 것이 좋습니다.

적합한 대상:

- Transforms.
- Bone rotations.
- Blend shape weights.
- 여러 작은 numeric fields.
- Per-player pose records.

Packed array는 payload size를 예측 가능하게 만들고 Udon bridge call을 줄입니다. 많은 scalar field보다 하나의 `byte[]` field가 보통 더 저렴합니다.

## RPC events

Event-like action에는 `SendTransRPC`를 사용합니다.

```csharp
public override void Interact()
{
    SendTransRPC(nameof(ToggleObject), RPCTarget.All);
}

public void ToggleObject()
{
    target.SetActive(!target.activeSelf);
}
```

`RPCTarget.All`은 method를 local에서 즉시 실행하고 receiver를 위해 TSMP stream에 queue합니다. Loopback test에서는 event가 지금 한 번, transport delay 뒤 한 번 더 일어납니다.

RPC는 event에 사용하고 continuous state에는 사용하지 마세요. Continuous state는 `[TransSync]` field에 넣어야 합니다.

## Setup requirements

Custom behaviour를 추가한 뒤:

1. Scene 또는 prefab에 추가합니다.
2. `Apply Setup`을 실행합니다.
3. Network ID가 할당되었는지 확인합니다.
4. `[TransSync]` field가 generated bindings에 나타나는지 확인합니다.
5. `TSMPDebugCanvas`를 켠 상태로 테스트합니다.

Component가 아무 데이터도 보내지 않으면 active state, `[TransSync]` direction, `TSMPBeforeEncode()`가 non-empty field를 만드는지 확인하세요.

## UdonSharp 호환성 체크리스트

- UdonSharp behaviour의 generic method를 피합니다.
- Runtime path에서 LINQ와 reflection을 피합니다.
- 명시적인 `for` loop를 사용합니다.
- Packed data에는 `byte[]`를 선호합니다.
- Public synced field는 단순하게 유지합니다.
- Field type 변경 후 UdonSharp compile을 테스트합니다.
