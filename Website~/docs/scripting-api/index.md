---
title: Scripting API Overview
---

# Scripting API overview

This section is a reference for public TSMP scripting APIs.

Use it when writing:

- A custom `TSMPNetworkBehaviour`.
- A packed `[TransSync]` component.
- A TSMP RPC interaction.
- A codec handler.
- Tests or tools that inspect frame data.

For tutorial-style explanations, read the Developer section first. This section focuses on names, fields, methods, and usage rules.

## Main namespaces

| Namespace | Contents |
| --- | --- |
| `K13A.TSMP` | Core runtime, protocol, setup, encoder, decoder, codec base classes. |
| `K13A.TSMP.Udon` | Network behaviour base class and built-in sync components. |

## Common imports

```csharp
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
```

## API categories

| Page | Use it for |
| --- | --- |
| `TSMPBehaviour` | Base behaviour, Udon/Mono bridge methods, logging helpers. |
| `TSMPEncoder` | Frame encode lifecycle, output texture, codec selection, queued RPC, writer API. |
| `TSMPDecoder` | Source texture readback, header validation, codec dispatch, variable/RPC application. |
| `TSMPSetup` | Scene binding, codec discovery, render texture setup, one-click configuration. |
| `TSMPNetworkBehaviour` | Network IDs, receive interpolation, lifecycle hooks, TSMP RPC. |
| `TransSync` | Field-level synchronization attribute and supported value types. |
| `Built-in network components` | Transform, humanoid pose, avatar pose, animator, timeline, blendshape, toggle components. |
| `TSMPCodec` | Codec handler contract and runtime/editor methods. |
| `TSMPCodecCatalog` | Installed codec discovery and package metadata shown in Setup. |
| `TSMPDecodeCommon.cginc` | Shared decode shader uniforms, vertex types, sampling helpers, and YCoCg helpers. |
| `TSMPDecodeByteOutput.cginc` | Shader include contract for writing decoded bytes to an RGBA byte texture. |
| `Protocol API` | Frame headers, network frame helpers, binary helpers, hashes, CRC. |

## Search tips

Use the search box for exact identifiers such as `SendTransRPC`, `TransSync`, `FrameHeader`, `NetworkValueType`, `codecId`, or `payloadBytes`. API pages repeat important names in headings and tables so they can be found from either the component name or the member name.

## UdonSharp note

Some APIs exist to let the same code run in Unity C# and UdonSharp. If an API is marked as a bridge or runtime path, keep UdonSharp compiler limits in mind.
