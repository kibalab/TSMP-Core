---
title: FrameHeader API
---

# `FrameHeader`

`FrameHeader` describes the fixed 56-byte header at the start of every TSMP frame. It lets the decoder identify protocol version, codec, payload size, frame index, and layout metadata before reading payload bytes.

## Header validation

Header readers validate:

- TSMP magic.
- Header size.
- Protocol major version.
- Payload size and layout.
- Header CRC32.

If CRC validation fails, the decoder discards the frame and logs a warning. Payload bytes are not decoded from a failed header.

## Important fields

| Field | Purpose |
| --- | --- |
| `Magic` | Identifies the frame as TSMP. |
| `HeaderSize` | Fixed header byte length. |
| `VersionMajor` / `VersionMinor` | Protocol compatibility. |
| `FrameIndex` | Sender frame counter. |
| `PayloadSize` | Number of payload bytes to decode. |
| `PayloadType` | Payload interpretation. Network frames use the TSMP network payload type. |
| `CodecId` | Selects the codec handler. |
| `StreamId` | Logical stream identifier. |
| `LayoutId` | Logical payload layout identifier. |
| `BlockSize` | Codec block size metadata. |
| `DecodeSampleSize` | Decoder sample size metadata. |
| `QuantizationMode` | Codec option or legacy quantization byte. |
| `HeaderCrc32` | CRC32 of header bytes with the CRC field zeroed during calculation. |

## Reserved bytes

Header 0.2 uses flag bit 0 (`FrameFlags.PayloadCrc32`) and bytes `44..47` for the payload CRC32; `48..49` remain reserved and zero. `FrameHeader.PayloadCrc32` holds the checksum value. Legacy 0.1 headers have no flag and keep `44..49` zero. The 56-byte layout is unchanged. See [payload integrity and compatibility](../concepts/protocol.md#payload-integrity).

## Writer rule

The writer always writes a CRC:

1. Fill header bytes.
2. Write zero to the CRC field.
3. Compute CRC over the header bytes before the CRC field.
4. Write the CRC into the final four bytes.

`CreateDefault()` retains the legacy 0.1 default. With `FrameFlags.PayloadCrc32` set, `WriteTo` writes the supplied `PayloadCrc32`, promotes the minor version to at least 0.2, then finalizes header CRC. It does not compute the payload checksum because it has no payload argument. `FrameHeaderWriter.TryWritePayloadCrc32` computes it from the exact payload bytes and updates the flag, version and header CRC. Production encoders call this automatically.

## Reader rule

The reader should return a failed status instead of throwing for malformed frames. Runtime decoder code can then set diagnostics and skip payload decode safely.

`FrameHeader.TryRead` validates the header, not the payload. `FrameHeaderReader.TryValidatePayload` checks the announced payload checksum separately; runtime decoding performs that check before accepting or applying a body. Header-only and frame-order skips do not verify the body, and a predicted skip may refresh only CRC-valid header metadata. Legacy frames remain accepted by default unless the decoder requires a checksum.
