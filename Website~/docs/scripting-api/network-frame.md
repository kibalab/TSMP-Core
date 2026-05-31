---
title: Network Frame API
---

# Network frame API

The TSMP network payload is a compact message stream inside the frame payload. It contains variable state messages and RPC messages.

## Network frame structure

| Part | Purpose |
| --- | --- |
| Network frame header | Version, message count, and payload-level metadata. |
| Variable state message | One `networkId` plus one or more variable values. |
| RPC message | One `networkId`, one method hash, and optional arguments. |
| Value payload | Typed bytes for primitives, strings, arrays, or packed `byte[]` data. |

Empty variable messages should not be written. If a component has no enabled fields, the builder rolls back the message.

## Variable state messages

A variable state message identifies a target behaviour by `networkId`, then contains field entries:

| Entry | Meaning |
| --- | --- |
| `variableHash` | Stable hash of the `[TransSync]` key or field name. |
| `NetworkValueType` | Encoded value type. |
| Length | Required for strings, arrays, and raw bytes. |
| Data | Value bytes. |

The decoder applies the value only when a binding matches the network ID, variable hash, direction, and target field.

## RPC messages

An RPC message identifies a target behaviour by `networkId` and a method by hash. `TSMPNetworkBehaviour.SendTransRPC()` is the normal entry point.

| Target | Behaviour |
| --- | --- |
| `RPCTarget.Local` | Execute locally only. No remote texture event is required. |
| `RPCTarget.Remote` | Queue for receivers only. |
| `RPCTarget.All` | Execute locally and queue for receivers. |

## Value types

Supported synchronized values include primitive numeric types, `bool`, `Vector2`, `Vector3`, `Quaternion`, `string`, `byte[]`, and supported arrays. Use `byte[]` for dense high-frequency data.

## Binary helper use

Protocol helpers read and write little-endian values. For custom packed payloads, keep a version byte at offset zero so receivers can handle future changes safely.
