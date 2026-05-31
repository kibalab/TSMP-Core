---
title: Custom Packed Data
---

# Custom packed data

Use a `[TransSync] byte[]` field when you need precise control over bandwidth. This is the recommended pattern for pose data, dense transform data, blendshape selections, and other high-frequency values.

## Packet layout

Start every packet with a version byte:

| Offset | Value |
| --- | --- |
| `0` | Packet version. |
| `1..n` | Data for that version. |

Receivers can then ignore unknown versions instead of misreading bytes.

## Example packet

```csharp
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public class PackedPointSync : TSMPNetworkBehaviour
{
    [TransSync("point.packed")]
    public byte[] pointBytes = new byte[13];

    public Transform target;

    public override void TSMPBeforeEncode()
    {
        if (target == null || pointBytes == null || pointBytes.Length < 13)
        {
            return;
        }

        Vector3 p = target.localPosition;
        pointBytes[0] = 1;
        Binary.WriteFloat32(pointBytes, 1, p.x);
        Binary.WriteFloat32(pointBytes, 5, p.y);
        Binary.WriteFloat32(pointBytes, 9, p.z);
    }

    public override void OnTSMPVariableReceived()
    {
        if (target == null || pointBytes == null || pointBytes.Length < 13 || pointBytes[0] != 1)
        {
            return;
        }

        float x = Binary.ReadFloat32(pointBytes, 1);
        float y = Binary.ReadFloat32(pointBytes, 5);
        float z = Binary.ReadFloat32(pointBytes, 9);
        target.localPosition = new Vector3(x, y, z);
    }
}
```

## Guard rules

Always validate:

- `byte[]` is not null.
- Length is large enough before every read.
- Version is supported.
- Index values are inside receiver-side allowlists.
- Target components are assigned.

Malformed packets should be skipped quietly unless you are actively debugging.

## Bandwidth rules

| Pattern | Better option |
| --- | --- |
| Many booleans | Pack bits into one byte. |
| Many normalized weights | Use `byte` values for `0..100` or `0..255`. |
| Positions with known range | Quantize to `ushort` or `short`. |
| Positions with unknown range | Use `Float32` only for values that need it. |
| Repeated strings | Send a small enum/index instead. |

## Compatibility

Changing a packet layout is a protocol change for your component. Increment the version byte and keep the old read path until older senders are no longer used.
