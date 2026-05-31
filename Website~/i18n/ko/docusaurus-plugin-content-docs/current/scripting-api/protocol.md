---
title: Protocol API
---

# Protocol API

Namespace: `K13A.TSMP`

이 타입들은 test, tool, custom codec, validator, diagnostics에 유용합니다.

## FrameHeader

Constants:

| Constant | Value |
| --- | ---: |
| `Size` | `56` |
| `BytesBeforeCrc` | `52` |
| `CrcOffset` | `52` |
| `MaxCodecOptionBytes` | `5` |

Important methods:

| Method | 용도 |
| --- | --- |
| `CreateDefault()` | Default network frame header를 만듭니다. |
| `WriteTo(byte[] buffer, int offset)` | Header와 CRC를 씁니다. |
| `TryRead(...)` | Native read/validate helper. |
| `ClampDecodeSampleSize(...)` | Decode sampling size를 clamp합니다. |

`WriteTo`는 항상 header CRC를 씁니다.

## FrameHeaderReader

Validation statuses:

| Status | 의미 |
| --- | --- |
| `StatusOk` | Header가 valid입니다. |
| `StatusInvalidBuffer` | Buffer range가 invalid입니다. |
| `StatusMagicMismatch` | Magic이 TSMP와 일치하지 않습니다. |
| `StatusHeaderSizeMismatch` | Header size가 지원되지 않습니다. |
| `StatusVersionMismatch` | Major version이 지원되지 않습니다. |
| `StatusCrcMismatch` | Header CRC가 실패했습니다. |

Test와 tool에서는 error string을 parsing하지 말고 reader status code를 사용하세요.

## Protocol enums

| Type | Values |
| --- | --- |
| `SymbolMode` | `Luma4` |
| `PayloadType` | `Empty`, `NetworkFrame` |
| `NetworkMessageType` | `VariableState`, `RpcCall` |
| `NetworkValueType` | Bool, Int32, Float32, vectors, quaternion, UTF8 string, raw bytes, supported arrays. |
| `NetworkSyncDirection` | `SendReceive`, `SendOnly`, `ReceiveOnly` |
| `RPCTarget` | `Local`, `Remote`, `All` |

## Network frame helpers

| Type | 용도 |
| --- | --- |
| `NetworkFrameProtocol` | Payload offsets, sizes, constants. |
| `NetworkFrameWriter` | Network frame과 message headers를 씁니다. |
| `NetworkFrameReader` | Network frame과 message headers를 읽습니다. |
| `NetworkValueCodec` | Supported C# value types를 TSMP value types로 mapping합니다. |
| `Binary` | Little-endian integer read/write helpers. |
| `StableHash` | Stable FNV-1a hash helpers. |
| `Crc32Runtime` | Runtime CRC32 table과 compute helpers. |

## Frame helpers

| Type | 용도 |
| --- | --- |
| `FrameLayout` | Width, height, block size로 active block layout을 계산합니다. |
| `FrameCapacity` | Frame/payload capacity를 계산합니다. |
| `FrameHeaderWriter` | Header writing helpers. |

## Hashing rule

TSMP는 field key와 RPC method name에 stable hash를 사용합니다. Sender와 receiver가 안정적으로 맞아야 한다면 runtime-generated string을 key로 사용하지 마세요.
