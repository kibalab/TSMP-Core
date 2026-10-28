---
title: 프레임 포맷
---

# 프레임 포맷

TSMP는 하나의 바이너리 프레임을 인코딩 텍스처에 씁니다. 코덱은 바이트가 픽셀이 되는 방식을 정하고, 프레임 포맷은 그 바이트의 의미를 정합니다.

일반 사용자는 프레임 포맷을 직접 수정할 필요가 없습니다. 하지만 CRC 오류, 페이로드 용량, 메시지 수, 커스텀 코덱을 디버깅할 때 구조를 이해하면 도움이 됩니다.

## 전체 프레임

```text
+----------------------+-------------------------------+----------------------+
| Frame header         | Frame body / payload          | Unused texture area  |
| 56 bytes             | PayloadSize bytes             | codec/padding space  |
+----------------------+-------------------------------+----------------------+
```

- Header는 수신자가 프레임을 읽는 방법을 알려줍니다.
- Body에는 동기화 값과 RPC 호출이 들어 있습니다.
- 남은 텍스처 용량은 디코더가 무시합니다.
- 사용하지 않는 영역은 오래된 데이터가 현재 payload처럼 보이지 않도록 clear되거나 무시되어야 합니다.

## 프레임 헤더

```text
byte 0                                                        byte 55
+--------+---------+---------+---------+---------+----------+---------+
| Magic  | Version | Layout  | Stream  | Codec   | Payload  | CRC32   |
| 0..3   | 4..11   | 12..19  | 20..31  | 32..39  | 40..51   | 52..55  |
+--------+---------+---------+---------+---------+----------+---------+
```

주요 header field:

| Offset | Size | 의미 |
| --- | ---: | --- |
| `0` | 4 | TSMP를 식별하는 magic value. |
| `4` | 2 | Protocol major/minor version. |
| `7` | 1 | 코덱이 사용하는 symbol mode. |
| `8` | 2 | Header size. 현재 값은 `56`. |
| `10` | 2 | Frame flags. bit 0 (`FrameFlags.PayloadCrc32`)이 payload CRC32 존재를 표시합니다. |
| `12` | 8 | Block 및 active layout 정보. |
| `20` | 4 | Stream ID. |
| `24` | 4 | Frame index. |
| `28` | 4 | Timestamp in milliseconds. |
| `32` | 8 | Codec ID와 codec options. |
| `40` | 2 | Payload type. |
| `42` | 2 | Payload size in bytes. |
| `44` | 4 | 보호 프레임의 payload CRC32. Legacy header는 0. |
| `48` | 2 | Reserved. 0으로 기록합니다. |
| `50` | 1 | Decode sample size. |
| `51` | 1 | Quantization mode. |
| `52` | 4 | Header CRC32. |

CRC는 CRC field를 0으로 둔 상태에서 bytes `0..51`에 대해 계산됩니다. 수신자가 계산한 CRC가 다르면 프레임을 버리고 경고를 기록합니다.

## Payload 무결성 {#payload-integrity}

실제 `TSMPEncoder`가 생성하는 프레임은 프로토콜 **0.2**를 사용하며 항상 flag bit 0을 설정합니다. `44..47`바이트에 정확히 `PayloadSize`만큼의 payload를 계산한 little-endian CRC32/IEEE를 기록합니다. 기존 header CRC는 flag와 payload CRC 필드도 보호합니다. 56바이트 header, payload 용량과 NetworkFrame 메시지 형식은 유지되며 body 뒤에 checksum 바이트를 추가하지 않습니다.

Payload 처리로 진행한 프레임은 readback 후 해당 프레임의 prediction 갱신이나 변수·RPC 적용 전에 표시된 payload CRC를 검증합니다. 불일치하면 적용 전에 프레임을 거부합니다. 순차·예측·combined-output·두 슬롯 경로와 payload-readback-only·parse-only 모드에 적용됩니다. 보호 프레임은 수동 layout에서도 실제 디코딩 길이가 header의 `PayloadSize`와 일치해야 합니다.

Legacy header API의 기본값은 **0.1**이며 payload CRC flag가 없고 `44..49`바이트가 0입니다. 현재 decoder는 호환성을 위해 이 프레임을 기본 수용합니다. checksum이 없는 송신기를 거부하려면 `requirePayloadCrc32`를 켭니다. Header-only 모드는 body를 읽거나 검증하지 않습니다. Payload 처리 전에 건너뛴 중복·과거 프레임도 payload 검증 완료가 아닙니다. 예측 경로는 이때 CRC를 통과한 header 메타데이터만 갱신할 수 있으며 body를 채택하거나 적용하지 않습니다. `lastPayloadCrcPresent`는 flag 존재, `lastPayloadCrcVerified`는 완료된 payload 검증 성공을 나타냅니다.

## 프레임 바디

일반 동기화에서 payload type은 TSMP network frame입니다.

```text
+-----------------------+---------------------------------------------+
| Network frame header  | Message 0 | Message 1 | ... | Message N     |
| 8 bytes               | variable state or RPC call                  |
+-----------------------+---------------------------------------------+
```

Network frame header:

| Offset | Size | 의미 |
| --- | ---: | --- |
| `0` | 1 | Network payload major version. |
| `1` | 1 | Network payload minor version. |
| `2` | 2 | Message count. |
| `4` | 4 | Network sequence. |

각 message는 8바이트 message header로 시작합니다.

```text
+------------+------+-------+----------+-------------+
| Network ID | Type | Flags | Sequence | Body length |
| 2 bytes    | 1    | 1     | 2 bytes  | 2 bytes     |
+------------+------+-------+----------+-------------+
```

Message body는 다음 중 하나입니다.

- Variable state: `TransSync` field 값.
- RPC call: `SendTransRPC`로 queue된 method hash와 encoded arguments.

## Variable state body

Variable state message는 하나의 network behaviour에 대한 하나 이상의 field 값을 담습니다.

```text
+----------------+------------------+------------------+
| Variable count | Variable value 0 | Variable value N |
| 2 bytes        | 7-byte header + data                 |
+----------------+------------------+------------------+
```

각 값은 다음을 저장합니다.

- Stable variable hash.
- Value type.
- Byte length.
- Encoded value bytes.

빈 variable state message는 쓰지 않습니다. 현재 프레임에서 enabled field가 하나도 없는 behaviour는 encoder가 해당 message를 rollback합니다.

## RPC body

RPC message에는 다음이 들어 있습니다.

- Stable method hash.
- Argument count.
- Encoded argument values.

RPC-only frame은 유효합니다. 즉 variable state가 바뀌지 않은 프레임에도 짧은 이벤트만 보낼 수 있습니다.

## 디버깅할 때 볼 것

- Frame index가 바뀌면 송신자가 프레임을 만들고 있습니다.
- Payload size가 0이면 동기화 데이터가 쓰이지 않았습니다.
- CRC가 실패하면 텍스처 경로가 픽셀을 바꾸거나 잘못된 영역을 읽고 있습니다.
- Message count가 예상보다 낮으면 setup binding이나 payload capacity를 확인해야 합니다.
- TX에는 RPC가 있는데 RX에는 없다면 frame loss와 duplicate-frame 처리를 확인하세요.

## 호환성 메모

구 decoder는 보호된 0.2 프레임의 동일한 56바이트 layout을 읽지만 payload checksum은 무시합니다. 검증을 사용하려면 현재 encoder와 decoder를 함께 사용하세요. 현재 decoder가 수용한 legacy 프레임의 payload는 계속 무보호입니다. CRC 검출이 실시간 전송률, 아바타 성능, 재전송이나 재시작·세션 동작을 보장하지는 않습니다.

현재 wire format에는 payload copy와 FEC가 없습니다. 현재 프레임은 하나의 payload body만 운반합니다.
