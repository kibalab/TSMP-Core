---
title: Network Frame API
---

# Network frame API

TSMP network payload는 frame payload 안의 compact message stream입니다. Variable state messages와 RPC messages를 포함합니다.

## Network frame structure

| Part | 용도 |
| --- | --- |
| Network frame header | Version, message count, payload-level metadata. |
| Variable state message | 하나의 `networkId`와 하나 이상의 variable values. |
| RPC message | 하나의 `networkId`, 하나의 method hash, optional arguments. |
| Value payload | Primitives, strings, arrays, packed `byte[]` data의 typed bytes. |

빈 variable message는 쓰지 않아야 합니다. Component에 enabled field가 없으면 builder가 message를 rollback합니다.

## Variable state messages

Variable state message는 `networkId`로 target behaviour를 식별한 뒤 field entries를 담습니다.

| Entry | 의미 |
| --- | --- |
| `variableHash` | `[TransSync]` key 또는 field name의 stable hash. |
| `NetworkValueType` | Encoded value type. |
| Length | Strings, arrays, raw bytes에 필요. |
| Data | Value bytes. |

Decoder는 network ID, variable hash, direction, target field가 matching될 때만 value를 적용합니다.

## RPC messages

RPC message는 `networkId`로 target behaviour를, hash로 method를 식별합니다. 일반 entry point는 `TSMPNetworkBehaviour.SendTransRPC()`입니다.

| Target | Behaviour |
| --- | --- |
| `RPCTarget.Local` | Local에서만 실행합니다. |
| `RPCTarget.Remote` | Receiver에만 queue합니다. |
| `RPCTarget.All` | Local에서 실행하고 receiver에도 queue합니다. |

## Value types

Supported synchronized values에는 primitive numeric types, `bool`, `Vector2`, `Vector3`, `Quaternion`, `string`, `byte[]`, supported arrays가 포함됩니다. Dense high-frequency data에는 `byte[]`를 사용하세요.

## Binary helper use

Protocol helpers는 little-endian value를 read/write합니다. Custom packed payload는 offset zero에 version byte를 두어 future change를 안전하게 처리하세요.
