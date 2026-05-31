---
title: Custom Codec
---

# Custom codec

기본 Luma4 텍스처 표현이 전송 경로에 맞지 않을 때 codec을 만듭니다.

Codec contract는 단순합니다.

```text
Core가 header bytes와 payload bytes를 제공합니다.
Codec은 그 bytes를 pixels로 씁니다.
Decoder는 codec을 사용해 같은 bytes를 복원합니다.
```

Shader 수준의 자세한 구현은 [Codec implementation guide](./codec-implementation.md)를 참고하세요.

## Core shader includes

Custom decode shader는 보통 Core의 두 cginc 파일을 사용합니다.

| Include | 용도 | API |
| --- | --- | --- |
| `TSMPDecodeCommon.cginc` | Decode 공용 uniforms, `appdata`/`v2f`, vertex function, block sampling helpers, luma sampling, YCoCg conversion helpers. | [Scripting API: `TSMPDecodeCommon.cginc`](../scripting-api/decode-common.md) |
| `TSMPDecodeByteOutput.cginc` | Byte-indexed `DecodeByte(index)` 함수를 decoder가 readback하는 RGBA byte output texture로 변환. | [Scripting API: `TSMPDecodeByteOutput.cginc`](../scripting-api/decode-byte-output.md) |

Shader가 shared `v2f`, `_MainTex`, `_BlockSize`, `_SampleSize`, `_StartBlock`, `_ByteCount`, `_ActiveWidthBlocks`, `_SourceWidth`, `_SourceHeight`, `_OutputWidth`, `_OutputHeight`, `_FlipY` contract를 사용한다면 먼저 `TSMPDecodeCommon.cginc`를 include하세요. Codec의 byte recovery function을 구현한 뒤 `TSMPDecodeByteOutput.cginc`를 include하면 됩니다.

## Package shape

권장 package layout:

```text
com.example.tsmp.codec.mycodec/
  package.json
  Runtime/
    Scripts/
    Shaders/
    Materials/
    Prefabs/
  Samples/
```

Package는 Core에 의존해야 합니다.

```json
"com.kibalab.tsmp.core": "1.0.0"
```

VRChat 사용자용 package라면 VPM dependency metadata도 함께 넣으세요.

## Runtime class

`TSMPCodec`을 상속하는 component를 만듭니다.

```csharp
using K13A.TSMP;
using UnityEngine;

public sealed class TSMPCodecMyCodec : TSMPCodec
{
    private const int SymbolModeMyCodec = 128;

    public Material byteDecodeMaterial;

    public override int GetEncoderSymbolMode()
    {
        return SymbolModeMyCodec;
    }

    public override int GetEncoderPayloadStartRow(int width, int blockSize)
    {
        return 5;
    }

    public override int GetEncoderPayloadCapacityBytes(int width, int height, int blockSize)
    {
        return 0;
    }

    public override bool WriteEncoderPayload(
        Color32[] pixels,
        int width,
        int height,
        int blockSize,
        byte[] payloadBytes,
        int payloadByteCount)
    {
        return false;
    }

    public override void ApplyDecodeOptions()
    {
        selectedDecodeMaterial = byteDecodeMaterial;
    }

#if !COMPILER_UDONSHARP
    public override int SymbolMode => SymbolModeMyCodec;
#endif
}
```

이 skeleton은 일부러 완성되어 있지 않습니다. 실제 codec은 payload bytes를 쓰고, 그와 일치하는 decode shader/material behaviour를 제공해야 합니다.

## Required encoder methods

| Method | Requirement |
| --- | --- |
| `GetEncoderSymbolMode()` | Frame header에 쓸 symbol mode를 반환합니다. |
| `GetEncoderPayloadStartRow(...)` | Payload block이 시작되는 위치를 Core에 알려줍니다. |
| `GetEncoderPayloadCapacityBytes(...)` | 담을 수 있는 payload byte 수를 Core에 알려줍니다. |
| `WriteEncoderPayload(...)` | 제공된 pixel buffer에 payload bytes를 씁니다. |

UdonSharp 호환성을 위해 logic은 단순하게 유지하고 unsupported C# constructs를 피하세요.

Package용 stable symbol mode를 고르고 다른 installed codecs와 충돌하지 않게 하세요. Core encoder/decoder는 이 값을 frame header로 전달하고, 그 의미는 codec package가 소유합니다.

## Codec options

Codec은 frame header에 최대 5개의 option byte를 노출할 수 있습니다.

Override:

```csharp
public override int GetEncoderCodecOptionByteCount()
public override int GetEncoderCodecOptionByte(int index)
```

Decoder는 `ApplyDecodeOptions()`가 호출되기 전에 이 bytes를 `codecOptionBytes`로 받습니다.

Option byte는 frame decode에 필요한 값에만 사용하세요. Editor-only display data에는 사용하지 않는 것이 좋습니다.

## Decoder materials

Native/editor decode support에는 non-Udon path 안에서 다음을 override합니다.

```csharp
public override int DecodeMaterialCount => 1;
public override Material GetDecodeMaterial(int index) => byteDecodeMaterial;
public override void ConfigureMaterials(CodecMaterialContext context)
```

Runtime decode에서는 `ApplyDecodeOptions()`가 다음 값을 설정해야 합니다.

- `selectedDecodeMaterial`
- `payloadStartRow`
- `payloadBlockCount`
- Shader에 필요한 codec-specific state

## Native editor writing

Editor/native C# path에서 frame을 써야 하는 codec은 다음을 구현합니다.

```csharp
public override bool TryWriteFrame(
    Texture2D texture,
    int blockSize,
    byte[] headerBytes,
    byte[] payloadBytes,
    out string error)
```

UdonSharp에서 사용할 수 없는 API를 쓰면 `#if !COMPILER_UDONSHARP` 안에 두세요.

## Registration

`TSMPSetup`에 나타나려면:

1. Codec component를 prefab에 넣습니다.
2. Stable `codecId`를 지정합니다.
3. 읽기 쉬운 `displayName`을 지정합니다.
4. `package.json`에 package metadata를 넣습니다.
5. Prefab을 참조하는 `TSMPCodecCatalog`를 추가하거나 생성합니다.
6. `TSMPSetup`에서 codecs를 refresh합니다.

Encoder가 codec class를 직접 참조하면 안 됩니다. 항상 `TSMPCodec` API를 통해 discover/call되어야 합니다.

## Validation checklist

- Encoder payload capacity가 맞습니다.
- Header와 payload region이 겹치지 않습니다.
- Codec option bytes가 decoder state로 round-trip됩니다.
- Output unused region이 clear됩니다.
- Decoder가 corrupted header를 거부합니다.
- Unity Play mode와 UdonSharp runtime에서 codec이 동작합니다.

## 언제 custom codec을 작성할까

Default Luma4 path가 전송 요구사항을 만족하지 못할 때만 custom codec을 작성하세요. 문제가 setup, capacity, OBS filtering이라면 새 codec으로 해결되지 않습니다.
