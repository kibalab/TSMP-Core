---
title: TSMPSetup API
---

# `TSMPSetup`

`TSMPSetup`은 encoder, decoder, codec handlers, bindings, render textures, materials가 서로 맞도록 scene을 설정합니다.

대부분 inspector에서 사용하지만, script나 editor tool에서도 같은 public method를 호출할 수 있습니다.

## Main references

| Field | 용도 |
| --- | --- |
| `encoder` | Configure할 `TSMPEncoder`. |
| `decoder` | Configure할 `TSMPDecoder`. |
| `sourceTexture` | Sender side에서 필요한 source texture. |
| `decoderSourceTexture` | Decoder가 읽는 texture. |
| `preferEncoderOutputAsDecoderSource` | Loopback test에서 encoder output을 decoder source로 사용합니다. |
| `encoderOutput` | Encoder에 할당되는 output render texture. |
| `payloadByteTexture` | Decoder에 할당되는 byte texture. |

## Frame settings

| Field | 용도 |
| --- | --- |
| `width` / `height` | Texture에서 size를 얻지 못할 때 쓰는 fallback frame size. |
| `blockSize` | Compatible codec에서 쓰는 pixel block size. |
| `sampleSize` | Header/decode sample size. |
| `flipY` | Decoder-side image flip. |

Output과 source texture가 할당되어 있으면, 반복 입력 대신 texture size를 기준으로 설정할 수 있습니다.

## Codec discovery

| Field | 용도 |
| --- | --- |
| `autoDiscoverCodecs` | Installed codec catalog와 prefab entry를 찾습니다. |
| `codecPrefabs` | Scene에서 사용할 수 있는 codec prefabs. |
| `selectedCodecIndex` | 선택된 codec entry. |
| `codecInstanceRoot` | Codec instances의 parent object. |

Codec tab은 catalog가 제공하는 package name, author, description 같은 metadata를 표시할 수 있습니다.

## Render texture setup

| Field | 용도 |
| --- | --- |
| `resizeFrameRenderTextures` | Frame textures를 setup dimension에 맞게 resize합니다. |
| `resizeByteRenderTextures` | Decoder readback용 byte texture를 resize합니다. |
| `autoSizeByteTexture` | Payload capacity로 byte texture size를 계산합니다. |
| `roundByteTextureWidthToPowerOfTwo` | Byte texture width를 power-of-two 친화적으로 만듭니다. |
| `byteTextureSafetyPixels` | Edge overflow를 피하기 위해 extra pixels를 더합니다. |

## `ApplyNow()`

```csharp
public void ApplyNow()
```

모든 setup option을 즉시 적용합니다. Codec instances refresh, encoder/decoder reference assignment, network binding rebuild, texture update, codec configuration을 수행합니다.

다음 변경 후 실행하세요.

- `TSMPNetworkBehaviour` component 추가/삭제.
- Network ID 변경.
- Codec selection 변경.
- Output 또는 source texture 변경.
- Package import 변경.

## `EncodeEncoderNow()`

```csharp
public void EncodeEncoderNow()
```

Configured encoder의 `EncodeNow()`를 호출합니다. 주 용도는 editor-time frame preview입니다.

## `RefreshInstalledCodecs()`

```csharp
public void RefreshInstalledCodecs()
```

Installed codec catalogs를 scan하고 codec list를 갱신합니다. Codec package를 import하거나 제거한 후 사용하세요.

## Setup prefab

일반 scene은 아래 prefab에서 시작하세요.

```text
Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab
```

Prefab을 배치하고, transport path용 texture를 할당하고, codec을 선택한 뒤 `Apply Setup`을 실행합니다.
