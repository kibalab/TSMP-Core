---
title: TransSync
---

# TransSync

Namespace: `K13A.TSMP`

`TransSyncAttribute` marks fields that TSMP should encode into variable state messages.

```csharp
[TransSync("example.value")]
public int syncedValue;
```

The field must be discoverable by `TSMPSetup`. After adding or removing a `[TransSync]` field, run `Apply Setup`.

## Properties

| Property | Type | Meaning |
| --- | --- | --- |
| `Key` | `string` | Stable key used for field hashing. |
| `Direction` | `NetworkSyncDirection` | Send/receive direction. |
| `Priority` | `int` | Binding ordering hint. |
| `SendOnChange` | `bool` | Metadata flag for change-oriented send policy. |
| `MinSendInterval` | `float` | Metadata flag for throttled send policy. |
| `EnabledBy` | `string` | Field or property name that enables this sync field. |

Use a clear, stable key such as `transform.packed`, `animator.bytes`, or `counter.value`. Do not use a key that changes at runtime.

## NetworkSyncDirection

| Value | Meaning |
| --- | --- |
| `SendReceive` | Field can be sent and received. |
| `SendOnly` | Field is encoded but not applied on receive. |
| `ReceiveOnly` | Field is applied on receive but not encoded. |

Direction is resolved during setup. Re-run setup after changing it.

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

For high-frequency data, prefer packed `byte[]` fields.

## EnabledBy

`EnabledBy` points to a field or property on the same component. If that member evaluates to false, the binding is skipped.

Use it for optional fields, not for per-frame complex logic. For per-frame packet choices, pack the choices into a `byte[]` and include flags inside the packet.

## Payload advice

Each separate field has message overhead. A few scalar fields are fine, but repeated high-frequency values should be packed.

Prefer:

```csharp
[TransSync("pose.packed")]
public byte[] poseBytes;
```

over many individual bone, blend shape, or transform fields.
