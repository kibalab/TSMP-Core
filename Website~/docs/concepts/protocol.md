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
| `12` | 8 | Block and active layout information. |
| `20` | 4 | Stream ID. |
| `24` | 4 | Frame index. |
| `28` | 4 | Timestamp in milliseconds. |
| `32` | 8 | Codec ID and codec options. |
| `40` | 2 | Payload type. |
| `42` | 2 | Payload size in bytes. |
| `44` | 6 | Reserved payload bytes. |
| `50` | 1 | Decode sample size. |
| `51` | 1 | Quantization mode. |
| `52` | 4 | Header CRC32. |

The CRC is calculated over bytes `0..51` while the CRC field is zero. If the receiver calculates a different CRC, it discards the frame and logs a warning.

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

The header size remains fixed at 56 bytes. Reserved bytes are kept for future compatibility and should be written as zero by current encoders.

Payload copy and FEC are not part of the current wire format. Current frames carry one payload body.
