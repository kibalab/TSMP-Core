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
| `12` | 8 | Block 및 active layout 정보. |
| `20` | 4 | Stream ID. |
| `24` | 4 | Frame index. |
| `28` | 4 | Timestamp in milliseconds. |
| `32` | 8 | Codec ID와 codec options. |
| `40` | 2 | Payload type. |
| `42` | 2 | Payload size in bytes. |
| `44` | 6 | Reserved payload bytes. |
| `50` | 1 | Decode sample size. |
| `51` | 1 | Quantization mode. |
| `52` | 4 | Header CRC32. |

CRC는 CRC field를 0으로 둔 상태에서 bytes `0..51`에 대해 계산됩니다. 수신자가 계산한 CRC가 다르면 프레임을 버리고 경고를 기록합니다.

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

Header size는 56바이트로 고정되어 있습니다. Reserved bytes는 이후 호환성을 위해 유지되며 현재 encoder는 0으로 써야 합니다.

현재 wire format에는 payload copy와 FEC가 없습니다. 현재 프레임은 하나의 payload body만 운반합니다.
