---
title: Glossary
---

# Glossary

Use this page when a TSMP term appears in the Inspector, debug canvas, or logs.

| Term | Meaning |
| --- | --- |
| Frame | One complete TSMP texture update. It contains a header and optional payload. |
| Header | The fixed 56-byte metadata block at the start of a frame. |
| Payload | The data body after the header. It contains network messages. |
| Codec | The component that turns bytes into pixels and pixels back into bytes. |
| Luma4 | The default TSMP codec used by the sample prefab. |
| Network ID | The ID used to route a message to a matching `TSMPNetworkBehaviour`. |
| Binding | Generated setup data that maps a network ID and field hash to a component field. |
| Variable state | A payload message containing one or more synchronized field values. |
| TSMP RPC | A payload message that dispatches a method-like event through the texture stream. |
| Frame index | The sender's increasing frame number. It helps detect duplicates and loss. |
| CRC | A checksum used to reject damaged headers. |
| Payload capacity | The number of payload bytes the current texture and codec can carry. |
| Receive interpolation | Per-component setting that decides how received values are applied. |
| Transport | The path carrying the encoded texture, such as VRChat camera output, Spout, or OBS. |

## Common debug phrases

| Phrase | Usually means |
| --- | --- |
| `Payload buffer is full` | Selected synchronized data does not fit in the current frame. |
| `Header CRC mismatch` | The transport changed header pixels or the receiver is reading the wrong region. |
| `payload=0` | Encoder is running but no selected data was written. |
| `msg=0` on RX | The decoder saw a frame but found no usable network messages. |
| `ReceiveInterpolation=None` | The component intentionally ignores received values. |
