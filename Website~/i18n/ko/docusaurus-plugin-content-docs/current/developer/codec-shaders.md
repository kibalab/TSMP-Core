---
title: Codec Shaders
---

# Codec shaders

TSMP codec shader는 payload bytes와 visible frame pixels 사이를 변환합니다. 기본 Luma4 codec을 reference path로 볼 수 있지만, custom codec은 자신만의 material과 shader pass를 사용할 수 있습니다.

## Encode side

Encode shader 또는 material은 보통 다음 input을 받습니다.

| Input | 용도 |
| --- | --- |
| Payload byte texture 또는 buffer texture | Encoder가 배치한 source bytes. |
| Header pixels | Core encoder가 준비한 header area. |
| Codec option bytes | 작은 codec-specific settings. |
| Block size | Symbol output용 pixel block dimensions. |
| Payload size | 그려야 하는 valid bytes 수. |

Encode result는 header를 읽을 수 있게 유지하고, codec이 보고한 layout에 payload symbols를 배치해야 합니다.

## Decode side

Decode shader 또는 material은 보통 다음 input을 받습니다.

| Input | 용도 |
| --- | --- |
| Source TSMP frame | Camera, OBS, Spout 등에서 받은 captured texture. |
| Header metadata | Payload size, block size, sample size, codec option bytes. |
| Payload layout | Start row/block and block count. |
| Byte output texture | Readback할 recovered bytes를 저장하는 texture. |

Decoder는 header의 payload byte count만 읽습니다. Unused pixels가 결과에 영향을 주면 안 됩니다.

Recovered bytes를 RGBA byte texture로 출력하는 decode shader는 [`TSMPDecodeByteOutput.cginc` scripting API](../scripting-api/decode-byte-output.md)를 참고하세요.

## Shader include paths

Package shader는 package-stable include path를 사용해야 합니다. Scene folder 위치에 의존하는 relative include path는 package 위치가 바뀔 때 깨지기 쉽습니다.

VPM package로 설치되어도 동작하는 include 방식을 선호하세요.

## Precision rules

- Header sampling은 정확해야 합니다.
- Payload symbol에는 filtering을 피하세요.
- Byte recovery는 point sampling을 사용하세요.
- 현재 payload가 사용하는 output pixel은 모두 clear 또는 overwrite하세요.
- 가능하면 unused output pixel도 deterministic하게 유지해 stale block을 피하세요.

## Material properties

Custom codec은 shader properties를 설명해야 합니다. 흔한 property는 다음과 같습니다.

| Property | Meaning |
| --- | --- |
| `_MainTex` | Source frame 또는 payload texture. |
| `_PayloadByteTexture` | Intermediate byte texture. |
| `_PayloadSize` | Valid payload byte count. |
| `_BlockSize` | Codec block size. |
| `_SampleSize` | Decode sample size. |
| `_CodecOptionBytes` | Codec-specific option vector. |

Editor setup과 runtime code가 special case 없이 assign할 수 있도록 stable property name을 사용하세요.

## Validation

Codec shader 배포 전 확인하세요.

- Deterministic byte sequence encode/decode.
- Non-full payload sizes.
- Codec switch 후 stale block 없음.
- Capture scaling disabled 상태.
- 실제 사용자가 쓸 render texture size.
