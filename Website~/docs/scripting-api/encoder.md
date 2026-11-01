---
title: TSMPEncoder API
---

# `TSMPEncoder`

`TSMPEncoder` builds a TSMP frame from synchronized behaviours, queued RPC calls, and the selected codec. It writes the result to one output `RenderTexture`.

Use this page when you need to drive encoding from code, inspect frame counters, or send a TSMP RPC.

## Component fields

| Field | Purpose |
| --- | --- |
| `output` | Render texture that receives the encoded frame. The texture size defines the final frame size. |
| `frameRate` | Maximum automatic encode rate when `autoEncode` is enabled. |
| `autoEncode` | Encodes on the component update loop. Disable it when another script calls `EncodeNow()`. |
| `clearAfterEncode` | Udon writer: resets the working frame after successful output (default `true`). Output counters remain readable. Ordinary Unity rebuilds its payload each attempt. |
| `selectedCodec` | Codec component used to convert payload bytes into pixels. Luma4 is the default codec. |
| `useBlockSymbolTexture` | Uses the codec block texture path when the selected codec supports it. |
| `networkBehaviours` | Bound behaviours that can contribute `[TransSync]` data and TSMP RPC messages. |
| `transRpcRepeatFrames` | Total successful frames carrying each queued TransRPC, including its first transmission (1-16). Failed encoding attempts do not consume a repeat. |
| `transSyncRefreshInterval` | Seconds before resending unchanged automatic TransSync fields (default 1). Zero disables refresh; per-field minimum intervals still apply. |
| `streamId` | Logical stream identifier written into the frame header. |
| `layoutId` | Logical layout identifier written into the frame header. |

`TSMPSetup` normally fills binding and codec fields. Manual editing is useful only for custom tools or test scenes.

## Diagnostics

### Luma4 GPU encoding

Luma4 uploads the header and payload as raw RGBA bytes, generates a small symbol texture on the GPU, then expands blocks into `output`. The palette, nibble order, header CRC and packet layout are unchanged. Every symbol image is redrawn, so shrinking payloads leave no old blocks.

The material is assigned automatically during Editor/build preparation, or loaded from Resources in ordinary Unity. No manual setup step is required. `useGpuLuma4` is a hidden advanced opt-out; `lastFrameUsedGpuLuma4` reports the path used by the last written frame. Missing resources, unsupported native shaders or ineligible layouts retain CPU encoding. The fast path requires dimensions divisible by block size, at least 38 blocks across and sufficient header/payload/end-marker space.

Native codec plugins opt in with `SupportsGpuLuma4Encoding`; the default is false. Udon uses this path only for the built-in Luma4 symbol mode. Other codec payload writers are unchanged. GPU resources belong to the encoder and are released on disable/destruction. Use `output` as the published image; the hidden `outputTexture` is CPU staging and is not refreshed by GPU encoding.

Small payloads remain eligible: the tested 0/32/256-byte workloads reduced CPU time as well as large payloads. The extra GPU pass is a CPU/GPU tradeoff, not a claim of lower GPU time on every device. Desktop D3D11 was measured; profile other deployment hardware before relying on these timings.

### Frame counters

| Member | Meaning |
| --- | --- |
| `EncodedObjectCount` / `encodedObjectCount` | Number of variable source behaviours encoded in the last frame. |
| `QueuedRpcCount` / `queuedRpcCount` | Number of RPC messages waiting to be encoded. |
| `PayloadBytes` / `payloadBytes` | Bytes written into the last payload. |
| `usablePayloadBytes` | Payload capacity after the frame header and codec layout are considered. |
| `deferredVariableCount` | Eligible automatic fields omitted for lack of capacity on the latest encode attempt. They remain eligible for retry. |
| `messageCount` | Variable messages plus RPC messages in the last network frame. |
| `variableMessageCount` | Number of variable state messages. |
| `rpcMessageCount` | Number of RPC messages. |
| `FrameIndex` / `frameIndex` | Monotonic frame counter written to the header. |
| `lastEncodeStage` | Udon integer stage: 0 initial, 1 preparation, 2 automatic fields, 3 packet/header, 4 texture writing, 5 output/commit, 6 completed. An error can stop at an earlier stage. |
| `LastError` / `lastError` | Last encoder error. Runtime builds also log important failures. |

After successful Udon output, `payloadBytes`, `messageCount`, `variableMessageCount` and `rpcMessageCount` survive automatic clearing so the Inspector and debug canvas can read them. They describe the latest attempt, not a permanent successful-output snapshot: `BeginFrame()`, explicit `ClearFrame()`, an empty attempt or a failed attempt resets them. Check `frameIndex` for successful output and `lastError` for failure. Clearing the writer does not erase output pixels or backing packet bytes.

## `EncodeNow()`

```csharp
public void EncodeNow()
```

Encodes one frame immediately. It is safe to call with `autoEncode` disabled.

Typical uses:

- A debug button that forces a frame.
- A capture pipeline that has its own timing.
- An editor-time preview tool.

`EncodeNow()` returns without writing a frame when there is no variable data and no queued RPC data.

Scheduling can skip an attempt when all fields are unchanged or waiting for their minimum interval. This is not an error and does not increment the frame index. `ResetFrameIndex()` also clears send snapshots so the next attempt sends initial state.

## `ResetFrameIndex()`

```csharp
public void ResetFrameIndex()
```

Resets the frame counter used in the header and diagnostics. This is useful for a clean capture or a reproducible test.

## `QueueRpc()` and `QueueRpcHash()`

```csharp
public bool QueueRpc(ushort networkId, string rpcName, params object[] arguments)
public bool QueueRpcHash(ushort networkId, uint rpcHash, params object[] arguments)
```

Queues a lower-level RPC message. Most user components should prefer `TSMPNetworkBehaviour.SendTransRPC()` because it already knows the behaviour network ID.

Arguments should use primitive TSMP value types. For high-frequency or complex data, pack the data into a `[TransSync] byte[]` field instead of sending many RPC arguments.

These lower-level methods are available in ordinary Unity without UdonSharp. `true` means the RPC was accepted into the queue, not delivered. Unsupported or null arguments, more than 255 arguments, and messages exceeding the protocol or configured frame capacity return `false`; check `lastError` for the reason.

If changed arguments or a smaller codec/output capacity make a queued RPC unsendable, the encoder discards that event, records a diagnostic and continues with later RPCs and variables. Warning logs follow the encoder's debug settings. A missing output/codec, an unusable frame capacity, or a codec write failure does not consume an otherwise valid event.

## `QueueTransRpc()`

```csharp
public bool QueueTransRpc(int networkId, uint rpcHash, string methodName)
```

Queues a TSMP RPC by network ID and method hash. This is the encoder-side entry point used by `TSMPNetworkBehaviour.SendTransRPC()`.

The queue is written into subsequent frames according to `transRpcRepeatFrames`, so a single event can survive short frame drops in the texture path.

The repeat budget decreases only after successful local output of a frame containing that queued event. An RPC queued during `TSMPBeforeEncode()` waits for the next frame with its full budget. Once the configured count is exhausted, transmission ends even if the receiver received none of those frames. Ordinary Unity applies the same enqueue validation and unsendable-event handling described above to `QueueTransRpc()`.

This is repeated transmission, not an acknowledged delivery guarantee. An event can still be lost if every frame carrying it is dropped. The Decoder suppresses repeats using Stream ID, Network ID, method hash and event ID, retaining the most recent 32 distinct events. Independent senders sharing a Decoder should use distinct `streamId` values. Restarting a sender with the same Stream ID and reused event IDs can still collide with that cache.

## Raw variable writer API

These Udon writer members are intended for advanced tools and generated code:

```csharp
public void BeginFrame()
public void ClearFrame()
public bool BeginVariableState(int networkId)
public bool WriteRawBytesVariable(uint variableHash, byte[] value)
public bool EndVariableState()
public bool BeginRpcCall(int networkId, uint rpcHash)
public bool WriteStringRpcArgument(string value)
public bool WriteInt32RpcArgument(int value)
public bool EndRpcCall()
public void CancelCurrentMessage()
```

Use this flow for a custom variable message:

```csharp
encoder.BeginFrame();
if (encoder.BeginVariableState(networkId) &&
    encoder.WriteRawBytesVariable(variableHash, payload))
{
    encoder.EndVariableState();
}
else
{
    encoder.CancelCurrentMessage();
}
```

For normal components, `[TransSync]` is simpler and less error-prone.

## Runtime rules

- The encoder should not know optional codec packages by type. It talks to the selected `TSMPCodec`.
- The output texture should be large enough for the selected codec and payload size.
- A payload overflow logs a warning and drops the over-budget message.
- RPC-only frames are valid. A frame can contain no variable messages and still carry queued RPC messages.
