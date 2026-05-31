---
title: 개발자 개요
---

# 개발자 개요

이 섹션은 TSMP를 확장하려는 개발자를 위한 내용입니다.

프리팹을 배치하고, 기본 컴포넌트로 동기화하고, 씬을 디버깅하는 정도라면 먼저 시작하기 섹션을 보세요. TSMP 코드에 직접 접근해야 할 때 이 섹션을 사용합니다.

## 확장할 수 있는 것

| 확장 | 사용 시점 | 시작 위치 |
| --- | --- | --- |
| Custom synchronized component | 기본 sync component가 필요한 데이터를 다루지 못할 때. | [Custom network behaviour](./custom-network-behaviour.md) |
| Field synchronization | Public field를 TSMP가 encode해야 할 때. | [Scripting API: TransSync](../scripting-api/transsync.md) |
| TSMP RPC event | Texture stream을 통해 delayed event를 보내야 할 때. | [Scripting API: TSMPNetworkBehaviour](../scripting-api/network-behaviour.md) |
| Codec package | 다른 byte-to-pixel 표현이 필요할 때. | [Custom codec](./custom-codec.md) |
| Codec shader | 전용 encode/decode shader logic이 필요할 때. | [Codec implementation guide](./codec-implementation.md) |
| Protocol tooling | Test, validator, debug tool이 필요할 때. | [Frame algorithms](./frame-algorithms.md) |

Member-level API는 별도의 [Scripting API](../scripting-api/index.md) 섹션을 참고하세요.

## 세부 경로

| 목표 | 읽을 페이지 |
| --- | --- |
| Synchronized variables 추가 | [사용자정의 network variables](./custom-network-variables.md) |
| Texture-stream delayed event 추가 | [사용자정의 network RPC](./custom-network-rpc.md) |
| Dense data를 하나의 field로 pack | [사용자정의 packed data](./custom-network-packed-data.md) |
| Codec package 작성 | [Codec package definition](./codec-package-definition.md) |
| Codec shader pass 작성 | [Codec shaders](./codec-shaders.md) |
| Byte recovery와 runtime support 검증 | [Codec validation](./codec-validation.md) |

## 호환성 목표

TSMP 코드는 두 환경에서 동작하도록 작성됩니다.

- Unity C# 및 editor runtime.
- UdonSharp compiled runtime.

같은 component가 보통 두 환경에서 모두 동작해야 합니다. UdonSharp가 compile할 수 없는 API를 피하고, reflection-heavy runtime logic보다 단순한 field, array, explicit method를 선호하세요.

## Extension 설계 규칙

- 씬에 노출되는 component는 명시적이고 inspect하기 쉬워야 합니다.
- 데이터는 `[TransSync]` field로 보내고 side effect는 넣지 않습니다.
- 고빈도 값은 Udon bridge cost를 줄이기 위해 `byte[]`로 pack합니다.
- 씬 구조가 바뀌면 `TSMPSetup`으로 binding을 다시 만듭니다.
- Core나 encoder code에서 optional codec package를 hard-code하지 않습니다.
- Custom codec은 `TSMPCodec` API 뒤에 둡니다.
- Texture path는 신뢰할 수 없다고 보고 debug counter로 검증합니다.

## Namespace 구조

| Namespace | 용도 |
| --- | --- |
| `K13A.TSMP` | Core runtime, protocol, encoder, decoder, codec base types, setup, debug utilities. |
| `K13A.TSMP.Udon` | Network behaviour base type과 built-in TSMP network components. |

Unity에 노출되는 component 이름은 `TSMP` prefix를 유지합니다. 내부 helper type은 아닐 수 있습니다.

## 권장 개발 루프

1. 가장 작은 component 또는 codec path를 구현합니다.
2. `Apply Setup`을 실행합니다.
3. `TSMPDebugCanvas`에서 payload bytes와 message count를 확인합니다.
4. Unity Play mode에서 테스트합니다.
5. UdonSharp를 compile합니다.
6. Udon 또는 capture behaviour에 의존하는 기능은 실제 VRChat runtime path에서 테스트합니다.
