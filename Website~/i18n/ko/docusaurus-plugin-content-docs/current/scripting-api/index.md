---
title: 스크립팅 API 개요
---

# Scripting API overview

이 섹션은 TSMP public scripting API 레퍼런스입니다.

다음 작업을 할 때 사용하세요.

- Custom `TSMPNetworkBehaviour` 작성.
- Packed `[TransSync]` component 작성.
- TSMP RPC interaction 작성.
- Codec handler 작성.
- Frame data를 검사하는 test 또는 tool 작성.

튜토리얼 방식 설명은 먼저 Developer 섹션을 읽으세요. 이 섹션은 이름, field, method, 사용 규칙에 집중합니다.

## Main namespaces

| Namespace | 내용 |
| --- | --- |
| `K13A.TSMP` | Core runtime, protocol, setup, encoder, decoder, codec base classes. |
| `K13A.TSMP.Udon` | Network behaviour base class와 built-in sync components. |

## Common imports

```csharp
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
```

## API categories

| Page | 용도 |
| --- | --- |
| `TSMPBehaviour` | Base behaviour, Udon/Mono bridge methods, logging helpers. |
| `TSMPEncoder` | Frame encode lifecycle, output texture, codec selection, queued RPC, writer API. |
| `TSMPDecoder` | Source texture readback, header validation, codec dispatch, variable/RPC application. |
| `TSMPSetup` | Scene binding, codec discovery, render texture setup, one-click configuration. |
| `TSMPNetworkBehaviour` | Network IDs, receive interpolation, lifecycle hooks, TSMP RPC. |
| `TransSync` | Field-level synchronization attribute와 supported value types. |
| `Built-in network components` | Transform, humanoid pose, avatar pose, animator, timeline, blendshape, toggle components. |
| `TSMPCodec` | Codec handler contract와 runtime/editor methods. |
| `TSMPCodecCatalog` | Setup에 표시되는 installed codec discovery와 package metadata. |
| `TSMPDecodeCommon.cginc` | Decode shader 공용 uniforms, vertex types, sampling helpers, YCoCg helpers. |
| `TSMPDecodeByteOutput.cginc` | Decoded bytes를 RGBA byte texture에 쓰는 shader include contract. |
| `Protocol API` | Frame headers, network frame helpers, binary helpers, hashes, CRC. |

## Search tips

검색창에는 `SendTransRPC`, `TransSync`, `FrameHeader`, `NetworkValueType`, `codecId`, `payloadBytes`처럼 정확한 identifier를 입력하세요. API 페이지는 component name과 member name 양쪽으로 찾기 쉽도록 중요한 이름을 heading과 table에 반복해서 둡니다.

## UdonSharp note

일부 API는 같은 코드가 Unity C#과 UdonSharp에서 동작하도록 하기 위해 존재합니다. Bridge 또는 runtime path로 표시된 API를 사용할 때는 UdonSharp compiler 제약을 고려하세요.
