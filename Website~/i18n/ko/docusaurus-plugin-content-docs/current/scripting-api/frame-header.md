---
title: FrameHeader API
---

# `FrameHeader`

`FrameHeader`는 모든 TSMP frame 시작 부분의 fixed 56-byte header를 설명합니다. Decoder가 payload bytes를 읽기 전에 protocol version, codec, payload size, frame index, layout metadata를 식별할 수 있게 합니다.

## Header validation

Header reader는 다음을 검증합니다.

- TSMP magic.
- Header size.
- Protocol major version.
- Payload size와 layout.
- Header CRC32.

CRC validation이 실패하면 decoder는 frame을 버리고 warning을 log합니다. 실패한 header에서 payload bytes를 decode하지 않습니다.

## Important fields

| Field | 용도 |
| --- | --- |
| `Magic` | Frame이 TSMP임을 식별합니다. |
| `HeaderSize` | Fixed header byte length. |
| `VersionMajor` / `VersionMinor` | Protocol compatibility. |
| `FrameIndex` | Sender frame counter. |
| `PayloadSize` | Decode할 payload bytes 수. |
| `PayloadType` | Payload 해석 방식. Network frame은 TSMP network payload type을 사용합니다. |
| `CodecId` | Codec handler를 선택합니다. |
| `StreamId` | Logical stream identifier. |
| `LayoutId` | Logical payload layout identifier. |
| `BlockSize` | Codec block size metadata. |
| `DecodeSampleSize` | Decoder sample size metadata. |
| `QuantizationMode` | Codec option 또는 legacy quantization byte. |
| `HeaderCrc32` | CRC field를 zero로 둔 상태의 header CRC32. |

## Reserved bytes

Header는 layout stability를 위해 reserved bytes를 유지합니다. Writer는 reserved bytes를 zero로 써야 합니다. Reader는 이후 protocol version이 정의하기 전까지 의미를 부여하지 않아야 합니다.

## Writer rule

Writer는 항상 CRC를 기록합니다.

1. Header bytes를 채웁니다.
2. CRC field에 zero를 씁니다.
3. CRC field 이전 header bytes로 CRC를 계산합니다.
4. 마지막 4 bytes에 CRC를 씁니다.

## Reader rule

Reader는 malformed frame에 대해 throw 대신 failed status를 반환해야 합니다. Runtime decoder는 diagnostics를 설정하고 payload decode를 안전하게 skip할 수 있습니다.
