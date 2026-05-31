# TSMP Protocol

상태: Draft
버전: 0.2
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

```text
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                         Magic "TSMP"                          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| VerMaj| VerMin| Profile|Symbol |          Header Size          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|            Flags              |          Block Size            |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|      Active Width Blocks       |      Active Height Blocks      |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|          Layout Id             |             Stream Id          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                           Frame Index                          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                          Timestamp Ms                          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|          Codec Id             | OptLen |     Codec Options     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|     Codec Options (cont.)     |          Payload Type          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|          Payload Size          |          Reserved             |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|              Reserved          |          Reserved  | Sample    |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| Legacy Symbol Opts            |          Header CRC32          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

Field notes:

- `Magic`: `0x504D5354`
- `Symbol`: codec-owned symbol mode byte. Core reserves `0` for Luma4.
- `Flags`: currently reserved; write `0`
- `Codec Id`: selects the codec runtime handler. Core reserves `0` for Luma4.
- `OptLen`: number of valid codec option bytes, max `5`
- `Codec Options`: codec-owned option bytes. Core does not interpret them.
- `Payload Type`: currently `NetworkFrame`
- `Payload Size`: decoded logical payload bytes
- `Reserved`: offsets 44..49; write `0`
- `Decode Sample`: block center sample size override, `0` means decoder default
- `Legacy Symbol Opts`: kept for older tooling; current codec-specific options use `Codec Options`
- `Header CRC32`: CRC32 over header bytes 0..51 with the CRC field itself written as zero before computation

Codec option bytes are documented by each codec package.

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

- Decoders must reject unsupported `PayloadType`.
- Decoders must ignore unsupported codec option bytes beyond the handler's known range.
- Encoders must write unused codec option bytes as zero.
- New field-level protocols should be implemented as `VariableState` or `RpcCall` body formats, not as new TSMP frame payload types unless the raster/frame layer itself changes.
