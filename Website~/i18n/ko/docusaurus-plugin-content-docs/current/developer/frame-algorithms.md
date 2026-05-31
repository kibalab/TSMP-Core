---
title: Frame과 Payload 알고리즘
---

# Frame and payload algorithms

여기서는 runtime data flow를 높은 수준에서 설명합니다. Encoder/decoder 변경을 검토하거나, test를 추가하거나, 왜 frame message가 예상보다 적은지 디버깅할 때 유용합니다.

## Encoder frame flow

```text
EncodeNow
  -> active TSMPNetworkBehaviour targets 수집
  -> network payload 시작
  -> 각 behaviour:
       TSMPBeforeEncode 호출
       enabled TransSync fields 기록
       비어 있는 VariableState message 취소
  -> queued RPC messages 기록
  -> message count patch
  -> CRC가 포함된 frame header 기록
  -> codec에 header/payload를 output texture로 그리도록 요청
```

중요한 동작:

- 빈 `VariableState` message는 rollback됩니다.
- RPC-only frame은 유효합니다.
- Queued RPC는 여러 frame에 반복될 수 있습니다.
- 현재 wire format에는 payload copy와 FEC가 없습니다.
- 선택한 codec이 payload start row, capacity, symbol mode, option bytes를 제공합니다.

## Network payload layout

```text
+-----------------------+-----------------------------+
| Network frame header  | Message 0 | Message 1 | ... |
| 8 bytes               | variable state or RPC       |
+-----------------------+-----------------------------+
```

Network frame header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 1 | Network major version. |
| `1` | 1 | Network minor version. |
| `2` | 2 | Message count. |
| `4` | 4 | Sequence. |

Message header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 2 | Network ID. |
| `2` | 1 | Message type. |
| `3` | 1 | Flags. |
| `4` | 2 | Message sequence. |
| `6` | 2 | Body length. |

## Variable state message

```text
+----------------+------------------+------------------+
| Variable count | Variable value 0 | Variable value N |
| 2 bytes        | 7-byte header + data                 |
+----------------+------------------+------------------+
```

Variable value header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 4 | Variable hash. |
| `4` | 1 | Value type. |
| `5` | 2 | Value byte length. |

Decoder는 variable hash와 network ID를 generated binding entry와 매칭합니다.

Encoder rule: behaviour에 대해 variable이 하나도 쓰이지 않으면 message를 취소하고 message count를 증가시키지 않습니다.

## RPC message

```text
+----------+----------------+----------------+----------------+
| RPC hash | Argument count | Argument 0     | Argument N     |
| 4 bytes  | 1 byte         | 3-byte header + data             |
+----------+----------------+----------------+----------------+
```

RPC argument header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 1 | Value type. |
| `1` | 2 | Value byte length. |

Decoder는 payload data에서 method name을 확인하고 `TSMPBehaviour.SendCustomEvent`를 통해 event를 dispatch합니다.

RPC event는 texture frame loss를 견디기 위해 encoder가 작은 frame 수만큼 반복합니다.

## Header validation

Decoder validation order:

1. Buffer range.
2. Magic.
3. Header size.
4. Major version.
5. Header CRC32.

CRC는 header bytes `0..51`에 대해 계산됩니다. Stored CRC는 bytes `52..55`에 있습니다. Mismatch가 있으면 payload processing 전에 frame을 버립니다.

## Decode flow

```text
Update / decode tick
  -> texture에서 header 영역 읽기
  -> header와 CRC 검증
  -> codecId로 codec 선택
  -> PayloadSize만큼 payload bytes 요청
  -> network frame header decode
  -> messages 반복
  -> variable values 적용 또는 RPC dispatch
  -> diagnostics 갱신
```

Decoder는 fail closed 방식이어야 합니다. Malformed payload data는 partial invalid state를 적용하지 않고 payload processing을 멈춘 뒤 diagnostics를 남깁니다.

## Test를 추가할 위치

Protocol test 대상:

- Header CRC generation과 rejection.
- Message count patching.
- Empty variable message rollback.
- RPC-only frames.
- Payload capacity boundaries.
- Duplicate frame과 duplicate RPC handling.
