---
title: TSMPCodec
---

# TSMPCodec

Namespace: `K13A.TSMP`

Codec handler의 base type입니다.

Codec handler는 encoder가 payload bytes를 pixels로 쓰는 방법과, decoder가 어떤 material/options로 payload bytes를 복원할지 알려줍니다.

## Public fields

| Field | Type | 용도 |
| --- | --- | --- |
| `codecId` | `ushort` | Frame header에 쓰는 stable codec ID. |
| `displayName` | `string` | Editor-facing name. |
| `codecOptionBytes` | `byte[]` | Frame header에서 복사된 decoder-side option bytes. |
| `selectedDecodeMaterial` | `Material` | Byte decode에 사용할 material. |
| `payloadStartRow` | `int` | Decode할 첫 payload row. |
| `payloadBlockCount` | `int` | Decode할 payload block 수. |
| `byteCount` | `int` | 요청된 payload byte count. |

Runtime encoder query result fields는 Udon event bridge를 위해 public입니다. `OnTSMPEncoderQuery()`와 `OnTSMPEncoderWritePayload()`가 할당합니다.

## Runtime encode methods

| Method | 용도 |
| --- | --- |
| `GetEncoderSymbolMode()` | Frame header용 symbol mode 반환. |
| `GetEncoderPayloadStartRow(width, blockSize)` | 첫 payload row 반환. |
| `GetEncoderPayloadCapacityBytes(width, height, blockSize)` | Payload byte capacity 반환. |
| `GetEncoderCodecOptionByteCount()` | Option byte count 반환, 최대 5. |
| `GetEncoderCodecOptionByte(index)` | Option byte 하나 반환. |
| `WriteEncoderPayload(...)` | Payload bytes를 encoder pixels에 씁니다. |
| `ApplyDecodeOptions()` | Header/options에서 decoder state를 적용합니다. |

VRChat 안에서 codec이 동작해야 한다면 이 methods는 UdonSharp-compatible해야 합니다.

## Helper methods

| Method | 용도 |
| --- | --- |
| `ReadCodecOptionByte(index, fallback)` | Fallback과 함께 option byte를 읽습니다. |
| `ReadCodecOptionFlag(index, fallback)` | Option을 boolean으로 읽습니다. |
| `GetEncoderActiveWidthBlocks(width, blockSize)` | Writable block width 계산. |
| `GetEncoderActiveHeightBlocks(height, blockSize)` | Writable block height 계산. |
| `WriteEncoderColorBlockAtIndex(...)` | Encoded block 하나를 color로 채웁니다. |
| `ReadEncoderBits(...)` | Payload bytes에서 arbitrary bits를 읽습니다. |

## Editor/native methods

`COMPILER_UDONSHARP` 밖에서 사용 가능:

| Method | 용도 |
| --- | --- |
| `SymbolMode` | Native symbol mode property. |
| `TryWriteFrame(...)` | Complete frame을 `Texture2D`에 씁니다. |
| `GetCodecOptionBytes()` | Native codec option byte array. |
| `DecodeMaterialCount` | Decode material 수. |
| `GetDecodeMaterial(index)` | Decode material lookup. |
| `DebugMaterialCount` | Debug material 수. |
| `GetDebugMaterial(index)` | Debug material lookup. |
| `ConfigureMaterials(context)` | Decode/debug materials 설정. |

## Udon event bridge

`TSMPCodec`은 encoder가 사용하는 public event method를 노출합니다.

| Method | 목적 |
| --- | --- |
| `OnTSMPEncoderQuery()` | Encoder query result fields를 채웁니다. |
| `OnTSMPEncoderWritePayload()` | `WriteEncoderPayload`를 호출하고 결과를 저장합니다. |

Encoder는 이 bridge를 사용해 optional codec package를 hard-code하지 않고 호출합니다.
