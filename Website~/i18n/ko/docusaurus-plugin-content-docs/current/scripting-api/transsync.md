---
title: TransSync
---

# TransSync

Namespace: `K13A.TSMP`

`TransSyncAttribute`는 TSMP가 variable state message로 encode할 field를 표시합니다.

```csharp
[TransSync("example.value")]
public int syncedValue;
```

Field는 `TSMPSetup`이 discover할 수 있어야 합니다. `[TransSync]` field를 추가하거나 제거한 뒤에는 `Apply Setup`을 실행하세요.

## Properties

| Property | Type | 기본값 | 역할 |
| --- | --- | --- | --- |
| `Key` | `string` | `null` | Variable hash를 계산하는 stable identifier입니다. 생략하면 field 이름을 사용합니다. Sender와 receiver field가 매칭되려면 같은 key, network ID, value type을 사용해야 합니다. |
| `Direction` | `NetworkSyncDirection` | `SendReceive` | `Apply Setup`이 이 field를 encoder binding table, decoder binding table, 또는 둘 다에 넣을지 결정합니다. Source field와 receive/display field를 분리할 때 사용합니다. |
| `Priority` | `int` | `0` | Generated receiver binding table에 저장되는 ordering hint입니다. 현재 runtime은 이 값을 variable write 순서 변경이나 throttle에 사용하지 않습니다. |
| `SendOnChange` | `bool` | `true` | Change-based sending을 위한 metadata입니다. Attribute에는 존재하지만 현재 encoder는 encode 시점마다 bound field를 sample합니다. Runtime bandwidth limiter로 기대하면 안 됩니다. |
| `MinSendInterval` | `float` | `0` | Rate-limited sending을 위한 metadata입니다. Attribute에는 존재하지만 현재 encoder는 field별 interval을 강제하지 않습니다. Custom pacing이 필요하면 component logic 또는 packed `byte[]` flag를 사용하세요. |
| `EnabledBy` | `string` | `null` | 같은 component의 `bool` field 또는 property 이름입니다. `Apply Setup`이 binding을 만들 때 해당 member가 false면 이 field는 generated binding table에서 제외됩니다. |

`transform.packed`, `animator.bytes`, `counter.value`처럼 명확하고 stable한 key를 사용하세요. Runtime에 바뀌는 key를 사용하지 마세요.

### `Key`

`Key`는 wire 상의 field identity입니다. 서로 다른 field 이름도 같은 variable로 동기화할 수 있습니다.

```csharp
public class ChatInput : TSMPNetworkBehaviour
{
    [TransSync("chat.text", Direction = NetworkSyncDirection.SendOnly)]
    public string outgoingText;
}

public class ChatOutput : TSMPNetworkBehaviour
{
    [TransSync("chat.text", Direction = NetworkSyncDirection.ReceiveOnly)]
    public string incomingText;
}
```

두 field 모두 `chat.text`를 사용하므로 TSMP는 같은 variable hash를 부여합니다. World를 배포한 뒤에는 key를 안정적으로 유지하세요. Key를 바꾸거나 rename했다면 `Apply Setup`을 다시 실행하고 sender와 receiver가 함께 갱신됐는지 확인해야 합니다.

### `Direction`

`Direction`은 generated binding table에서 field가 어느 쪽에 사용될지 결정합니다.

| Value | 주 사용처 |
| --- | --- |
| `SendReceive` | 같은 field가 encode도 되고 decoded value도 받을 수 있는 단순 mirrored state. 같은 local field를 loopback하는 경우가 아니라면 가장 간단합니다. |
| `SendOnly` | Source/input field. Encoder는 읽을 수 있지만 decoder는 이 field에 write하지 않습니다. Local UI input, local tracking state, self-loop test에 사용하세요. |
| `ReceiveOnly` | Display/output field. Decoder는 write할 수 있지만 encoder는 읽지 않습니다. Text label, proxy avatar, remote-only state, delayed loopback display에 사용하세요. |

`instance A encoder -> stream -> instance A decoder` 같은 loopback 테스트에서는 입력 field를 `SendOnly`, 별도 출력 field를 `ReceiveOnly`로 두는 편이 안전합니다. 하나의 `SendReceive` field를 양쪽에 쓰면 지연된 과거 frame이 현재 local value를 다시 덮어쓸 수 있습니다.

### `Priority`, `SendOnChange`, `MinSendInterval`

이 옵션들은 의도한 송신 정책을 설명하는 metadata에 가깝고, 현재 runtime에서 field별 scheduler로 동작하지 않습니다.

- `Priority`는 receiver binding에 저장되며 tooling 또는 향후 scheduler에서 사용할 수 있습니다.
- `SendOnChange`는 현재 encoder가 field를 sample하는 것을 막지 않습니다.
- `MinSendInterval`은 현재 해당 field의 시간 간격을 throttle하지 않습니다.

지금 실제 bandwidth 제어가 필요하다면 `TSMPBeforeEncode()`에서 직접 값을 만들고, 변경이 없을 때는 같은 값, 빈 packet, 또는 flag가 들어간 packed `byte[]`를 쓰세요. 고빈도 데이터는 여러 scalar field보다 하나의 packed field가 보통 더 저렴하고 제어하기 쉽습니다.

### `EnabledBy`

`EnabledBy`는 setup-time optional field에 적합합니다.

```csharp
public bool includeVelocity = true;

[TransSync("velocity", EnabledBy = nameof(includeVelocity))]
public Vector3 velocity;
```

`Apply Setup`을 실행할 때 TSMP는 `includeVelocity`를 확인합니다. false면 이 field는 generated binding table에 들어가지 않습니다. 업로드된 Udon world에서는 이것을 고빈도 runtime toggle이 아니라 binding-generation option으로 취급하세요. 매 frame마다 보낼 데이터를 바꾸고 싶다면 binding은 유지하고 packed `byte[]` 안에 flag를 넣는 방식이 더 안전합니다.

## NetworkSyncDirection

| Value | 의미 |
| --- | --- |
| `SendReceive` | Field를 send/receive할 수 있습니다. |
| `SendOnly` | Field를 encode하지만 receive 적용은 하지 않습니다. |
| `ReceiveOnly` | Receive 적용은 하지만 encode하지 않습니다. |

Direction은 setup 중에 해석됩니다. 변경 후 setup을 다시 실행하세요.

## Supported value types

- `bool`
- `int`
- `float`
- `Vector2`
- `Vector3`
- `Quaternion`
- `string`
- `byte[]`
- `bool[]`
- `int[]`
- `float[]`
- `Vector2[]`
- `Vector3[]`
- `Quaternion[]`
- `string[]`

고빈도 데이터에는 packed `byte[]` field를 선호하세요.

## 바인딩 재생성

`Key`, `Direction`, value type, `EnabledBy`는 generated binding에 영향을 줍니다. 이 값을 바꾼 뒤에는 `TSMPSetup`에서 `Apply Setup`을 실행하세요. 업로드된 VRChat world에서 TSMP는 runtime reflection 대신 generated table을 사용합니다.

## Payload advice

각 field에는 message overhead가 있습니다. 몇 개의 scalar field는 괜찮지만, 반복되는 고빈도 값은 pack하는 것이 좋습니다.

선호:

```csharp
[TransSync("pose.packed")]
public byte[] poseBytes;
```

많은 개별 bone, blend shape, transform field보다 위 방식이 좋습니다.
