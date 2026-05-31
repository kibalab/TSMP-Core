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
| `clearAfterEncode` | Clears unused frame pixels after writing the current frame. Enable this when changing codecs or payload size frequently. |
| `selectedCodec` | Codec component used to convert payload bytes into pixels. Luma4 is the default codec. |
| `useBlockSymbolTexture` | Uses the codec block texture path when the selected codec supports it. |
| `networkBehaviours` | Bound behaviours that can contribute `[TransSync]` data and TSMP RPC messages. |
| `transRpcRepeatFrames` | Number of additional frames that keep a queued RPC visible. Increase this for lossy capture paths. |
| `streamId` | Logical stream identifier written into the frame header. |
| `layoutId` | Logical layout identifier written into the frame header. |

`TSMPSetup` normally fills binding and codec fields. Manual editing is useful only for custom tools or test scenes.

## Diagnostics

| Member | Meaning |
| --- | --- |
| `EncodedObjectCount` / `encodedObjectCount` | Number of variable source behaviours encoded in the last frame. |
| `QueuedRpcCount` / `queuedRpcCount` | Number of RPC messages waiting to be encoded. |
| `PayloadBytes` / `payloadBytes` | Bytes written into the last payload. |
| `usablePayloadBytes` | Payload capacity after the frame header and codec layout are considered. |
| `messageCount` | Variable messages plus RPC messages in the last network frame. |
| `variableMessageCount` | Number of variable state messages. |
| `rpcMessageCount` | Number of RPC messages. |
| `FrameIndex` / `frameIndex` | Monotonic frame counter written to the header. |
| `lastEncodeStage` | Short text identifier for the last encode stage. |
| `LastError` / `lastError` | Last encoder error. Runtime builds also log important failures. |

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

## `ResetFrameIndex()`

```csharp
public void ResetFrameIndex()
```

Resets the frame counter used in the header and diagnostics. This is useful for a clean capture or a reproducible test.

## `QueueRpc()` and `QueueRpcHash()`

```csharp
public void QueueRpc(ushort networkId, string rpcName, params object[] arguments)
public void QueueRpcHash(ushort networkId, uint rpcHash, params object[] arguments)
```

Queues a lower-level RPC message. Most user components should prefer `TSMPNetworkBehaviour.SendTransRPC()` because it already knows the behaviour network ID.

Arguments should use primitive TSMP value types. For high-frequency or complex data, pack the data into a `[TransSync] byte[]` field instead of sending many RPC arguments.

## `QueueTransRpc()`

```csharp
public void QueueTransRpc(int networkId, uint rpcHash, string methodName)
```

Queues a TSMP RPC by network ID and method hash. This is the encoder-side entry point used by `TSMPNetworkBehaviour.SendTransRPC()`.

The queue is written into subsequent frames according to `transRpcRepeatFrames`, so a single event can survive short frame drops in the texture path.

## Raw variable writer API

These members are intended for advanced tools and generated code:

```csharp
public bool BeginFrame()
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
if (encoder.BeginFrame() &&
    encoder.BeginVariableState(networkId) &&
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
