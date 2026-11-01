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

Header 0.2는 flag bit 0 (`FrameFlags.PayloadCrc32`)과 `44..47`바이트를 payload CRC32에 사용하며, `48..49`는 reserved로 0을 유지합니다. `FrameHeader.PayloadCrc32`가 checksum 값을 담습니다. Legacy 0.1 header는 flag가 없고 `44..49`가 0입니다. 56바이트 layout은 유지됩니다. [Payload 무결성과 호환성](../concepts/protocol.md#payload-integrity)을 참고하세요.

## Writer rule

Writer는 항상 CRC를 기록합니다.

1. Header bytes를 채웁니다.
2. CRC field에 zero를 씁니다.
3. CRC field 이전 header bytes로 CRC를 계산합니다.
4. 마지막 4 bytes에 CRC를 씁니다.

`CreateDefault()`는 legacy 0.1 기본값을 유지합니다. `FrameFlags.PayloadCrc32`를 설정한 `WriteTo`는 전달된 `PayloadCrc32`를 기록하고 minor version을 최소 0.2로 올린 뒤 header CRC를 완성합니다. payload 인자가 없으므로 payload checksum 자체를 계산하지는 않습니다. `FrameHeaderWriter.TryWritePayloadCrc32`는 정확한 payload 바이트로 계산하고 flag·version·header CRC를 갱신합니다. 실제 encoder는 이를 자동 호출합니다.

## Reader rule

Reader는 malformed frame에 대해 throw 대신 failed status를 반환해야 합니다. Runtime decoder는 diagnostics를 설정하고 payload decode를 안전하게 skip할 수 있습니다.

`FrameHeader.TryRead`는 header를 검증하고 payload는 검증하지 않습니다. `FrameHeaderReader.TryValidatePayload`가 표시된 payload checksum을 별도로 검사하며 runtime decoder는 body 채택·적용 전에 실행합니다. Header-only와 프레임 순서 skip은 body를 검사하지 않으며, 예측 skip은 CRC를 통과한 header 메타데이터만 갱신할 수 있습니다. Decoder가 checksum을 필수로 요구하지 않으면 legacy 프레임은 기본 수용됩니다.
