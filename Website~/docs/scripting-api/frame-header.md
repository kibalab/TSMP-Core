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

The header keeps reserved bytes for layout stability. Writers should set reserved bytes to zero. Readers should not attach meaning to reserved bytes unless a later protocol version defines them.

## Writer rule

The writer always writes a CRC:

1. Fill header bytes.
2. Write zero to the CRC field.
3. Compute CRC over the header bytes before the CRC field.
4. Write the CRC into the final four bytes.

## Reader rule

The reader should return a failed status instead of throwing for malformed frames. Runtime decoder code can then set diagnostics and skip payload decode safely.
