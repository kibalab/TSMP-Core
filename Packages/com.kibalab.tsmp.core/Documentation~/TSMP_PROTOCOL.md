# TSMP Protocol

상태: Draft
버전: frame header 0.2 (payload CRC32), legacy header API 기본값 0.1
범위: 비디오 래스터 기반 generic networking frame 전송

## Overview

TSMP는 일반 영상 프레임 안에 byte payload를 시각 심볼로 인코딩해 전송하는 프로토콜이다.

```text
Network Behaviours
-> TSMP Encoder
-> Video Stream
-> TSMP Decoder
-> Network Behaviours
```

현재 payload 의미 계층은 하나만 사용한다.

- `PayloadType.NetworkFrame = 0x0100`

Transform, Animator, Humanoid pose 동기화는 별도 frame payload type이 아니라 `NetworkFrame` 안의 변수 동기화 또는 RPC 위에서 구현한다.

## Raster Model

TSMP는 영상 프레임을 고정 크기 block grid로 본다.

- block size: 기본 8x8 pixel
- source frame: 예: 640x360
- active width blocks: `sourceWidth / blockSize`
- active height blocks: `sourceHeight / blockSize`

각 block은 하나의 symbol을 표현한다. Luma4는 코어가 제공하는 기본 symbol mode이며, 추가 symbol mode는 codec package가 정의한다.

| Symbol Mode | Value | Bits / Symbol |
| --- | ---: | ---: |
| Luma4 | 0 | 4 |

## Frame Layout

```text
Block rows:

0          finder / calibration / reserved
1          luma header calibration
2..4       fixed-size TSMP frame header
5..N       codec-owned calibration rows, if needed
N..end     payload bytes
```

The exact payload start row is owned by the selected codec. Core Luma4 starts at row `5`.

## Frame Header

All integers are little endian. Header size is fixed to 56 bytes in v0.2.

| Offset | Bytes | Field |
| --- | ---: | --- |
| 0 | 4 | Magic |
| 4 | 1 | Version major |
| 5 | 1 | Version minor |
| 6 | 1 | Profile |
| 7 | 1 | Symbol mode |
| 8 | 2 | Header size |
| 10 | 2 | Flags |
| 12 | 2 | Block size |
| 14 | 2 | Active width blocks |
| 16 | 2 | Active height blocks |
| 18 | 2 | Layout ID |
| 20 | 4 | Stream ID |
| 24 | 4 | Frame index |
| 28 | 4 | Timestamp milliseconds |
| 32 | 2 | Codec ID |
| 34 | 1 | Codec option length |
| 35 | 5 | Codec options |
| 40 | 2 | Payload type |
| 42 | 2 | Payload size |
| 44 | 4 | Payload CRC32 when flag bit 0 is set; legacy writes zero |
| 48 | 2 | Reserved, zero |
| 50 | 1 | Decode sample size |
| 51 | 1 | Legacy quantization option |
| 52 | 4 | Header CRC32 |

Field notes:

- `Magic`: `0x504D5354`
- `Symbol`: codec-owned symbol mode byte. Core reserves `0` for Luma4.
- `Flags`: bit 0 is `FrameFlags.PayloadCrc32`; production encoders set it for protected frames. Legacy header APIs default to `0`.
- `Codec Id`: selects the codec runtime handler. Core reserves `0` for Luma4.
- `OptLen`: number of valid codec option bytes, max `5`
- `Codec Options`: codec-owned option bytes. Core does not interpret them.
- `Payload Type`: currently `NetworkFrame`
- `Payload Size`: decoded logical payload bytes
- `Payload CRC32`: offsets 44..47, CRC32/IEEE over exactly `Payload Size` bytes of the logical payload, little endian
- `Reserved`: offsets 48..49; write `0`. Legacy 0.1 headers also write offsets 44..47 as zero.
- `Decode Sample`: block center sample size override, `0` means decoder default
- `Legacy Symbol Opts`: kept for older tooling; current codec-specific options use `Codec Options`
- `Header CRC32`: CRC32 over header bytes 0..51 with the CRC field itself written as zero before computation

Codec option bytes are documented by each codec package.

### Payload integrity

Production `TSMPEncoder` always writes protected header version 0.2, flag bit 0 and the payload CRC. The legacy header API default remains 0.1. NetworkFrame body version/encoding and payload capacity are unchanged; the checksum occupies four previously reserved header bytes rather than extending the payload.

Both checksums use the existing CRC32/IEEE implementation: reflected polynomial `0xEDB88320`, initial value `0xFFFFFFFF`, final XOR `0xFFFFFFFF`. The header CRC covers bytes 0..51, including the flag and payload checksum value. Encoders write the payload CRC before finalizing the header CRC.

For frames that proceed to payload processing, the current decoder validates an announced payload CRC after readback, before that frame refreshes prediction or applies variables/RPCs. A failed check discards the frame before dispatch. Sequential, predicted, combined-output and two-slot decoding use the same check, including payload-readback-only and parse-only modes. For protected frames, the decoded payload length must match the header `Payload Size` even with a manual layout/length override. Retained buffer tails are not part of the CRC.

`requirePayloadCrc32` defaults to false for legacy reception. When enabled, it rejects frames that do not announce a payload checksum. `lastPayloadCrcPresent` reports the announced flag and `lastPayloadCrcVerified` reports a completed successful payload check. Header-only mode and duplicate/older frames skipped before payload processing are not payload-verified. The predicted skip path may refresh only metadata from a CRC-valid header without accepting or applying its body. A default-accepted legacy frame can have both indicators false.

## NetworkFrame Payload

```text
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| NetMaj| NetMin|          Message Count                         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       Network Frame Sequence                   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
:                           Messages...                         :
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

Each message:

```text
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|          Network Id           | MsgType| Flags |   Sequence    |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|          Body Length          |             Body...            :
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

Message types:

- `VariableState = 1`
- `RpcCall = 2`

## VariableState Body

```text
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                         Variable Hash                         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| ValueType | Reserved|          Value Length                    |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
:                            Value Bytes                        :
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

The body may contain multiple variable entries.

Supported value types:

- `Bool`
- `Int32`
- `Float32`
- `Vector2`
- `Vector3`
- `Quaternion`
- `UTF8String`
- `RawBytes`
- common arrays of the above numeric/string types

## RpcCall Body

```text
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                           Rpc Hash                            |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| Arg Count | Reserved|               Arguments...              :
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

Each argument uses the same typed value format as `VariableState`.

## Binding Model

`NetworkId` selects a `TSMPNetworkBehaviour` target. `VariableHash` selects a generated binding entry for a `[TransSync]` field.

The Udon runtime does not inspect attributes. Binding tables are generated in the Unity editor and serialized onto decoders.

## Compatibility Rules

- Current production encoder frames use header 0.2 with payload CRC; legacy header-building APIs retain the 0.1 default without it.
- An older decoder can read the unchanged 56-byte layout and header CRC of a protected 0.2 frame, but it does not verify payload CRC. Protection requires a current decoder.
- A current decoder accepts frames without payload CRC by default; those legacy payloads remain unprotected. Enable `requirePayloadCrc32` when that compatibility behavior is not wanted.
- Decoders must reject unsupported `PayloadType`.
- Decoders must ignore unsupported codec option bytes beyond the handler's known range.
- Encoders must write unused codec option bytes as zero.
- New field-level protocols should be implemented as `VariableState` or `RpcCall` body formats, not as new TSMP frame payload types unless the raster/frame layer itself changes.

CRC detection does not recover a rejected frame or change the finite RPC-repeat/cache and frame-window restart contracts. This extension does not establish live transport rates, actual VRChat client execution or multi-avatar performance.
