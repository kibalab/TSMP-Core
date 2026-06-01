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

| Property | Type | Default | What it does |
| --- | --- | --- | --- |
| `Key` | `string` | `null` | Stable identifier used to calculate the variable hash. If omitted, TSMP uses the field name. Sender and receiver fields must use the same key, network ID, and value type to match. |
| `Direction` | `NetworkSyncDirection` | `SendReceive` | Controls whether the setup process puts this field in the encoder binding table, the decoder binding table, or both. Use this to separate source fields from receive/display fields. |
| `Priority` | `int` | `0` | Stored in the generated receiver binding table as an ordering hint for tooling and future scheduling. The current runtime does not use it to throttle or reorder variable writes. |
| `SendOnChange` | `bool` | `true` | Metadata for change-based sending. It is accepted by the attribute but the current encoder still samples bound fields during encode. Do not rely on it as a runtime bandwidth limiter. |
| `MinSendInterval` | `float` | `0` | Metadata for rate-limited sending. It is accepted by the attribute but the current encoder does not enforce per-field intervals. Use component-level logic or packed `byte[]` flags when you need custom pacing. |
| `EnabledBy` | `string` | `null` | Name of a `bool` field or property on the same component. When `Apply Setup` builds bindings and the member is false, this field is omitted from the generated binding table. |

Use a clear, stable key such as `transform.packed`, `animator.bytes`, or `counter.value`. Do not use a key that changes at runtime.

### `Key`

`Key` is the wire identity of the field. It lets two different field names synchronize as the same variable:

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

Both fields use `chat.text`, so TSMP gives them the same variable hash. Keep this key stable after publishing a world. If you rename or change the key, run `Apply Setup` again and make sure all senders and receivers are updated together.

### `Direction`

`Direction` decides which side of the generated binding table can use the field.

| Value | Typical use |
| --- | --- |
| `SendReceive` | Simple mirrored state where the same field can be encoded and also receive decoded values. Good for one-way sender and receiver objects that are not the same local field. |
| `SendOnly` | Source/input fields. The encoder can read the field, but the decoder will not write back into this field. Use this for local UI input, local tracking state, or self-loop tests. |
| `ReceiveOnly` | Display/output fields. The decoder can write into the field, but the encoder will not read it. Use this for text labels, proxy avatars, remote-only state, or delayed loopback display. |

For a loopback test such as `instance A encoder -> stream -> instance A decoder`, prefer `SendOnly` on the input field and `ReceiveOnly` on a separate output field. If one `SendReceive` field is used for both, delayed frames can overwrite the current local value with older received data.

### `Priority`, `SendOnChange`, and `MinSendInterval`

These options describe intended send policy, but they are not a per-field scheduler in the current runtime.

- `Priority` is recorded in receiver bindings and can be used by tools or future scheduler work.
- `SendOnChange` does not currently stop the encoder from sampling the field each encode.
- `MinSendInterval` does not currently throttle that field by time.

When you need real bandwidth control today, put your own logic in `TSMPBeforeEncode()` and write either an unchanged value, an empty packet, or a packed `byte[]` with flags. For high-frequency data, one packed field is usually cheaper and easier to control than many scalar fields.

### `EnabledBy`

`EnabledBy` is best for setup-time optional fields:

```csharp
public bool includeVelocity = true;

[TransSync("velocity", EnabledBy = nameof(includeVelocity))]
public Vector3 velocity;
```

When `Apply Setup` runs, TSMP checks `includeVelocity`. If it is false, the field is not added to the generated binding table. For uploaded Udon worlds, treat this as a binding-generation option, not as a high-frequency runtime toggle. If you need to change what data is sent every frame, keep the binding present and encode that choice inside a packed `byte[]`.

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

## Binding rebuilds

`Key`, `Direction`, value type, and `EnabledBy` affect generated bindings. After changing any of them, run `Apply Setup` on `TSMPSetup`. In uploaded VRChat worlds, TSMP uses those generated tables instead of runtime reflection.

## Payload advice

Each separate field has message overhead. A few scalar fields are fine, but repeated high-frequency values should be packed.

Prefer:

```csharp
[TransSync("pose.packed")]
public byte[] poseBytes;
```

over many individual bone, blend shape, or transform fields.
