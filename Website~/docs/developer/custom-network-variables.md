---
title: Custom Network Variables
---

# Custom network variables

Use `[TransSync]` when a `TSMPNetworkBehaviour` needs to send data every frame or whenever a value changes. The encoder reads the field, writes a variable message, and the decoder applies it to the matching receiver field.

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

The key is hashed into a stable `variableHash`. Use explicit keys instead of relying on field names when the field might be renamed later.

## Supported value types

| Type group | Examples |
| --- | --- |
| Boolean and numeric | `bool`, `int`, `float` |
| Unity structs | `Vector2`, `Vector3`, `Quaternion` |
| Text | `string` |
| Packed data | `byte[]` |
| Arrays | Supported primitive and struct arrays |

Prefer `byte[]` for high-frequency values because it lets you control byte layout and avoid many small field messages.

## Direction

```csharp
[TransSync("state", Direction = NetworkSyncDirection.SendReceive)]
public int state;
```

| Direction | Meaning |
| --- | --- |
| `SendReceive` | Sender writes it and receiver applies it. |
| `SendOnly` | Sender writes it but this field is not used as a receiver target. |
| `ReceiveOnly` | Receiver applies it but this field is not written by the sender. |

## Conditional enable

```csharp
public bool sendVelocity = true;

[TransSync("velocity", EnabledBy = nameof(sendVelocity))]
public Vector3 velocity;
```

`EnabledBy` points to a boolean field on the same component. When false, the variable is skipped. If a variable message would have no fields, it is removed from the payload.

## Receive interpolation

Each `TSMPNetworkBehaviour` has `receiveInterpolation`:

| Mode | Behaviour |
| --- | --- |
| `None` | Ignore received values. |
| `Discrete` | Apply values when a frame is decoded. |
| `Continuous` | Smooth toward received values where the component implements continuous application. |

Custom components can use `GetReceiveInterpolationStep()` to drive their own smoothing.

## Setup requirement

After adding, removing, or renaming `[TransSync]` fields, run `Apply Setup`. TSMP uses generated binding tables instead of runtime reflection in uploaded worlds.
