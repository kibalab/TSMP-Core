---
title: TSMPEncoder
---

# TSMPEncoder

Use `TSMPEncoder` on the sender side. It writes TSMP data into an output render texture.

Most users configure it through `TSMPSetup` instead of editing every field directly.

## What you need

- An output `RenderTexture`.
- The Luma4 handler selected through `TSMPSetup`.
- At least one enabled TSMP network component, or a queued RPC call.
- Enough payload capacity for the selected data.

## How to use it

1. Assign the output texture through `TSMPSetup`.
2. Choose Luma4 through the Codec tab.
3. Set the target frame rate.
4. Click `Apply Setup`.
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

If the payload is too large, the encoder logs `Payload buffer is full` and cannot include everything. Reduce synchronized data first, then increase texture capacity if needed.

High-bandwidth sources include:

- Full VRChat avatar pose sync.
- Humanoid pose with many bones.
- Large blend shape selections.
- Many active players.
- Many independent transform objects.

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
