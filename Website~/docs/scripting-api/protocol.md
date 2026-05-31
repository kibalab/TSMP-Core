---
title: Protocol API
---

# Protocol API

Namespace: `K13A.TSMP`

These types are useful for tests, tools, custom codecs, validators, and diagnostics.

## FrameHeader

Constants:

| Constant | Value |
| --- | ---: |
| `Size` | `56` |
| `BytesBeforeCrc` | `52` |
| `CrcOffset` | `52` |
| `MaxCodecOptionBytes` | `5` |

Important methods:

| Method | Use |
| --- | --- |
| `CreateDefault()` | Create a default network frame header. |
| `WriteTo(byte[] buffer, int offset)` | Write header and CRC. |
| `TryRead(...)` | Native read/validate helper. |
| `ClampDecodeSampleSize(...)` | Clamp decode sampling size. |

The header CRC is always written by `WriteTo`.

## FrameHeaderReader

Validation statuses:

| Status | Meaning |
| --- | --- |
| `StatusOk` | Header is valid. |
| `StatusInvalidBuffer` | Buffer range is invalid. |
| `StatusMagicMismatch` | Magic does not match TSMP. |
| `StatusHeaderSizeMismatch` | Header size is not supported. |
| `StatusVersionMismatch` | Major version is not supported. |
| `StatusCrcMismatch` | Header CRC failed. |

Use reader status codes in tests and tools instead of parsing error strings.

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

| Type | Use |
| --- | --- |
| `NetworkFrameProtocol` | Payload offsets, sizes, and constants. |
| `NetworkFrameWriter` | Writes network frame and message headers. |
| `NetworkFrameReader` | Reads network frame and message headers. |
| `NetworkValueCodec` | Maps supported C# value types to TSMP value types. |
| `Binary` | Little-endian integer read/write helpers. |
| `StableHash` | Stable FNV-1a hash helpers. |
| `Crc32Runtime` | Runtime CRC32 table and compute helpers. |

## Frame helpers

| Type | Use |
| --- | --- |
| `FrameLayout` | Calculates active block layout from width, height, and block size. |
| `FrameCapacity` | Calculates available frame/payload capacity. |
| `FrameHeaderWriter` | Header writing helpers. |

## Hashing rule

TSMP uses stable hashes for field keys and RPC method names. Do not use runtime-generated strings as keys if sender and receiver need to match reliably.
