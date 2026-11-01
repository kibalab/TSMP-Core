---
title: TSMPEncoder
---

# TSMPEncoder

Use `TSMPEncoder` on the sender side. It writes TSMP data into an output render texture.

Most users configure it through `TSMPSetup` instead of editing every field directly.

Automatic fields honor [TransSync send scheduling](../scripting-api/transsync.md#priority-sendonchange-and-minsendinterval). Unchanged values normally refresh once per second. A stationary scene can therefore stop incrementing the frame index between refreshes; this is expected. To repeat one component's state more frequently without changing every component's refresh interval, select [Send Mode: Always](./network-behaviour.md#send-mode) on that component. Minimum intervals and capacity still apply.

## What you need

- An output `RenderTexture`.
- The Luma4 handler selected through `TSMPSetup`.
- At least one enabled TSMP network component, or a queued RPC call.
- Enough payload capacity for the selected data.

## How to use it

1. Assign the output texture through `TSMPSetup`.
2. Choose Luma4 through the Codec tab.
3. Set the target frame rate.
4. Allow automatic Setup preparation to finish; `Apply Setup` is an optional manual refresh.
5. Watch the output texture while synchronized objects move.

The output texture should visibly change when payload data changes. If it stays flat or only shows a static pattern, check setup references and payload diagnostics.

## What the output should look like

A healthy TSMP output usually has:

- A structured header area.
- A changing payload area when synchronized values change.
- Empty black or unused space when the texture has more capacity than the current payload needs.

Old blocks should not remain after changing settings or payload size. If they do, make sure output clearing is enabled and the output render texture is the one being displayed.

## Payload and capacity

The encoder builds one payload per frame. Payload contains:

- Variable state messages from `[TransSync]` fields.
- RPC messages queued by `SendTransRPC`.

When automatic variables exceed capacity, higher-priority fields are included first and the rest are deferred to later attempts. **Deferred Variables** and a rate-limited warning show this condition. A field larger than a whole payload cannot be delivered until you reduce its size or increase output capacity. Invalid values and oversized RPC/manual messages can still fail an attempt.

High-bandwidth sources include:

- Full VRChat avatar pose sync.
- Humanoid pose with many bones.
- Large blend shape selections.
- Many active players.
- Many independent transform objects.

## Payload integrity

The production encoder always writes a payload CRC32 in its 56-byte header and marks the frame as protocol 0.2. This covers exactly the serialized payload length for both ordinary Unity and Udon output, with every codec; it does not reduce payload capacity or add bytes to the NetworkFrame body. A current decoder validates it before applying values or RPCs.

Older decoders can read the same frame layout but do not check the payload CRC. Legacy header-building APIs retain their 0.1 default, so custom senders using them do not gain payload protection automatically. See the [wire layout and compatibility rules](../concepts/protocol.md#payload-integrity). A checksum detects a changed frame; it does not recover it or guarantee a target publication rate.

## RPC use

From a `TSMPNetworkBehaviour`, call:

```csharp
SendTransRPC(nameof(MyMethod), RPCTarget.All);
```

The encoder includes queued RPCs in the payload for several frames so short events are less likely to be missed by the texture transport.

Targets:

| Target | Sender | Receiver |
| --- | --- | --- |
| `Local` | Runs immediately. | Not sent. |
| `Remote` | Does not run locally. | Runs after decode. |
| `All` | Runs immediately. | Runs again after decode. |

## Runtime diagnostics

Important fields and warnings:

| Diagnostic | Meaning |
| --- | --- |
| Frame index | Increases when frames are being encoded. |
| Payload bytes | Number of payload bytes written this frame. |
| Message count | Number of network messages in the payload. |
| `Payload buffer is full` | Current frame cannot fit all synchronized data. |
| `Failed to write TSMP value` | A field could not be encoded. |
| Missing codec or output texture | The encoder cannot produce a valid frame. |

## Common fixes

| Symptom | Try this |
| --- | --- |
| Frame index does not increase | Enable auto encode or call Encode Now from the Debug tab. |
| Payload is always zero | Add an enabled TSMP network component and run `Apply Setup`. |
| Payload buffer is full | Reduce synced data or increase output capacity. |
| RPC is missed | Check frame loss and keep RPC repeat frames enabled. |
| Output looks unchanged after codec change | Re-run `Apply Setup` and confirm the output render texture is cleared. |
