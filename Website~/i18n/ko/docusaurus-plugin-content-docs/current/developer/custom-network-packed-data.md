---
title: 사용자정의 Packed Data
---

# 사용자정의 packed data

Bandwidth를 직접 제어해야 한다면 `[TransSync] byte[]` field를 사용하세요. Pose data, dense transform data, blendshape selections, high-frequency values에 권장되는 방식입니다.

## Packet layout

모든 packet은 version byte로 시작하세요.

| Offset | Value |
| --- | --- |
| `0` | Packet version. |
| `1..n` | 해당 version의 data. |

Receiver는 알 수 없는 version을 잘못 읽지 않고 무시할 수 있습니다.

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

항상 다음을 검증하세요.

- `byte[]`가 null이 아님.
- Read 전에 length가 충분함.
- Version이 지원됨.
- Index 값이 receiver-side allowlist 안에 있음.
- Target component가 할당되어 있음.

Malformed packet은 적극 debug 중이 아니라면 조용히 skip하는 편이 좋습니다.

## Bandwidth rules

| Pattern | Better option |
| --- | --- |
| Many booleans | Bits를 한 byte에 pack. |
| Many normalized weights | `0..100` 또는 `0..255`를 `byte`로 저장. |
| Positions with known range | `ushort` 또는 `short`로 quantize. |
| Positions with unknown range | 필요한 값만 `Float32` 사용. |
| Repeated strings | 작은 enum/index 전송. |

## Compatibility

Packet layout 변경은 해당 component의 protocol 변경입니다. Version byte를 올리고, 오래된 sender가 사라질 때까지 old read path를 유지하세요.
