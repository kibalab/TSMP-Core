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

| Property | Type | 의미 |
| --- | --- | --- |
| `Key` | `string` | Field hashing에 사용할 stable key. |
| `Direction` | `NetworkSyncDirection` | Send/receive direction. |
| `Priority` | `int` | Binding ordering hint. |
| `SendOnChange` | `bool` | Change-oriented send policy용 metadata flag. |
| `MinSendInterval` | `float` | Throttled send policy용 metadata flag. |
| `EnabledBy` | `string` | 이 sync field를 enable하는 field 또는 property 이름. |

`transform.packed`, `animator.bytes`, `counter.value`처럼 명확하고 stable한 key를 사용하세요. Runtime에 바뀌는 key를 사용하지 마세요.

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

## EnabledBy

`EnabledBy`는 같은 component의 field 또는 property를 가리킵니다. 그 member가 false로 평가되면 binding을 skip합니다.

Optional field에 사용하세요. Per-frame complex logic에는 적합하지 않습니다. Per-frame packet choice는 `byte[]` 안에 flag를 넣어 표현하는 편이 좋습니다.

## Payload advice

각 field에는 message overhead가 있습니다. 몇 개의 scalar field는 괜찮지만, 반복되는 고빈도 값은 pack하는 것이 좋습니다.

선호:

```csharp
[TransSync("pose.packed")]
public byte[] poseBytes;
```

많은 개별 bone, blend shape, transform field보다 위 방식이 좋습니다.
