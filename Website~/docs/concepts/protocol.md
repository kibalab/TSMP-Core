---
title: Frame Format
---

# Frame format

TSMP writes one binary frame into the encoded texture. The codec decides how bytes become pixels; the frame format decides what those bytes mean.

You usually do not need to edit the frame format. Understanding the layout helps when debugging CRC errors, payload capacity, message count, or custom codec work.

## Whole frame

```text
+----------------------+-------------------------------+----------------------+
| Frame header         | Frame body / payload          | Unused texture area  |
| 56 bytes             | PayloadSize bytes             | codec/padding space  |
+----------------------+-------------------------------+----------------------+
```

- The header tells the receiver how to read the frame.
- The body contains synchronized values and RPC calls.
- Any remaining texture capacity is ignored by the decoder.
- Unused texture area should be cleared or ignored so old data cannot be mistaken for current payload.

## Frame header

```text
byte 0                                                        byte 55
+--------+---------+---------+---------+---------+----------+---------+
| Magic  | Version | Layout  | Stream  | Codec   | Payload  | CRC32   |
| 0..3   | 4..11   | 12..19  | 20..31  | 32..39  | 40..51   | 52..55  |
+--------+---------+---------+---------+---------+----------+---------+
```

Important header fields:

| Offset | Size | Meaning |
| --- | ---: | --- |
| `0` | 4 | Magic value identifying TSMP. |
| `4` | 2 | Protocol major/minor version. |
| `7` | 1 | Symbol mode used by the codec. |
| `8` | 2 | Header size. Current value is `56`. |
| `10` | 2 | Frame flags; bit 0 (`FrameFlags.PayloadCrc32`) announces payload CRC32. |
| `12` | 8 | Block and active layout information. |
| `20` | 4 | Stream ID. |
| `24` | 4 | Frame index. |
| `28` | 4 | Timestamp in milliseconds. |
| `32` | 8 | Codec ID and codec options. |
| `40` | 2 | Payload type. |
| `42` | 2 | Payload size in bytes. |
| `44` | 4 | Payload CRC32 in protected frames; zero in legacy headers. |
| `48` | 2 | Reserved; written as zero. |
| `50` | 1 | Decode sample size. |
| `51` | 1 | Quantization mode. |
| `52` | 4 | Header CRC32. |

The CRC is calculated over bytes `0..51` while the CRC field is zero. If the receiver calculates a different CRC, it discards the frame and logs a warning.

## Payload integrity {#payload-integrity}

Production `TSMPEncoder` frames use protocol **0.2** and always set flag bit 0. Bytes `44..47` hold little-endian CRC32/IEEE of exactly the `PayloadSize` payload bytes. The existing header CRC also covers the flag and payload CRC field. The 56-byte header, payload capacity and NetworkFrame message layout are unchanged; no checksum bytes are appended to the body.

When a frame proceeds to payload processing, the decoder checks an announced payload CRC after readback and before that frame refreshes prediction or applies variables/RPCs. A mismatch discards the frame before application. This applies to sequential, predicted, combined-output and two-slot paths, including payload-readback-only and parse-only modes. A protected frame's decoded length must match its header `PayloadSize`, including when manual layout is used.

Legacy header APIs keep the **0.1** default with no payload CRC flag and zero bytes `44..49`. The decoder accepts these frames by default for compatibility. Enable `requirePayloadCrc32` to reject senders without a payload checksum. Header-only mode does not read or verify the body. Duplicate or older frames skipped before payload processing are not payload-verified either; the predicted path may refresh only header metadata from their CRC-valid header, without accepting or applying their body. `lastPayloadCrcPresent` reports the flag and `lastPayloadCrcVerified` reports a completed successful payload check.

## Frame body

For normal synchronization, the payload type is a TSMP network frame.

```text
+-----------------------+---------------------------------------------+
| Network frame header  | Message 0 | Message 1 | ... | Message N     |
| 8 bytes               | variable state or RPC call                  |
+-----------------------+---------------------------------------------+
```

Network frame header:

| Offset | Size | Meaning |
| --- | ---: | --- |
| `0` | 1 | Network payload major version. |
| `1` | 1 | Network payload minor version. |
| `2` | 2 | Message count. |
| `4` | 4 | Network sequence. |

Each message starts with an 8-byte message header:

```text
+------------+------+-------+----------+-------------+
| Network ID | Type | Flags | Sequence | Body length |
| 2 bytes    | 1    | 1     | 2 bytes  | 2 bytes     |
+------------+------+-------+----------+-------------+
```

Message bodies are either:

- Variable state: values from `TransSync` fields.
- RPC call: a method hash plus encoded arguments queued by `SendTransRPC`.

## Variable state body

Variable state messages contain one or more field values for one network behaviour.

```text
+----------------+------------------+------------------+
| Variable count | Variable value 0 | Variable value N |
| 2 bytes        | 7-byte header + data                 |
+----------------+------------------+------------------+
```

Each value stores:

- Stable variable hash.
- Value type.
- Byte length.
- Encoded value bytes.

Empty variable state messages are not written. If a behaviour has no enabled fields for the current frame, the encoder rolls that message back.

## RPC body

RPC messages contain:

- Stable method hash.
- Argument count.
- Encoded argument values.

RPC-only frames are valid. This allows a scene to send a short event even when no variable state changed that frame.

## What to check when debugging

- If the frame index changes, the sender is producing frames.
- If payload size is zero, no synchronized data was written.
- If CRC fails, the texture path is changing pixels or reading the wrong region.
- If message count is lower than expected, setup bindings or payload capacity need attention.
- If TX has RPC messages but RX does not, check frame loss and duplicate-frame handling.

## Compatibility notes

An older decoder can read the unchanged 56-byte layout of a protected 0.2 frame but ignores its payload checksum. Pair a current encoder with a current decoder to use the check. Legacy frames accepted by a current decoder remain unprotected at the payload level. CRC detection does not establish live transport rate, avatar performance, retransmission or restart/session behavior.

Payload copy and FEC are not part of the current wire format. Current frames carry one payload body.
