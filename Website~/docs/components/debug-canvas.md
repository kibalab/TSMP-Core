---
title: TSMPDebugCanvas
---

# TSMPDebugCanvas

Use `TSMPDebugCanvas` to see TSMP status inside Play mode or an uploaded VRChat world.

This is important because Inspector fields are not visible in-game. When a stream fails in VRChat, the debug canvas is usually the fastest way to identify which stage failed.

## What to assign

- `Encoder`: the sender `TSMPEncoder`, if this canvas should show TX data.
- `Decoder`: the receiver `TSMPDecoder`, if this canvas should show RX data.
- `Target Text`: a Unity UI `Text` component.

You can show encoder, decoder, or both.

## TX fields

| Field | Meaning |
| --- | --- |
| `TX` bitrate | Estimated outgoing TSMP data rate. |
| `frame` | Current encoder frame index. It should increase while encoding. |
| `payload` | Used payload bytes over usable payload bytes. |
| `msg` | Total payload messages written this frame. |
| `var` | Variable state messages. |
| `rpc` | RPC messages. |
| `codec` | Selected codec ID. |
| `size` | Output frame size. |

## RX fields

| Field | Meaning |
| --- | --- |
| `RX` bitrate | Estimated received TSMP data rate. |
| `loss` | Estimated skipped frame percentage. |
| `valid` | Whether the last decoded frame was usable. |
| `header` | Whether the header was valid. |
| `payload` | Payload bytes requested by the header. |
| `available` | Payload bytes available from the decoded texture. |
| `msg` | Network messages found in the payload. |
| `var` | Variable values applied. |
| `rpc` | RPC calls received. |
| `dupFrame` | Duplicate frames skipped by the decoder. |
| `dupRpc` | Duplicate RPC calls skipped by the decoder. |

## Reading common states

| Display | Likely meaning |
| --- | --- |
| `TX frame` increases, `payload=0` | Encoder is running, but no data is selected for sync. |
| `TX payload` near capacity | Reduce synchronized data or increase output capacity. |
| `RX valid=no` | The receiver cannot decode the current texture. |
| `header=no` | The TSMP header is missing or damaged. |
| `msg=0` on RX | A valid frame arrived, but no network messages were decoded. |
| `loss` is high | The transport path is dropping or repeating frames. |
| `rpc` increases on TX but not RX | RPC frames are being lost or decoder bindings do not match. |

## How to use it while debugging

1. Confirm TX frame increases.
2. Confirm TX payload is greater than zero.
3. Confirm RX header and valid are both yes.
4. Confirm RX message count is greater than zero.
5. Confirm receiver objects are active and receive interpolation is not `None`.

If a step fails, debug that step before moving to the next one.
