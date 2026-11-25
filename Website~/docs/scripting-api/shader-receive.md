---
title: TSMP.cginc
---

Use this include in a **consumer** shader targeting 3.5 or higher. It reads data already validated and published by a decoder. It does not decode an encoded video texture or implement a codec. For a working material, see [Read TSMP data in a material](../guides/shader-receive.md).

```hlsl
#pragma target 3.5
#include "Packages/com.kibalab.tsmp.core/Shaders/TSMP.cginc"
```

## Lookups and reads

All numeric arguments are unsigned 32-bit integers. The selectors write a `TSMPByteView` and return `false` if the requested data is unavailable. `TSMPTryGetFrame(out view)` and `TSMPTryGetField(networkId, hash, out view)` are shortcuts for channel 0.

| HLSL | Result |
| --- | --- |
| `bool TSMPTryGetFrame(channel, out view)` | Latest accepted [NetworkFrame payload](./network-frame.md), excluding the 56-byte wire header. RPC entries are visible as bytes, not queued events. |
| `bool TSMPTryGetField(channel, networkId, hash, out view)` | Latest value bytes for this key, without the entry header. Returns true even for a received zero-length value. |
| `uint TSMPStreamId(channel)` | Stream ID for the published frame. Check frame availability first. |
| `uint TSMPFrameIndex(channel)` | Frame number. A valid frame can have index zero. |
| `uint TSMPReadByte(view, offset)` | One byte. |
| `uint TSMPReadUInt16LE(view, offset)` | Two bytes, little-endian. |
| `uint TSMPReadUInt32LE(view, offset)` | Four bytes, little-endian. |
| `float TSMPReadFloat32LE(view, offset)` | Four bytes interpreted as an IEEE 754 Float32, not an integer converted to float. |
| `uint TSMPReadBytesLE(view, offset, count)` | One to four bytes, little-endian; zero for any other count. |

A view's `length` is in bytes; `valueType` is the wire `NetworkValueType` for a field (0 for the whole frame). Pass offsets **relative to the start of the view**. The readers handle unaligned addresses and return zero if any requested byte is out of bounds. Check the lookup and length to distinguish a valid zero from missing data. Treat `view.channel` and `view.offset` as internal addressing: reuse a view within one shader invocation, but look it up again for the next frame.

## Choosing a field {#field-key}

The key is the sender's Network ID plus the unsigned wire hash. **Copy Shader Lookup** supplies the HLSL expression for a configured binding. Without a receiver binding, compute the hash in C# with `StableHash.VariableHash(behaviour.GetType(), fieldName, transSyncKey)`. It hashes the full behaviour type name, a dot and the TransSync key (or field name when the key is empty). Do not use `Shader.PropertyToID` or store the hash in a float: it may lose low bits. Use a `uint`, such as `0xFEDCBA98u`.

## Lifetime and cost

SendOnChange omissions keep the last value; duplicate entries in one frame use the last entry. Changing streams or accepting a frame-number restart clears prior fields. Filtered, rejected and diagnostic safety-mode frames do not publish. Turning off the decoder or switching channels releases the previous data.

**Off** (default) skips publication. When enabled, each published frame uploads the field directory and runs one GPU pass that reads the decoded bytes; it does not add a GPU readback or replace CPU validation, bindings or RPC dispatch. Each channel can retain 1,024 fields and 1 MiB of field data (each field rounded up to four bytes). Exceeding either limit invalidates the channel and logs a warning; CPU decoding continues and the cache starts again on the next publishable frame.
