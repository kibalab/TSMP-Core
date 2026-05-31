---
title: Developer Overview
---

# Developer overview

This section is for developers extending TSMP.

Start with Getting Started if you only need to place the prefab, synchronize built-in components, or debug a scene. Use this section when you need to write code against TSMP.

## What you can extend

| Extension | Use it when | Start here |
| --- | --- | --- |
| Custom synchronized component | Built-in sync components do not cover your data. | [Custom network behaviour](./custom-network-behaviour.md) |
| Field synchronization | You need TSMP to encode a public field. | [Scripting API: TransSync](../scripting-api/transsync.md) |
| TSMP RPC event | You need delayed event delivery through the texture stream. | [Scripting API: TSMPNetworkBehaviour](../scripting-api/network-behaviour.md) |
| Codec package | You need a different byte-to-pixel representation. | [Custom codec](./custom-codec.md) |
| Codec shader | You need dedicated encode/decode shader logic. | [Codec implementation guide](./codec-implementation.md) |
| Protocol tooling | You need tests, validators, or debug tools. | [Frame algorithms](./frame-algorithms.md) |

For member-level API details, see the separate [Scripting API](../scripting-api/index.md) section.

## Detailed paths

| Goal | Read |
| --- | --- |
| Add synchronized variables | [Custom network variables](./custom-network-variables.md) |
| Add delayed texture-stream events | [Custom network RPC](./custom-network-rpc.md) |
| Pack dense data into one field | [Custom packed data](./custom-network-packed-data.md) |
| Build a codec package | [Codec package definition](./codec-package-definition.md) |
| Write codec shader passes | [Codec shaders](./codec-shaders.md) |
| Validate byte recovery and runtime support | [Codec validation](./codec-validation.md) |

## Compatibility goals

TSMP code is written to run in two environments:

- Unity C# and editor runtime.
- UdonSharp compiled runtime.

The same component should usually work in both environments. Avoid APIs that UdonSharp cannot compile, and prefer simple fields, arrays, and explicit methods over reflection-heavy runtime logic.

## Design rules for extensions

- Keep scene-facing components explicit and easy to inspect.
- Use `[TransSync]` fields for data, not side effects.
- Pack high-frequency values into `byte[]` to reduce Udon bridge cost.
- Let `TSMPSetup` rebuild bindings after scene structure changes.
- Do not hard-code optional codec packages in Core or encoder code.
- Keep custom codecs behind the `TSMPCodec` API.
- Treat the texture path as unreliable and validate with debug counters.

## Namespace layout

| Namespace | Purpose |
| --- | --- |
| `K13A.TSMP` | Core runtime, protocol, encoder, decoder, codec base types, setup, debug utilities. |
| `K13A.TSMP.Udon` | Network behaviour base type and built-in TSMP network components. |

Unity-facing component names keep the `TSMP` prefix. Internal helper types may not.

## Recommended development loop

1. Implement the smallest component or codec path.
2. Run `Apply Setup`.
3. Confirm payload bytes and message counts in `TSMPDebugCanvas`.
4. Test Unity Play mode.
5. Compile UdonSharp.
6. Test in a real VRChat runtime path when the feature depends on Udon or capture behaviour.
