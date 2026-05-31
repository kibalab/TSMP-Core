---
title: Frame and Payload Algorithms
---

# Frame and payload algorithms

This page describes the runtime data flow at a high level. It is useful when reviewing encoder/decoder changes, adding tests, or debugging why a frame contains fewer messages than expected.

## Encoder frame flow

```text
EncodeNow
  -> collect active TSMPNetworkBehaviour targets
  -> begin network payload
  -> for each behaviour:
       call TSMPBeforeEncode
       write enabled TransSync fields
       cancel empty VariableState messages
  -> write queued RPC messages, if any
  -> patch message count
  -> write frame header with CRC
  -> ask codec to draw header and payload into output texture
```

Important behaviour:

- Empty `VariableState` messages are rolled back.
- RPC-only frames are allowed.
- Queued RPCs can repeat for multiple frames.
- Payload copy and FEC are not part of the current wire format.
- The selected codec supplies payload start row, capacity, symbol mode, and option bytes.

## Network payload layout

```text
+-----------------------+-----------------------------+
| Network frame header  | Message 0 | Message 1 | ... |
| 8 bytes               | variable state or RPC       |
+-----------------------+-----------------------------+
```

Network frame header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 1 | Network major version. |
| `1` | 1 | Network minor version. |
| `2` | 2 | Message count. |
| `4` | 4 | Sequence. |

Message header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 2 | Network ID. |
| `2` | 1 | Message type. |
| `3` | 1 | Flags. |
| `4` | 2 | Message sequence. |
| `6` | 2 | Body length. |

## Variable state message

```text
+----------------+------------------+------------------+
| Variable count | Variable value 0 | Variable value N |
| 2 bytes        | 7-byte header + data                 |
+----------------+------------------+------------------+
```

Variable value header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 4 | Variable hash. |
| `4` | 1 | Value type. |
| `5` | 2 | Value byte length. |

The decoder matches variable hash and network ID to a generated binding entry.

Encoder rule: if no variables were written for a behaviour, the message is canceled and the message count is not increased.

## RPC message

```text
+----------+----------------+----------------+----------------+
| RPC hash | Argument count | Argument 0     | Argument N     |
| 4 bytes  | 1 byte         | 3-byte header + data             |
+----------+----------------+----------------+----------------+
```

RPC argument header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 1 | Value type. |
| `1` | 2 | Value byte length. |

The decoder resolves the method name through payload data and dispatches the event through `TSMPBehaviour.SendCustomEvent`.

RPC events are repeated by the encoder for a small number of frames to tolerate texture frame loss.

## Header validation

Decoder validation order:

1. Buffer range.
2. Magic.
3. Header size.
4. Major version.
5. Header CRC32.

CRC is calculated over header bytes `0..51`. The stored CRC is at bytes `52..55`. A mismatch discards the frame before payload processing.

## Decode flow

```text
Update / decode tick
  -> read header area from texture
  -> validate header and CRC
  -> choose codec by codecId
  -> request payload bytes according to PayloadSize
  -> decode network frame header
  -> iterate messages
  -> apply variable values or dispatch RPC calls
  -> update diagnostics
```

The decoder should fail closed. Malformed payload data stops payload processing and records diagnostics instead of applying partial invalid state.

## Where to add tests

Add protocol tests around:

- Header CRC generation and rejection.
- Message count patching.
- Empty variable message rollback.
- RPC-only frames.
- Payload capacity boundaries.
- Duplicate frame and duplicate RPC handling.
